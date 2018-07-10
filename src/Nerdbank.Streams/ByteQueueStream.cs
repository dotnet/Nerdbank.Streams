// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Validation;

    /// <summary>
    /// A <see cref="Stream"/> that acts as a queue for bytes, in that what gets written to it
    /// can then be read from it, in order.
    /// </summary>
    public class ByteQueueStream : Stream, IDisposableObservable
    {
        /// <summary>
        /// The default size for the initially allocated buffer.
        /// </summary>
        private const int DefaultInitialBufferSize = 4096;

        /// <summary>
        /// The preferred minimum size to increase the buffer.
        /// </summary>
        private const int PreferredBufferMinimumIncrease = DefaultInitialBufferSize;

        /// <summary>
        /// A completed task.
        /// </summary>
        private static readonly Task CompletedTask = Task.FromResult(0);

        /// <summary>
        /// The maximum size the buffer is allowed to grow before write calls are blocked (pending a read that will release buffer space.
        /// </summary>
        private readonly int maxBufferSize;

        /// <summary>
        /// The object to lock when accessing <see cref="buffer"/>.
        /// </summary>
        private readonly object syncObject = new object();

        /// <summary>
        /// The buffer of written bytes that have not yet been read.
        /// </summary>
        private byte[] buffer;

        private int offset;

        private int count;

        /// <summary>
        /// Initializes a new instance of the <see cref="ByteQueueStream"/> class.
        /// </summary>
        public ByteQueueStream()
            : this(DefaultInitialBufferSize)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ByteQueueStream"/> class.
        /// </summary>
        /// <param name="initialBufferSize">The initial size of the buffer used to store written bytes until they are read.</param>
        /// <param name="maxBufferSize">The maximum size the buffer is allowed to grow before write calls are blocked (pending a read that will release buffer space.</param>
        public ByteQueueStream(int initialBufferSize = DefaultInitialBufferSize, int maxBufferSize = int.MaxValue)
        {
            Requires.Range(initialBufferSize > 0, nameof(initialBufferSize), "A positive value is required.");
            Requires.Range(maxBufferSize >= initialBufferSize, nameof(maxBufferSize), "This value must exceed the one supplied to " + nameof(initialBufferSize) + ".");

            this.buffer = new byte[initialBufferSize];
            this.maxBufferSize = maxBufferSize;
        }

        /// <inheritdoc />
        public bool IsDisposed { get; private set; }

        /// <inheritdoc />
        public override bool CanRead => !this.IsDisposed;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => !this.IsDisposed;

        /// <inheritdoc />
        public override long Length => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override long Position
        {
            get => throw this.ThrowDisposedOr(new NotSupportedException());
            set => throw this.ThrowDisposedOr(new NotSupportedException());
        }

        /// <summary>
        /// Gets the number of bytes remaining in the write buffer.
        /// </summary>
        private int BufferRemaining => this.buffer.Length - this.count;

        /// <summary>
        /// Does nothing, since web sockets do not need to be flushed.
        /// </summary>
        public override void Flush()
        {
        }

        /// <summary>
        /// Does nothing, since web sockets do not need to be flushed.
        /// </summary>
        /// <param name="cancellationToken">An ignored cancellation token.</param>
        /// <returns>A completed task.</returns>
        public override Task FlushAsync(CancellationToken cancellationToken) => CompletedTask;

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override void SetLength(long value) => throw this.ThrowDisposedOr(new NotSupportedException());

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (this.syncObject)
            {
                while (this.count == 0)
                {
                    Monitor.Wait(this.syncObject);
                }

                // We need to copy from our own buffer in up to two segments, since the buffer could "wrap around".
                int bytesRead = this.ReadOneSegment(buffer, ref offset, ref count);
                bytesRead += this.ReadOneSegment(buffer, ref offset, ref count);

                // Tip off writers that the buffer has more capacity.
                Monitor.PulseAll(this.syncObject);

                return bytesRead;
            }
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Requires.Range(offset + count <= buffer.Length, nameof(count));
            Requires.Range(count <= this.maxBufferSize, nameof(count), "Message exceeds maximum buffer size.");
            Verify.NotDisposed(this);

            lock (this.syncObject)
            {
                while (this.count + count > this.maxBufferSize)
                {
                    Monitor.Wait(this.syncObject);
                }

                if (this.BufferRemaining < count)
                {
                    this.GrowBuffer(this.count + count);
                }

                // We need to copy to our own buffer in up to two segments, since the buffer could "wrap around".
                this.WriteOneSegment(buffer, ref offset, ref count);
                this.WriteOneSegment(buffer, ref offset, ref count);

                // Tip off readers that there is more data.
                Monitor.PulseAll(this.syncObject);
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            this.IsDisposed = true;
            base.Dispose(disposing);
        }

        private Exception ThrowDisposedOr(Exception ex)
        {
            Verify.NotDisposed(this);
            throw ex;
        }

        private void GrowBuffer(int desiredSize)
        {
            Requires.Range(desiredSize > 0, nameof(desiredSize));
            Requires.Range(desiredSize <= this.maxBufferSize, nameof(desiredSize));

            Assumes.True(Monitor.IsEntered(this.syncObject));
            Verify.Operation(desiredSize > this.buffer.Length, "Buffer already meets or exceeds this size.");

            // Adjust desired size to be a multiple of its current size, if possible.
            int currentSize = this.buffer.Length;
            int multipleBy2 = currentSize * 2;
            int marginalIncrease = currentSize + PreferredBufferMinimumIncrease;
            if (desiredSize < multipleBy2 && multipleBy2 < this.maxBufferSize)
            {
                desiredSize = multipleBy2;
            }
            else if (desiredSize < marginalIncrease && marginalIncrease < this.maxBufferSize)
            {
                desiredSize = marginalIncrease;
            }

            byte[] newBuffer = new byte[desiredSize];
            int bytesRead = this.count > 0 ? this.Read(newBuffer, 0, newBuffer.Length) : 0;
            Assumes.True(this.count == 0); // We assume our own Read method never returns less than we ask for, up to the full buffer.
            this.buffer = newBuffer;
            this.offset = 0;
            this.count = bytesRead;
        }

        private int ReadOneSegment(byte[] buffer, ref int offset, ref int count)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Requires.Range(offset >= 0 && offset <= buffer.Length, nameof(offset));
            Requires.Range(count >= 0, nameof(count));

            int bytesRead = 0;
            int copyLength = Math.Min(Math.Min(count, this.count), this.buffer.Length - this.offset);
            if (copyLength > 0)
            {
                Array.Copy(this.buffer, this.offset, buffer, offset, copyLength);
                bytesRead += copyLength;

                int newCount = this.count - bytesRead;
                int newOffset = newCount == 0 ? 0 : (this.offset + bytesRead) % this.buffer.Length;
                this.offset = newOffset;
                this.count = newCount;

                offset += bytesRead;
                count -= bytesRead;
            }

            return bytesRead;
        }

        private void WriteOneSegment(byte[] buffer, ref int offset, ref int count)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Requires.Range(offset >= 0 && offset <= buffer.Length, nameof(offset));
            Requires.Range(count >= 0, nameof(count));

            int firstEmptySlot = (this.offset + this.count) % this.buffer.Length;
            int emptySpaceInSlot = firstEmptySlot >= this.offset ? this.buffer.Length - firstEmptySlot : this.offset;
            int copyLength = Math.Min(count, emptySpaceInSlot);
            Array.Copy(buffer, offset, this.buffer, firstEmptySlot, copyLength);
            this.count += copyLength;

            offset += copyLength;
            count -= copyLength;
        }
    }
}
