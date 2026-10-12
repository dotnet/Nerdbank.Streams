// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
#if NETFRAMEWORK
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
#endif
using Nerdbank.Streams;
using Xunit;

public class SharedMemoryDuplexPipeTests
{
#if NET8_0_OR_GREATER || NETFRAMEWORK
    [Test]
    public async Task BasicRoundTripOverPair()
    {
        (SharedMemoryDuplexPipe client, SharedMemoryDuplexPipe server) = await SharedMemoryDuplexPipe.CreatePairAsync();
        using (client)
        using (server)
        {
            byte[] message = [1, 2, 3, 4, 5];
            await client.Output.WriteAsync(message);

            ReadResult read = await server.Input.ReadAsync();
            Assert.Equal(message, read.Buffer.ToArray());
            server.Input.AdvanceTo(read.Buffer.End);
        }
    }

    /// <summary>Verifies that repeatedly suspended reads recover after cancellation and deliver later messages.</summary>
    [Test]
    public async Task PendingReadsRecoverAfterCancellation()
    {
        (SharedMemoryDuplexPipe client, SharedMemoryDuplexPipe server) = await SharedMemoryDuplexPipe.CreatePairAsync();
        using (client)
        using (server)
        using (CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10)))
        {
            for (byte value = 0; value < 32; value++)
            {
                using CancellationTokenSource canceled = new();
                Task<ReadResult> pending = server.Input.ReadAsync(canceled.Token).AsTask();
                canceled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

                Task<ReadResult> next = server.Input.ReadAsync(timeout.Token).AsTask();
                await client.Output.WriteAsync(new byte[] { value }, timeout.Token);
                ReadResult read = await next;
                Assert.Equal(new byte[] { value }, read.Buffer.ToArray());
                server.Input.AdvanceTo(read.Buffer.End);
            }
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ListenAndConnectRendezvous(bool longName)
    {
        string channelName = $"test-{Guid.NewGuid():N}" + (longName ? new string('x', 72) : string.Empty);
        Task<SharedMemoryDuplexPipe> listenTask = SharedMemoryDuplexPipe.ListenAsync(channelName);
        SharedMemoryDuplexPipe client = await SharedMemoryDuplexPipe.ConnectAsync(channelName);
        SharedMemoryDuplexPipe server = await listenTask;

        using (client)
        using (server)
        {
            byte[] sent = [42, 43, 44];
            await client.Output.WriteAsync(sent);
            ReadResult read = await server.Input.ReadAsync();
            Assert.Equal(sent, read.Buffer.ToArray());
            server.Input.AdvanceTo(read.Buffer.End);
        }
    }

    /// <summary>
    /// Verifies that messages smaller than, equal to, and larger than the buffer arrive intact
    /// whether the reader keeps up with each message (so the writer returns to the start of the buffer)
    /// or lags behind (so data wraps around the end of the buffer).
    /// </summary>
    /// <param name="lockstep">Whether to read each message before writing the next one.</param>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task MessagesOfManySizesArriveIntact(bool lockstep)
    {
        const int Capacity = 64;
        const int MaxMessageSize = 3 * Capacity;
        int totalLength = 0;
        for (int size = 1; size <= MaxMessageSize; size++)
        {
            totalLength += size;
        }

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        (SharedMemoryDuplexPipe client, SharedMemoryDuplexPipe server) = await SharedMemoryDuplexPipe.CreatePairAsync(new SharedMemoryPipeOptions { Capacity = Capacity }, timeout.Token);
        using (client)
        using (server)
        {
            byte[] received = new byte[totalLength];
            int receivedLength = 0;
            int sentLength = 0;
            Task writing = lockstep ? Task.CompletedTask : Task.Run(WriteAllAsync);
            if (lockstep)
            {
                for (int size = 1; size <= MaxMessageSize; size++)
                {
                    Task<FlushResult> flush = WriteMessageAsync(size).AsTask();
                    await ReadAtLeastAsync(sentLength);
                    await flush;
                }
            }
            else
            {
                await ReadAtLeastAsync(totalLength);
            }

            await writing;
            Assert.Equal(totalLength, receivedLength);
            for (int i = 0; i < totalLength; i++)
            {
                Assert.Equal(Expected(i), received[i]);
            }

            async Task WriteAllAsync()
            {
                for (int size = 1; size <= MaxMessageSize; size++)
                {
                    await WriteMessageAsync(size);
                }
            }

            ValueTask<FlushResult> WriteMessageAsync(int size)
            {
                // Write the way serializers do: request a span, fill some of it, advance, repeat.
                int remaining = size;
                while (remaining > 0)
                {
                    Span<byte> span = client.Output.GetSpan(Math.Min(remaining, 16));
                    int chunk = Math.Min(remaining, Math.Min(span.Length, 16));
                    for (int i = 0; i < chunk; i++)
                    {
                        span[i] = Expected(sentLength++);
                    }

                    client.Output.Advance(chunk);
                    remaining -= chunk;
                }

                return client.Output.FlushAsync(timeout.Token);
            }

            async Task ReadAtLeastAsync(int length)
            {
                while (receivedLength < length)
                {
                    ReadResult read = await server.Input.ReadAsync(timeout.Token);
                    read.Buffer.CopyTo(received.AsSpan(receivedLength));
                    receivedLength += checked((int)read.Buffer.Length);
                    server.Input.AdvanceTo(read.Buffer.End);
                }
            }
        }

        static byte Expected(int index) => unchecked((byte)((index * 31) + (index >> 8)));
    }

    /// <summary>Verifies that a client using the wrong capacity fails without stranding the server's backing file.</summary>
    [Test]
    public async Task RejectedClientDoesNotStrandBackingFile()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), $"nbjsonrpc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string channelName = $"test-{Guid.NewGuid():N}";
            string backingFile = Path.Combine(directory, $"nbjsonrpc-{channelName}.shm");
            SharedMemoryPipeOptions options = new() { BaseDirectory = directory, Capacity = 4096 };
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            Task<SharedMemoryDuplexPipe> listenTask = SharedMemoryDuplexPipe.ListenAsync(channelName, options, timeout.Token);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await SharedMemoryDuplexPipe.ConnectAsync(channelName, new SharedMemoryPipeOptions { BaseDirectory = directory, Capacity = 8192 }, timeout.Token));
            await Assert.ThrowsAsync<IOException>(async () => await listenTask);
            Assert.False(File.Exists(backingFile));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

