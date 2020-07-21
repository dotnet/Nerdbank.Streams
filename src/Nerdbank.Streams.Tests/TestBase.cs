// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public abstract class TestBase : IDisposable
{
    protected static readonly TimeSpan ExpectedTimeout = TimeSpan.FromMilliseconds(200);

    protected static readonly TimeSpan UnexpectedTimeout = Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(10);

    private const bool WriteTestOutputToFile = false;

    private readonly ProcessJobTracker processJobTracker = new ProcessJobTracker();

    private readonly CancellationTokenSource timeoutTokenSource;

    private readonly Stopwatch testStopwatch;

    private readonly Random random = new Random();

    private CancellationTokenRegistration timeoutLoggerRegistration;

    protected TestBase(ITestOutputHelper logger)
    {
        this.Logger = WriteTestOutputToFile ? new FileLogger($@"d:\temp\test.{DateTime.Now:HH-mm-ss.ff}.log", logger) : logger;
        this.testStopwatch = Stopwatch.StartNew();
        this.timeoutTokenSource = new CancellationTokenSource(TestTimeout);
        this.timeoutLoggerRegistration = this.timeoutTokenSource.Token.Register(() => logger.WriteLine("**Timeout token signaled** (Time index: {0:HH:mm:ss.ff}, Elapsed: {1})", DateTime.Now, this.ElapsedTime));
    }

    public static CancellationToken ExpectedTimeoutToken => new CancellationTokenSource(ExpectedTimeout).Token;

    protected ITestOutputHelper Logger { get; }

    protected CancellationToken TimeoutToken => Debugger.IsAttached ? CancellationToken.None : this.timeoutTokenSource.Token;

    /// <summary>
    /// Gets the time the test has been running since its timeout timer started.
    /// </summary>
    protected TimeSpan ElapsedTime => this.testStopwatch.Elapsed;

    private static TimeSpan TestTimeout => UnexpectedTimeout;

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async Task ReadAsync(Stream stream, byte[] buffer, int? count = null, int offset = 0, bool isAsync = true)
    {
        Requires.NotNull(stream, nameof(stream));
        Requires.NotNull(buffer, nameof(buffer));

        count = count ?? buffer.Length;

        if (count == 0)
        {
            return;
        }

        int bytesRead = 0;
        while (bytesRead < count)
        {
            int bytesJustRead = isAsync
                ? await stream.ReadAsync(buffer, offset + bytesRead, count.Value - bytesRead, this.TimeoutToken).WithCancellation(this.TimeoutToken)
                : stream.Read(buffer, offset + bytesRead, count.Value - bytesRead);
            if (bytesJustRead == 0)
            {
                throw new EndOfStreamException();
            }

            bytesRead += bytesJustRead;
        }
    }

    public async ValueTask<ReadOnlySequence<byte>> ReadAtLeastAsync(PipeReader reader, int minLength)
    {
        Requires.NotNull(reader, nameof(reader));
        Requires.Range(minLength > 0, nameof(minLength));

        var bytesReceived = new Sequence<byte>();
        while (bytesReceived.Length < minLength)
        {
            var readResult = await reader.ReadAsync(this.TimeoutToken);
            foreach (var segment in readResult.Buffer)
            {
                var memory = bytesReceived.GetMemory(segment.Length);
                segment.CopyTo(memory);
                bytesReceived.Advance(segment.Length);
            }

            reader.AdvanceTo(readResult.Buffer.End);

            if (readResult.IsCompleted && bytesReceived.Length < minLength)
            {
                throw new EndOfStreamException($"PipeReader completed after reading {bytesReceived.Length} of the expected {minLength} bytes.");
            }
        }

        return bytesReceived.AsReadOnlySequence;
    }

    public async Task DrainAsync(PipeReader reader, long requiredLength)
    {
        Requires.NotNull(reader, nameof(reader));

        while (requiredLength > 0)
        {
            ReadResult readResult = await reader.ReadAsync(this.TimeoutToken);
            long bytesToConsume = Math.Min(requiredLength, readResult.Buffer.Length);
            reader.AdvanceTo(readResult.Buffer.GetPosition(bytesToConsume));
            requiredLength -= bytesToConsume;
            if (readResult.IsCompleted && requiredLength > 0)
            {
                throw new EndOfStreamException("End of stream encountered before we read all the expected bytes.");
            }
        }
    }

    public async Task DrainReaderTillCompletedAsync(PipeReader reader)
    {
        while (true)
        {
            var readResult = await reader.ReadAsync(this.TimeoutToken);
            reader.AdvanceTo(readResult.Buffer.End);
            if (readResult.IsCompleted)
            {
                break;
            }
        }
    }

    internal byte[] GetBuffer(int length)
    {
        var buffer = new byte[length];
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = 0xcc;
        }

        return buffer;
    }

    /// <summary>
    /// Executes the specified test method in its own process, offering maximum isolation from ambient noise from other threads
    /// and GC.
    /// </summary>
    /// <param name="testClassName">The full name of the test class.</param>
    /// <param name="testMethodName">The name of the test method.</param>
    /// <param name="logger">An optional logger to forward any <see cref="ITestOutputHelper"/> output to from the isolated test runner.</param>
    /// <returns>
    /// A task whose result is <c>true</c> if test execution is already isolated and should therefore proceed with the body of the test,
    /// or <c>false</c> after the isolated instance of the test has completed execution.
    /// </returns>
    /// <exception cref="Xunit.Sdk.XunitException">Thrown if the isolated test result is a Failure.</exception>
    /// <exception cref="SkipException">Thrown if on a platform that we do not yet support test isolation on.</exception>
    internal Task<bool> ExecuteInIsolationAsync(string testClassName, string testMethodName, ITestOutputHelper logger)
    {
        Requires.NotNullOrEmpty(testClassName, nameof(testClassName));
        Requires.NotNullOrEmpty(testMethodName, nameof(testMethodName));

#if NETFRAMEWORK
        const string testHostProcessName = "IsolatedTestHost.exe";
        if (Process.GetCurrentProcess().ProcessName == Path.GetFileNameWithoutExtension(testHostProcessName))
        {
            return TplExtensions.TrueTask;
        }

        // Pass in the original location of the test assembly so that the host can find the .config file.
        string testAssemblyPath = Path.Combine(Environment.CurrentDirectory, Path.GetFileName(Assembly.GetExecutingAssembly().Location));
        var startInfo = new ProcessStartInfo(
            testHostProcessName,
            AssembleCommandLineArguments(
                testAssemblyPath,
                testClassName,
                testMethodName,
                Debugger.IsAttached.ToString()))
        {
            RedirectStandardError = logger != null,
            RedirectStandardOutput = logger != null,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        Process isolatedTestProcess = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        var processExitCode = new TaskCompletionSource<IsolatedTestHost.ExitCodes>();
        isolatedTestProcess.Exited += (s, e) =>
        {
            processExitCode.SetResult((IsolatedTestHost.ExitCodes)isolatedTestProcess.ExitCode);
        };
        if (logger != null)
        {
            isolatedTestProcess.OutputDataReceived += (s, e) => logger.WriteLine(e.Data ?? string.Empty);
            isolatedTestProcess.ErrorDataReceived += (s, e) => logger.WriteLine(e.Data ?? string.Empty);
        }

        logger?.WriteLine("Test host launched with: \"{0}\" {1}", Path.GetFullPath(startInfo.FileName), startInfo.Arguments);
        Assert.True(isolatedTestProcess.Start());
        this.processJobTracker.AddProcess(isolatedTestProcess);

        if (logger != null)
        {
            isolatedTestProcess.BeginOutputReadLine();
            isolatedTestProcess.BeginErrorReadLine();
        }

        return processExitCode.Task.ContinueWith(
            t =>
            {
                switch (t.Result)
                {
                    case IsolatedTestHost.ExitCodes.TestSkipped:
                        throw new SkipException("Test skipped. See output of isolated task for details.");
                    case IsolatedTestHost.ExitCodes.TestPassed:
                    default:
                        Assert.Equal(IsolatedTestHost.ExitCodes.TestPassed, t.Result);
                        break;
                }

                return false;
            },
            TaskScheduler.Default);
#else
        return Task.FromException<bool>(new SkipException("Test isolation is not yet supported on this platform."));
#endif
    }

    /// <summary>
    /// Executes the specified test method in its own process, offering maximum isolation from ambient noise from other threads
    /// and GC.
    /// </summary>
    /// <param name="testClass">The instance of the test class containing the method to be run in isolation.</param>
    /// <param name="testMethodName">The name of the test method.</param>
    /// <param name="logger">An optional logger to forward any <see cref="ITestOutputHelper"/> output to from the isolated test runner.</param>
    /// <returns>
    /// A task whose result is <c>true</c> if test execution is already isolated and should therefore proceed with the body of the test,
    /// or <c>false</c> after the isolated instance of the test has completed execution.
    /// </returns>
    /// <exception cref="Xunit.Sdk.XunitException">Thrown if the isolated test result is a Failure.</exception>
    /// <exception cref="SkipException">Thrown if on a platform that we do not yet support test isolation on.</exception>
    internal Task<bool> ExecuteInIsolationAsync(object testClass, string testMethodName, ITestOutputHelper logger)
    {
        Requires.NotNull(testClass, nameof(testClass));
        return this.ExecuteInIsolationAsync(testClass.GetType().FullName!, testMethodName, logger);
    }

    protected static Task WhenAllSucceedOrAnyFail(params Task[] tasks)
    {
        var tcs = new TaskCompletionSource<int>();
        Task.WhenAll(tasks).ApplyResultTo(tcs);
        foreach (var task in tasks)
        {
            task.ContinueWith(t => tcs.TrySetException(t.Exception!), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default).Forget();
        }

        return tcs.Task;
    }

    protected static Task<T[]> WhenAllSucceedOrAnyFail<T>(params Task<T>[] tasks)
    {
        var tcs = new TaskCompletionSource<T[]>();
        Task.WhenAll(tasks).ApplyResultTo(tcs);
        foreach (var task in tasks)
        {
            task.ContinueWith(t => tcs.TrySetException(t.Exception!), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default).Forget();
        }

        return tcs.Task;
    }

    protected static void AssertNoFault(MultiplexingStream? stream)
    {
        Exception? fault = stream?.Completion.Exception?.InnerException;
        if (fault != null)
        {
            ExceptionDispatchInfo.Capture(fault).Throw();
        }
    }

    protected static async Task<int> ReadAtLeastAsync(Stream stream, ArraySegment<byte> buffer, int requiredLength, CancellationToken cancellationToken)
    {
        Requires.NotNull(stream, nameof(stream));
        Requires.NotNull(buffer.Array!, nameof(buffer));
        Requires.Range(requiredLength >= 0, nameof(requiredLength));

        int bytesRead = 0;
        while (bytesRead < requiredLength)
        {
            int bytesReadJustNow = await stream.ReadAsync(buffer.Array, buffer.Offset + bytesRead, buffer.Count - bytesRead, cancellationToken).ConfigureAwait(false);
            Assert.NotEqual(0, bytesReadJustNow);
            bytesRead += bytesReadJustNow;
        }

        return bytesRead;
    }

    /// <summary>
    /// Executes the specified test method in its own process, offering maximum isolation from ambient noise from other threads
    /// and GC.
    /// </summary>
    /// <param name="testMethodName">The name of the test method.</param>
    /// <returns>
    /// A task whose result is <c>true</c> if test execution is already isolated and should therefore proceed with the body of the test,
    /// or <c>false</c> after the isolated instance of the test has completed execution.
    /// </returns>
    /// <exception cref="Xunit.Sdk.XunitException">Thrown if the isolated test result is a Failure.</exception>
    /// <exception cref="SkipException">Thrown if on a platform that we do not yet support test isolation on.</exception>
    protected Task<bool> ExecuteInIsolationAsync([CallerMemberName] string? testMethodName = null)
    {
        return this.ExecuteInIsolationAsync(this, testMethodName!, this.Logger);
    }

    /// <summary>
    /// Executes the specified test method in its own process, offering maximum isolation from ambient noise from other threads
    /// and GC.
    /// </summary>
    /// <param name="testMethodName">The name of the test method.</param>
    /// <returns>
    /// <c>true</c> if test execution is already isolated and should therefore proceed with the body of the test,
    /// or <c>false</c> after the isolated instance of the test has completed execution.
    /// </returns>
    /// <exception cref="Xunit.Sdk.XunitException">Thrown if the isolated test result is a Failure.</exception>
    /// <exception cref="SkipException">Thrown if on a platform that we do not yet support test isolation on.</exception>
    protected bool ExecuteInIsolation([CallerMemberName] string? testMethodName = null)
    {
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
        return this.ExecuteInIsolationAsync(this, testMethodName!, this.Logger).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
    }

    protected async Task WaitForQuietPeriodAsync(bool completeSynchronously = false)
    {
        const int shortDelayDuration = 250;
        const int quietPeriodMaxAttempts = 3;
        const int quietThreshold = 1200;
        long waitForQuietMemory1, waitForQuietMemory2, waitPeriodAllocations, waitForQuietAttemptCount = 0;
        do
        {
            waitForQuietMemory1 = GC.GetTotalMemory(true);
            await MaybeShouldBlock(Task.Delay(shortDelayDuration), completeSynchronously);
            waitForQuietMemory2 = GC.GetTotalMemory(true);

            waitPeriodAllocations = Math.Abs(waitForQuietMemory2 - waitForQuietMemory1);
            this.Logger.WriteLine("Bytes allocated during quiet wait period: {0}", waitPeriodAllocations);
        }
        while (waitPeriodAllocations > quietThreshold || ++waitForQuietAttemptCount >= quietPeriodMaxAttempts);
        if (waitPeriodAllocations > quietThreshold)
        {
            this.Logger.WriteLine("WARNING: Unable to establish a quiet period.");
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.testStopwatch.Stop();
            this.timeoutLoggerRegistration.Dispose();
            this.processJobTracker.Dispose();
            (this.Logger as IDisposable)?.Dispose();
        }
    }

    protected byte[] GetRandomBuffer(int length)
    {
        var buffer = new byte[length];
        this.random.NextBytes(buffer);
        return buffer;
    }

    protected async Task TransmitAndVerifyAsync(Stream writeTo, Stream readFrom, byte[] data)
    {
        Requires.NotNull(writeTo, nameof(writeTo));
        Requires.NotNull(readFrom, nameof(readFrom));
        Requires.NotNull(data, nameof(data));

        await writeTo.WriteAsync(data, 0, data.Length, this.TimeoutToken).WithCancellation(this.TimeoutToken);
        await writeTo.FlushAsync().WithCancellation(this.TimeoutToken);
        await this.VerifyReceivedDataAsync(readFrom, data);
    }

    protected async Task VerifyReceivedDataAsync(Stream readFrom, byte[] data)
    {
        Requires.NotNull(readFrom, nameof(readFrom));
        Requires.NotNull(data, nameof(data));

        var readBuffer = new byte[data.Length * 2];
        int readBytes = await ReadAtLeastAsync(readFrom, new ArraySegment<byte>(readBuffer), data.Length, this.TimeoutToken);
        Assert.Equal(data.Length, readBytes);
        for (int i = 0; i < data.Length; i++)
        {
            Assert.Equal(data[i], readBuffer[i]);
        }
    }

    private static Task MaybeShouldBeComplete(Task task, bool shouldBeSynchronous)
    {
        Assert.True(task.IsCompleted || !shouldBeSynchronous);
        return task;
    }

    private static Task MaybeShouldBlock(Task task, bool shouldBlock)
    {
        if (shouldBlock)
        {
            task.GetAwaiter().GetResult();
        }

        return task;
    }

    private static string AssembleCommandLineArguments(params string[] args) => string.Join(" ", args.Select(a => $"\"{a}\""));

    private class FileLogger : ITestOutputHelper, IDisposable
    {
        private readonly StreamWriter file;
        private readonly ITestOutputHelper forwardTo;

        internal FileLogger(string fileName, ITestOutputHelper forwardTo)
        {
            this.file = new StreamWriter(File.OpenWrite(fileName));
            this.forwardTo = forwardTo;
        }

        public void WriteLine(string message)
        {
            this.file.WriteLine(message);
            this.forwardTo.WriteLine(message);
            Debug.WriteLine(message);
        }

        public void WriteLine(string format, params object[] args)
        {
            this.file.WriteLine(format, args);
            this.forwardTo.WriteLine(format, args);
            Debug.WriteLine(format, args);
        }

        public void Dispose() => this.file.Dispose();
    }
}
