// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;
    using System.IO;
    using System.IO.Pipelines;
    using System.Net.WebSockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;
    using Microsoft.VisualStudio.Threading;

    /// <summary>
    /// Stream extension methods.
    /// </summary>
    public static partial class PipeExtensions
    {
        /// <summary>
        /// The default buffer size to use for pipe readers.
        /// </summary>
        private const int DefaultReadBufferSize = 4 * 1024;

        /// <summary>
        /// Exposes a full-duplex pipe as a <see cref="Stream"/>.
        /// The pipe will be completed when the <see cref="Stream"/> is disposed.
        /// </summary>
        /// <param name="pipe">The pipe to wrap as a stream.</param>
        /// <returns>The wrapping stream.</returns>
        public static Stream AsStream(this IDuplexPipe pipe) => AsStream(pipe, ownsPipe: true);

        /// <summary>
        /// Exposes a full-duplex pipe as a <see cref="Stream"/>.
        /// </summary>
        /// <param name="pipe">The pipe to wrap as a stream.</param>
        /// <param name="ownsPipe"><c>true</c> to complete the underlying reader and writer when the <see cref="Stream"/> is disposed; <c>false</c> to keep them open.</param>
        /// <returns>The wrapping stream.</returns>
        public static Stream AsStream(this IDuplexPipe pipe, bool ownsPipe) => FullDuplexStream.Splice(pipe.Input.AsStream(leaveOpen: !ownsPipe), pipe.Output.AsStream(leaveOpen: !ownsPipe));

        /// <summary>
        /// Exposes a pipe reader as a <see cref="Stream"/>.
        /// </summary>
        /// <param name="pipeReader">The pipe to read from when <see cref="Stream.ReadAsync(byte[], int, int, CancellationToken)"/> is invoked.</param>
        /// <returns>The wrapping stream.</returns>
        /// <remarks>
        /// The reader will be completed when the <see cref="Stream"/> is disposed.
        /// </remarks>
        [Obsolete("Use " + nameof(PipeReader) + "." + nameof(PipeReader.AsStream) + " instead.")]
        public static Stream AsStream(this PipeReader pipeReader) => pipeReader.AsStream(leaveOpen: false);

        /// <summary>
        /// Exposes a pipe reader as a <see cref="Stream"/> after asynchronously reading all content
        /// so the returned stream can be read synchronously without needlessly blocking a thread while waiting for more data.
        /// </summary>
        /// <param name="pipeReader">The pipe to read from when <see cref="Stream.ReadAsync(byte[], int, int, CancellationToken)"/> is invoked.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The wrapping stream.</returns>
        /// <remarks>
        /// The reader will be completed when the <see cref="Stream"/> is disposed.
        /// </remarks>
        public static async Task<Stream> AsPrebufferedStreamAsync(this PipeReader pipeReader, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(pipeReader, nameof(pipeReader));

            while (true)
            {
                // Read and immediately report all bytes as "examined" so that the next ReadAsync call will block till more bytes come in.
                // The goal here is to force the PipeReader to buffer everything internally (even if it were to exceed its natural writer threshold limit).
                ReadResult readResult = await pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                pipeReader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);

                if (readResult.IsCompleted)
                {
                    // After having buffered and "examined" all the bytes, the stream returned from PipeReader.AsStream() would fail
                    // because it may not "examine" all bytes at once.
                    // Instead, we'll create our own Stream over just the buffer itself, and recycle the buffers when the stream is disposed
                    // the way the stream returned from PipeReader.AsStream() would have.
                    return readResult.Buffer.AsStream(reader => ((PipeReader)reader!).Complete(), pipeReader);
                }
            }
        }

        /// <summary>
        /// Exposes a pipe writer as a <see cref="Stream"/>.
        /// </summary>
        /// <param name="pipeWriter">The pipe to write to when <see cref="Stream.WriteAsync(byte[], int, int, CancellationToken)"/> is invoked.</param>
        /// <returns>The wrapping stream.</returns>
        /// <remarks>
        /// The writer will be completed when the <see cref="Stream"/> is disposed.
        /// </remarks>
        [Obsolete("Use " + nameof(PipeWriter) + "." + nameof(PipeWriter.AsStream) + " instead.")]
        public static Stream AsStream(this PipeWriter pipeWriter) => pipeWriter.AsStream(leaveOpen: false);

        /// <summary>
        /// Enables efficiently reading a stream using <see cref="PipeReader"/>.
        /// </summary>
        /// <param name="stream">The stream to read from using a pipe.</param>
        /// <param name="sizeHint">A hint at the size of messages that are commonly transferred. Use 0 for a commonly reasonable default.</param>
        /// <param name="pipeOptions">Optional pipe options to use.</param>
        /// <param name="cancellationToken">A cancellation token that aborts reading from the <paramref name="stream"/>.</param>
        /// <returns>A <see cref="PipeReader"/>.</returns>
        /// <remarks>
        /// When the caller invokes <see cref="PipeReader.Complete(Exception)"/> on the result value,
        /// this leads to the associated <see cref="PipeWriter.Complete(Exception)"/> to be automatically called as well.
        /// </remarks>
        public static PipeReader UsePipeReader(this Stream stream, int sizeHint = 0, PipeOptions? pipeOptions = null, CancellationToken cancellationToken = default)
        {
            return UsePipeReader(stream, sizeHint, pipeOptions, null, cancellationToken);
        }

        /// <summary>
        /// Creates a <see cref="PipeReader"/> that reads from the specified <see cref="Stream"/> exactly as told to do so.
        /// It does *not* close the <paramref name="stream"/> when completed.
        /// </summary>
        /// <param name="stream">The stream to read from using a pipe.</param>
        /// <param name="sizeHint">A hint at the size of messages that are commonly transferred. Use 0 for a commonly reasonable default.</param>
        /// <returns>A <see cref="PipeReader"/>.</returns>
        /// <remarks>
        /// This reader may not be as efficient as the <see cref="Pipe"/>-based <see cref="PipeReader"/> returned from <see cref="UsePipeReader(Stream, int, PipeOptions, CancellationToken)"/>,
        /// but its interaction with the underlying <see cref="Stream"/> is closer to how a <see cref="Stream"/> would typically be used which can ease migration from streams to pipes.
        /// </remarks>
        [Obsolete("Use " + nameof(PipeReader) + "." + nameof(PipeReader.Create) + " instead.")]
        public static PipeReader UseStrictPipeReader(this Stream stream, int sizeHint = DefaultReadBufferSize)
        {
            Requires.NotNull(stream, nameof(stream));
            Requires.Argument(stream.CanRead, nameof(stream), "Stream must be readable.");

            return new StreamPipeReader(stream, sizeHint, leaveOpen: true);
        }

        /// <summary>
        /// Enables writing to a stream using <see cref="PipeWriter"/>.
        /// </summary>
        /// <param name="stream">The stream to write to using a pipe. This stream is *not* closed automatically.</param>
        /// <param name="pipeOptions">Optional pipe options to use.</param>
        /// <param name="cancellationToken">A cancellation token that aborts writing to the <paramref name="stream"/>.</param>
        /// <returns>A <see cref="PipeWriter"/>.</returns>
        public static PipeWriter UsePipeWriter(this Stream stream, PipeOptions? pipeOptions = null, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(stream, nameof(stream));
            Requires.Argument(stream.CanWrite, nameof(stream), "Stream must be writable.");

            var pipe = new Pipe(pipeOptions ?? PipeOptions.Default);
            Task.Run(async delegate
            {
                try
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ReadResult readResult = await pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                        if (readResult.Buffer.Length > 0)
                        {
                            foreach (ReadOnlyMemory<byte> segment in readResult.Buffer)
                            {
                                await stream.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
                            }

                            await stream.FlushIfNecessaryAsync(cancellationToken).ConfigureAwait(false);
                        }

                        pipe.Reader.AdvanceTo(readResult.Buffer.End);
                        readResult.ScrubAfterAdvanceTo();

                        if (readResult.IsCompleted)
                        {
                            break;
                        }
                    }

                    await pipe.Reader.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Propagate the exception to the writer.
                    await pipe.Reader.CompleteAsync(ex).ConfigureAwait(false);
                    return;
                }
            }).Forget();
            return pipe.Writer;
        }

        /// <summary>
        /// Creates a <see cref="PipeWriter"/> that writes to an underlying <see cref="Stream"/>
        /// when <see cref="PipeWriter.FlushAsync(CancellationToken)"/> is called rather than asynchronously sometime later.
        /// </summary>
        /// <param name="stream">The stream to write to using a pipe.</param>
        /// <returns>A <see cref="PipeWriter"/>.</returns>
        /// <remarks>
        /// This writer may not be as efficient as the <see cref="Pipe"/>-based <see cref="PipeWriter"/> returned from <see cref="UsePipeWriter(Stream, PipeOptions, CancellationToken)"/>,
        /// but its interaction with the underlying <see cref="Stream"/> is closer to how a <see cref="Stream"/> would typically be used which can ease migration from streams to pipes.
        /// </remarks>
        [Obsolete("Use " + nameof(PipeWriter) + "." + nameof(PipeWriter.Create) + " instead.")]
        public static PipeWriter UseStrictPipeWriter(this Stream stream)
        {
            Requires.NotNull(stream, nameof(stream));
            Requires.Argument(stream.CanWrite, nameof(stream), "Stream must be writable.");

            return new StreamPipeWriter(stream);
        }

        /// <summary>
        /// Enables reading and writing to a <see cref="Stream"/> using <see cref="PipeWriter"/> and <see cref="PipeReader"/>.
        /// </summary>
        /// <param name="stream">The stream to access using a pipe.</param>
        /// <param name="sizeHint">A hint at the size of messages that are commonly transferred. Use 0 for a commonly reasonable default.</param>
        /// <param name="pipeOptions">Optional pipe options to use.</param>
        /// <param name="cancellationToken">A token that may cancel async processes to read from and write to the <paramref name="stream"/>.</param>
        /// <returns>An <see cref="IDuplexPipe"/> instance.</returns>
        public static IDuplexPipe UsePipe(this Stream stream, int sizeHint = 0, PipeOptions? pipeOptions = null, CancellationToken cancellationToken = default)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return UsePipe(stream, allowUnwrap: false, sizeHint: sizeHint, pipeOptions: pipeOptions, cancellationToken: cancellationToken);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        /// <summary>
        /// Enables reading and writing to a <see cref="Stream"/> using <see cref="PipeWriter"/> and <see cref="PipeReader"/>.
        /// </summary>
        /// <param name="stream">The stream to access using a pipe.</param>
        /// <param name="allowUnwrap">Obsolete. This value is ignored.</param>
        /// <param name="sizeHint">A hint at the size of messages that are commonly transferred. Use 0 for a commonly reasonable default.</param>
        /// <param name="pipeOptions">Optional pipe options to use.</param>
        /// <param name="cancellationToken">A token that may cancel async processes to read from and write to the <paramref name="stream"/>.</param>
        /// <returns>An <see cref="IDuplexPipe"/> instance.</returns>
        [Obsolete("Use the UsePipe overload that doesn't take an allowUnwrap parameter instead.")]
        public static IDuplexPipe UsePipe(this Stream stream, bool allowUnwrap, int sizeHint = 0, PipeOptions? pipeOptions = null, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(stream, nameof(stream));
            Requires.Argument(stream.CanRead || stream.CanWrite, nameof(stream), "Stream is neither readable nor writable.");

            // OBSOLETE API USAGE NOTICE: If at some point we need to stop relying on these obsolete callbacks,
            //                            we can return a decorated PipeReader/PipeWriter that calls us from its Complete method directly.
#pragma warning disable CS0618 // Type or member is obsolete
            PipeWriter? output = stream.CanWrite ? stream.UsePipeWriter(pipeOptions, cancellationToken) : null;
#pragma warning disable VSTHRD110 // Observe result of async calls - https://github.com/microsoft/vs-threading/issues/899
            PipeReader? input = stream.CanRead ? stream.UsePipeReader(sizeHint, pipeOptions, output?.WaitForReaderCompletionAsync(), cancellationToken) : null;
#pragma warning restore VSTHRD110 // Observe result of async calls

            Task? closeStreamAntecedent;
            if (input != null && output != null)
            {
                // The UsePipeReader function will be responsible for disposing the stream in this case.
                closeStreamAntecedent = null;
            }
            else if (input != null)
            {
                closeStreamAntecedent = input.WaitForWriterCompletionAsync();
            }
            else
            {
                Assumes.NotNull(output);
                closeStreamAntecedent = output.WaitForReaderCompletionAsync();
            }
#pragma warning restore CS0618 // Type or member is obsolete

            closeStreamAntecedent?.ContinueWith((_, state) => ((Stream)state!).Dispose(), stream, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Default).Forget();
            return new DuplexPipe(input, output);
        }

        /// <summary>
        /// Enables efficiently reading a <see cref="WebSocket"/> using <see cref="PipeReader"/>.
        /// </summary>
        /// <param name="webSocket">The web socket to read from using a pipe.</param>
        /// <param name="sizeHint">A hint at the size of messages that are commonly transferred. Use 0 for a commonly reasonable default.</param>
        /// <param name="pipeOptions">Optional pipe options to use.</param>
        /// <param name="cancellationToken">A cancellation token that aborts reading from the <paramref name="webSocket"/>.</param>
        /// <returns>A <see cref="PipeReader"/>.</returns>
        public static PipeReader UsePipeReader(this WebSocket webSocket, int sizeHint = 0, PipeOptions? pipeOptions = null, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(webSocket, nameof(webSocket));

            var pipe = new Pipe(pipeOptions ?? PipeOptions.Default);
            Task.Run(async delegate
            {
                while (true)
                {
                    Memory<byte> memory = pipe.Writer.GetMemory(sizeHint);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var readResult = await webSocket.ReceiveAsync(memory, cancellationToken).ConfigureAwait(false);
                        if (readResult.Count == 0)
                        {
                            break;
                        }

                        pipe.Writer.Advance(readResult.Count);
                    }
                    catch (Exception ex)
                    {
                        // Propagate the exception to the reader.
                        await pipe.Writer.CompleteAsync(ex).ConfigureAwait(false);
                        return;
                    }

                    FlushResult result = await pipe.Writer.FlushAsync().ConfigureAwait(false);
                    if (result.IsCompleted)
                    {
                        break;
                    }
                }

                // Tell the PipeReader that there's no more data coming
                await pipe.Writer.CompleteAsync().ConfigureAwait(false);
            }).Forget();

            return pipe.Reader;
        }

        /// <summary>
        /// Enables efficiently writing to a <see cref="WebSocket"/> using a <see cref="PipeWriter"/>.
        /// </summary>
        /// <param name="webSocket">The web socket to write to using a pipe.</param>
        /// <param name="pipeOptions">Optional pipe options to use.</param>
        /// <param name="cancellationToken">A cancellation token that aborts writing to the <paramref name="webSocket"/>.</param>
        /// <returns>A <see cref="PipeWriter"/>.</returns>
        public static PipeWriter UsePipeWriter(this WebSocket webSocket, PipeOptions? pipeOptions = null, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(webSocket, nameof(webSocket));

            var pipe = new Pipe(pipeOptions ?? PipeOptions.Default);
            Task.Run(async delegate
            {
                try
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ReadResult readResult = await pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                        if (readResult.Buffer.Length > 0)
                        {
                            foreach (ReadOnlyMemory<byte> segment in readResult.Buffer)
                            {
                                await webSocket.SendAsync(segment, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken).ConfigureAwait(false);
                            }
                        }

                        pipe.Reader.AdvanceTo(readResult.Buffer.End);
                        readResult.ScrubAfterAdvanceTo();

                        if (readResult.IsCompleted)
                        {
                            break;
                        }
                    }

                    await pipe.Reader.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Propagate the exception to the writer.
                    await pipe.Reader.CompleteAsync(ex).ConfigureAwait(false);
                    return;
                }
            }).Forget();
            return pipe.Writer;
        }

        /// <summary>
        /// Enables reading and writing to a <see cref="WebSocket"/> using <see cref="PipeWriter"/> and <see cref="PipeReader"/>.
        /// </summary>
        /// <param name="webSocket">The <see cref="WebSocket"/> to access using a pipe.</param>
        /// <param name="sizeHint">A hint at the size of messages that may be transferred. Use 0 for a commonly reasonable default.</param>
        /// <param name="pipeOptions">Optional pipe options to use.</param>
        /// <param name="cancellationToken">A token that may cancel async processes to read from and write to the <paramref name="webSocket"/>.</param>
        /// <returns>An <see cref="IDuplexPipe"/> instance.</returns>
        public static IDuplexPipe UsePipe(this WebSocket webSocket, int sizeHint = 0, PipeOptions? pipeOptions = null, CancellationToken cancellationToken = default)
        {
            return new DuplexPipe(webSocket.UsePipeReader(sizeHint, pipeOptions, cancellationToken), webSocket.UsePipeWriter(pipeOptions, cancellationToken));
        }

        /// <summary>
        /// Creates a <see cref="PipeReader"/> that can read no more than a given number of bytes from an underlying reader.
        /// </summary>
        /// <param name="reader">The <see cref="PipeReader"/> to read from.</param>
        /// <param name="length">The number of bytes to read from the parent <paramref name="reader"/>.</param>
        /// <returns>A reader that ends after <paramref name="length"/> bytes are read.</returns>
        public static PipeReader ReadSlice(this PipeReader reader, long length) => new NestedPipeReader(reader, length);

        /// <summary>
        /// Wraps a <see cref="PipeReader"/> in another object that will report when <see cref="PipeReader.Complete(Exception)"/> is called.
        /// </summary>
        /// <param name="reader">The reader to be wrapped.</param>
        /// <param name="callback">
        /// The callback to invoke when the <see cref="PipeReader.Complete(Exception)"/> method is called.
        /// If this delegate throws an exception it will propagate to the caller of the <see cref="PipeReader.Complete(Exception)"/> method.
        /// </param>
        /// <param name="state">An optional state object to supply to the <paramref name="callback"/>.</param>
        /// <returns>The wrapped <see cref="PipeReader"/> which should be exposed to have its <see cref="PipeReader.Complete(Exception)"/> method called.</returns>
        /// <remarks>
        /// If the <see cref="PipeReader"/> has already been completed, the provided <paramref name="callback"/> may never be invoked.
        /// </remarks>
        public static PipeReader OnCompleted(this PipeReader reader, Action<Exception?, object?> callback, object? state = null) => new PipeReaderCompletionWatcher(reader, callback, state);

        /// <summary>
        /// Wraps a <see cref="PipeWriter"/> in another object that will report when <see cref="PipeWriter.Complete(Exception)"/> is called.
        /// </summary>
        /// <param name="reader">The writer to be wrapped.</param>
        /// <param name="callback">
        /// The callback to invoke when the <see cref="PipeWriter.Complete(Exception)"/> method is called.
        /// If this delegate throws an exception it will propagate to the caller of the <see cref="PipeWriter.Complete(Exception)"/> method.
        /// </param>
        /// <param name="state">An optional state object to supply to the <paramref name="callback"/>.</param>
        /// <returns>The wrapped <see cref="PipeWriter"/> which should be exposed to have its <see cref="PipeWriter.Complete(Exception)"/> method called.</returns>
        /// <remarks>
        /// If the <see cref="PipeWriter"/> has already been completed, the provided <paramref name="callback"/> may never be invoked.
        /// </remarks>
        public static PipeWriter OnCompleted(this PipeWriter reader, Action<Exception?, object?> callback, object? state = null) => new PipeWriterCompletionWatcher(reader, callback, state);

        /// <summary>
        /// Forwards all bytes coming from a <see cref="PipeReader"/> to the specified <see cref="PipeWriter"/>.
        /// </summary>
        /// <param name="reader">The reader to get bytes from.</param>
        /// <param name="writer">The writer to copy bytes to. <see cref="PipeWriter.CompleteAsync(Exception)"/> will be called on this object when the reader completes or an error occurs.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>
        /// A <see cref="Task"/> that completes when the <paramref name="reader"/> has finished producing bytes, or an error occurs.
        /// This <see cref="Task"/> never faults, since any exceptions are used to complete the <paramref name="writer"/>.
        /// </returns>
        /// <remarks>
        /// If an error occurs during reading or writing, the <paramref name="writer"/> and <paramref name="reader"/> are completed with the exception.
        /// </remarks>
        internal static Task LinkToAsync(this PipeReader reader, PipeWriter writer, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(reader, nameof(reader));
            Requires.NotNull(writer, nameof(writer));

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            return Task.Run(async delegate
            {
                try
                {
                    if (DuplexPipe.IsDefinitelyCompleted(reader))
                    {
                        await writer.CompleteAsync().ConfigureAwait(false);
                        return;
                    }

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                        if (result.IsCanceled)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            throw new OperationCanceledException(Strings.PipeReaderCanceled);
                        }

                        writer.Write(result.Buffer);
                        reader.AdvanceTo(result.Buffer.End);
                        result.ScrubAfterAdvanceTo();
                        FlushResult flushResult = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

                        if (flushResult.IsCanceled)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            throw new OperationCanceledException(Strings.PipeWriterFlushCanceled);
                        }

                        if (flushResult.IsCompleted)
                        {
                            // Break out of copy loop. The receiver doesn't care any more.
                            break;
                        }

                        if (result.IsCompleted)
                        {
                            await writer.CompleteAsync().ConfigureAwait(false);
                            break;
                        }
                    }

                    await reader.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await writer.CompleteAsync(ex).ConfigureAwait(false);
                    await reader.CompleteAsync(ex).ConfigureAwait(false);
                }
            });
        }

        /// <summary>
        /// Forwards all bytes coming from either <see cref="IDuplexPipe"/> to the other <see cref="IDuplexPipe"/>.
        /// </summary>
        /// <param name="pipe1">The first duplex pipe.</param>
        /// <param name="pipe2">The second duplex pipe.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>
        /// A <see cref="Task"/> that completes when both <see cref="PipeReader"/> instances are finished producing bytes, or an error occurs.
        /// This <see cref="Task"/> never faults, since any exceptions are used to complete the <see cref="PipeWriter" /> objects.
        /// </returns>
        /// <remarks>
        /// If an error occurs during reading or writing, both reader and writer are completed with the exception.
        /// </remarks>
        internal static Task LinkToAsync(this IDuplexPipe pipe1, IDuplexPipe pipe2, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(pipe1, nameof(pipe1));
            Requires.NotNull(pipe2, nameof(pipe2));

            return Task.WhenAll(
                pipe1.Input.LinkToAsync(pipe2.Output, cancellationToken),
                pipe2.Input.LinkToAsync(pipe1.Output, cancellationToken));
        }

        /// <summary>
        /// Enables efficiently reading a stream using <see cref="PipeReader"/>.
        /// </summary>
        /// <param name="stream">The stream to read from using a pipe.</param>
        /// <param name="sizeHint">A hint at the size of messages that are commonly transferred. Use 0 for a commonly reasonable default.</param>
        /// <param name="pipeOptions">Optional pipe options to use.</param>
        /// <param name="disposeWhenReaderCompleted">A task which, when complete, signals that this method should dispose of the <paramref name="stream"/>.</param>
        /// <param name="cancellationToken">A cancellation token that aborts reading from the <paramref name="stream"/>.</param>
        /// <returns>A <see cref="PipeReader"/>.</returns>
        /// <remarks>
        /// When the caller invokes <see cref="PipeReader.Complete(Exception)"/> on the result value,
        /// this leads to the associated <see cref="PipeWriter.Complete(Exception)"/> to be automatically called as well.
        /// </remarks>
        private static PipeReader UsePipeReader(this Stream stream, int sizeHint = 0, PipeOptions? pipeOptions = null, Task? disposeWhenReaderCompleted = null, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(stream, nameof(stream));
            Requires.Argument(stream.CanRead, nameof(stream), "Stream must be readable.");

            var pipe = new Pipe(pipeOptions ?? PipeOptions.Default);

            // Notice when the pipe reader isn't listening any more, and terminate our loop that reads from the stream.
            // OBSOLETE API USAGE NOTICE: If at some point we need to stop relying on PipeWriter.OnReaderCompleted (since it is deprecated and may be removed later),
            //                            we can return a decorated PipeReader that calls us from its Complete method directly.
            var combinedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
#pragma warning disable CS0618 // Type or member is obsolete
            pipe.Writer.OnReaderCompleted(
                (ex, state) =>
                {
                    try
                    {
                        ((CancellationTokenSource)state!).Cancel();
                    }
                    catch (AggregateException cancelException)
                    {
                        // .NET Core may throw this when canceling async I/O (https://github.com/dotnet/runtime/issues/39902).
                        // Just swallow it. We've canceled what we intended to.
                        cancelException.Handle(x => x is ObjectDisposedException);
                    }
                },
                combinedTokenSource);

            // When this argument is provided, it provides a means to ensure we don't hang while reading from an I/O pipe
            // that doesn't respect the CancellationToken. Disposing a Stream while reading is a means to terminate the ReadAsync operation.
            if (disposeWhenReaderCompleted is object)
            {
                disposeWhenReaderCompleted.ContinueWith(
                    (_, s1) =>
                    {
                        var tuple = (Tuple<Pipe, Stream>)s1!;
                        tuple.Item1.Writer.OnReaderCompleted((ex, s2) => ((Stream)s2!).Dispose(), tuple.Item2);
                    },
                    Tuple.Create(pipe, stream),
                    cancellationToken,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default).Forget();
            }
#pragma warning restore CS0618 // Type or member is obsolete

            Task.Run(async delegate
            {
                while (!combinedTokenSource.Token.IsCancellationRequested)
                {
                    Memory<byte> memory = pipe.Writer.GetMemory(sizeHint);
                    try
                    {
                        int bytesRead = await stream.ReadAsync(memory, combinedTokenSource.Token).ConfigureAwait(false);
                        if (bytesRead == 0)
                        {
                            break;
                        }

                        pipe.Writer.Advance(bytesRead);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        // Propagate the exception to the reader.
                        await pipe.Writer.CompleteAsync(ex).ConfigureAwait(false);
                        return;
                    }

                    FlushResult result = await pipe.Writer.FlushAsync().ConfigureAwait(false);
                    if (result.IsCompleted)
                    {
                        break;
                    }
                }

                // Tell the PipeReader that there's no more data coming
                await pipe.Writer.CompleteAsync().ConfigureAwait(false);
            }).Forget();
            return pipe.Reader;
        }

        /// <summary>
        /// Copies a sequence of bytes to a <see cref="PipeWriter"/>.
        /// </summary>
        /// <param name="writer">The writer to use.</param>
        /// <param name="sequence">The sequence to read.</param>
        private static void Write(this PipeWriter writer, ReadOnlySequence<byte> sequence)
        {
            Requires.NotNull(writer, nameof(writer));

            foreach (ReadOnlyMemory<byte> sourceMemory in sequence)
            {
                var sourceSpan = sourceMemory.Span;
                var targetSpan = writer.GetSpan(sourceSpan.Length);
                sourceSpan.CopyTo(targetSpan);
                writer.Advance(sourceSpan.Length);
            }
        }
    }
}