#if NET8_0_OR_GREATER
    /// <summary>Verifies that other users cannot read or write a waiting listener's backing file.</summary>
    [Test]
    public async Task UnixBackingFileIsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), $"nbjsonrpc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string channelName = $"test-{Guid.NewGuid():N}";
            string backingFile = Path.Combine(directory, $"nbjsonrpc-{channelName}.shm");
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            Task<SharedMemoryDuplexPipe> listenTask = SharedMemoryDuplexPipe.ListenAsync(channelName, new SharedMemoryPipeOptions { BaseDirectory = directory }, timeout.Token);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(backingFile) & (UnixFileMode)0x1ff);
            using SharedMemoryDuplexPipe client = await SharedMemoryDuplexPipe.ConnectAsync(channelName, new SharedMemoryPipeOptions { BaseDirectory = directory }, timeout.Token);
            using SharedMemoryDuplexPipe server = await listenTask;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

#endif

    /// <summary>
    /// Verifies that no file is left on disk once the endpoints are connected, so a crash cannot leave one behind.
    /// </summary>
    [Test]
    public async Task NoBackingFileRemainsOnceConnected()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"nbjsonrpc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            SharedMemoryPipeOptions options = new() { BaseDirectory = directory, Capacity = 4096 };
            (SharedMemoryDuplexPipe pairClient, SharedMemoryDuplexPipe pairServer) = await SharedMemoryDuplexPipe.CreatePairAsync(options);
            using (pairClient)
            using (pairServer)
            {
                Assert.Empty(Directory.GetFiles(directory));
            }

            string channelName = $"test-{Guid.NewGuid():N}";
            Task<SharedMemoryDuplexPipe> listenTask = SharedMemoryDuplexPipe.ListenAsync(channelName, options);
            SharedMemoryDuplexPipe client = await SharedMemoryDuplexPipe.ConnectAsync(channelName, options);
            SharedMemoryDuplexPipe server = await listenTask;
            using (client)
            using (server)
            {
                Assert.Empty(Directory.GetFiles(directory));
                await client.Output.WriteAsync(new byte[] { 7 });
                ReadResult read = await server.Input.ReadAsync();
                Assert.Equal(new byte[] { 7 }, read.Buffer.ToArray());
                server.Input.AdvanceTo(read.Buffer.End);
            }

            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
#endif

#if NETFRAMEWORK
    /// <summary>Verifies that the Framework rendezvous pipe grants access only to the current user.</summary>
    [Test]
    public async Task FrameworkRendezvousPipeIsCurrentUserOnly()
    {
        string channelName = $"test-{Guid.NewGuid():N}";
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        Task<SharedMemoryDuplexPipe> listen = SharedMemoryDuplexPipe.ListenAsync(channelName, cancellationToken: timeout.Token);
        using NamedPipeClientStream client = new(".", $"nbjsonrpc-{channelName}", PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
        await client.ConnectAsync(timeout.Token);
        PipeSecurity security = client.GetAccessControl();
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User!;
        Assert.Equal(user, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.True(security.AreAccessRulesProtected);
        AuthorizationRuleCollection rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
        Assert.NotEmpty(rules);
        foreach (PipeAccessRule rule in rules)
        {
            Assert.Equal(user, rule.IdentityReference);
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        }

        await client.WriteAsync(new byte[] { 1 }, 0, 1, timeout.Token);
        using SharedMemoryDuplexPipe server = await listen;
    }
#endif
}
