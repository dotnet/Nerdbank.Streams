// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;

    /// <content>
    /// Contains the <see cref="ChannelStream"/> nested type.
    /// </content>
    public partial class MultiplexingStream
    {
        /// <summary>
        /// A full duplex stream that uses a single channel on a <see cref="MultiplexingStream"/> as its transport.
        /// </summary>
        private class ChannelStream : Stream, IDisposableObservable
        {
            /// <summary>
            /// The channel that this stream operates on.
            /// </summary>
            private readonly Channel channel;

            /// <summary>
            /// The stream we read from.
            /// </summary>
            private readonly Stream readStream;

            /// <summary>
            /// A stream we write to when messages are received from the transport stream so that we can later read them from <see cref="readStream"/>.
            /// </summary>
            private readonly Stream receivedStream;

            /// <summary>
            /// The semaphore acquired while writing.
            /// </summary>
            private readonly SemaphoreSlim writeSemaphore = new SemaphoreSlim(1);

            /// <summary>
            /// The buffer that local writes are temporarily stored, pending a flush.
            /// </summary>
            private readonly byte[] writeBuffer;

            /// <summary>
            /// The number of bytes in the <see cref="writeBuffer"/> that have not been flushed.
            /// </summary>
            private int writeBufferBytesUsed;

            /// <summary>
            /// Initializes a new instance of the <see cref="ChannelStream"/> class.
            /// </summary>
            /// <param name="channel">The channel that this stream operates on.</param>
            internal ChannelStream(Channel channel)
            {
                this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
                var streams = FullDuplexStream.CreateStreams();
                this.readStream = streams.Item1;
                this.receivedStream = streams.Item2;
                this.writeBuffer = new byte[channel.UnderlyingMultiplexingStream.maxFrameLength - FrameHeader.HeaderLength];
            }

            /// <inheritdoc />
            public override bool CanRead => !this.IsDisposed;

            /// <inheritdoc />
            public override bool CanSeek => false;

            /// <inheritdoc />
            public override bool CanWrite => !this.IsDisposed;

            /// <inheritdoc />
            public override long Length => throw this.ThrowDisposedOr(new NotSupportedException());

            /// <inheritdoc />
            public bool IsDisposed { get; private set; }

            /// <inheritdoc />
            public override long Position
            {
                get => throw this.ThrowDisposedOr(new NotSupportedException());
                set => this.ThrowDisposedOr(new NotSupportedException());
            }

#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
            /// <inheritdoc />
            public override void Flush() => this.FlushAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits

            /// <inheritdoc />
            public override async Task FlushAsync(CancellationToken cancellationToken)
            {
                await this.writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await this.FlushCoreAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    this.writeSemaphore.Release();
                }
            }

            /// <inheritdoc />
            public override int ReadByte() => this.readStream.ReadByte();

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count) => this.readStream.Read(buffer, offset, count);

            /// <inheritdoc />
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return this.readStream.ReadAsync(buffer, offset, count, cancellationToken);
            }

            /// <inheritdoc />
            public override long Seek(long offset, SeekOrigin origin) => throw this.ThrowDisposedOr(new NotSupportedException());

            /// <inheritdoc />
            public override void SetLength(long value) => this.ThrowDisposedOr(new NotSupportedException());

            /// <inheritdoc />
            public override void Write(byte[] buffer, int offset, int count)
            {
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
                this.WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
            }

            /// <inheritdoc />
            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                await this.writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (count <= this.writeBuffer.Length || this.writeBufferBytesUsed > 0)
                    {
                        // Fill up our write buffer as far as we can as a perf optimization.
                        int bufferedBytesCount = Math.Min(count, this.writeBuffer.Length - this.writeBufferBytesUsed);
                        Array.Copy(buffer, offset, this.writeBuffer, this.writeBufferBytesUsed, bufferedBytesCount);
                        this.writeBufferBytesUsed += bufferedBytesCount;
                        offset += bufferedBytesCount;
                        count -= bufferedBytesCount;
                    }

                    // If the buffer is totally full, go ahead and flush now, then consider writing more.
                    Assumes.False(this.writeBufferBytesUsed > this.writeBuffer.Length);
                    if (this.writeBufferBytesUsed == this.writeBuffer.Length)
                    {
                        await this.FlushCoreAsync(cancellationToken).ConfigureAwait(false);
                    }

                    bool writtenWithoutFlush = false;
                    while (count > 0)
                    {
                        // Use our write buffer for the rest of the message if it will fit. Otherwise, flush it directly.
                        Assumes.True(this.writeBufferBytesUsed == 0);
                        if (count <= this.writeBuffer.Length)
                        {
                            Array.Copy(buffer, offset, this.writeBuffer, 0, count);
                            this.writeBufferBytesUsed = count;
                            count = 0;
                        }
                        else
                        {
                            var header = new FrameHeader
                            {
                                Code = ControlCode.Content,
                                ChannelId = this.channel.Id.Value,
                                FramePayloadLength = this.writeBuffer.Length, // the maximum payload size for a frame
                            };
                            await this.channel.UnderlyingMultiplexingStream.SendFrameAsync(header, new ArraySegment<byte>(buffer, offset, header.FramePayloadLength), flush: false, cancellationToken).ConfigureAwait(false);
                            writtenWithoutFlush = true;
                            offset += header.FramePayloadLength;
                            count -= header.FramePayloadLength;
                        }
                    }

                    // If we bypassed our write buffer and wrote directly to the underlying stream, the user has no way to "flush" that
                    // since our own FlushAsync method only flushes our write buffer. So flush it now on the underlying stream if we didn't flush earlier.
                    if (writtenWithoutFlush)
                    {
                        await this.channel.UnderlyingMultiplexingStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    this.writeSemaphore.Release();
                }
            }

            internal void AddReadMessage(ArraySegment<byte> message) => this.receivedStream.Write(message.Array, message.Offset, message.Count);

            internal void RemoteEnded()
            {
                this.receivedStream.Dispose(); // This signals to any Read calls to return 0 bytes.
            }

            /// <inheritdoc />
            protected override void Dispose(bool disposing)
            {
                if (!this.IsDisposed)
                {
                    this.Flush();

                    this.IsDisposed = true;
                    this.readStream.Dispose();
                    this.receivedStream.Dispose();
                    this.channel.OnStreamDisposed();
                    base.Dispose(disposing);
                }
            }

            /// <summary>
            /// Flush our write buffer. The caller must hold the <see cref="writeSemaphore"/> when calling this method and must hold it till the returned <see cref="Task"/> has completed.
            /// </summary>
            /// <param name="cancellationToken">A cancellation token.</param>
            /// <returns>A task that marks completion of the operation.</returns>
            private Task FlushCoreAsync(CancellationToken cancellationToken)
            {
                if (this.writeBufferBytesUsed > 0)
                {
                    var header = new FrameHeader
                    {
                        Code = ControlCode.Content,
                        ChannelId = this.channel.Id.Value,
                        FramePayloadLength = this.writeBufferBytesUsed,
                    };

                    // Prepare all fields before making the call to an async method so we can avoid
                    // allocating another Task in this method by simply returning the Task directly without awaiting.
                    int writeBufferBytesUsed = this.writeBufferBytesUsed;
                    this.writeBufferBytesUsed = 0;
                    return this.channel.UnderlyingMultiplexingStream.SendFrameAsync(header, new ArraySegment<byte>(this.writeBuffer, 0, writeBufferBytesUsed), flush: true, cancellationToken);
                }

                return Utilities.CompletedTask;
            }

            private T ReturnIfNotDisposed<T>(T value)
            {
                Verify.NotDisposed(this);
                return value;
            }

            private Exception ThrowDisposedOr(Exception ex)
            {
                Verify.NotDisposed(this);
                throw ex;
            }
        }
    }
}
