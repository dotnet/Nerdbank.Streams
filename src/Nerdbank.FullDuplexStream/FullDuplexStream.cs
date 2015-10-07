namespace Nerdbank.FullDuplexStream
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using Validation;

    /// <summary>
    /// Provides a full duplex stream which may be shared by two parties to
    /// exchange messages.
    /// </summary>
    public class FullDuplexStream : Stream
    {
        /// <summary>
        /// The messages posted by the <see cref="other"/> party,
        /// for this stream to read.
        /// </summary>
        private readonly List<Message> readQueue = new List<Message>();

        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => true;

        /// <inheritdoc />
        public override long Length
        {
            get { throw new NotSupportedException(); }
        }

        /// <inheritdoc />
        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        private FullDuplexStream other;

        /// <summary>
        /// Creates a pair of streams that can be passed to two parties
        /// to allow for interaction with each other.
        /// </summary>
        /// <returns>A pair of streams.</returns>
        public static Tuple<Stream, Stream> CreateStreams()
        {
            var stream1 = new FullDuplexStream();
            var stream2 = new FullDuplexStream();
            stream1.other = stream2;
            stream2.other = stream1;
            return Tuple.Create<Stream, Stream>(stream1, stream2);
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Requires.Range(offset >= 0, nameof(offset));
            Requires.Range(count >= 0, nameof(count));
            Requires.Range(offset + count <= buffer.Length, nameof(count));

            lock (this.readQueue)
            {
                var message = this.readQueue[0];
                int copiedBytes = message.Consume(buffer, offset, count);
                if (message.IsConsumed)
                {
                    this.readQueue.RemoveAt(0);
                }

                return copiedBytes;
            }
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Requires.Range(offset >= 0, nameof(offset));
            Requires.Range(count >= 0, nameof(count));
            Requires.Range(offset + count <= buffer.Length, nameof(count));

            byte[] queuedBuffer = new byte[count];
            Array.Copy(buffer, offset, queuedBuffer, 0, count);
            lock (this.other.readQueue)
            {
                this.other.readQueue.Add(new Message(queuedBuffer));
            }
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        private class Message
        {
            internal Message(byte[] buffer)
            {
                this.Buffer = buffer;
            }

            public bool IsConsumed => this.Position == this.Buffer.Length;

            /// <summary>
            /// Gets the buffer to read from.
            /// </summary>
            private byte[] Buffer { get; }

            /// <summary>
            /// Gets or sets the position within the buffer that indicates the first
            /// character that has not yet been read.
            /// </summary>
            private int Position { get; set; }

            public int Consume(byte[] buffer, int offset, int count)
            {
                int copiedBytes = Math.Min(count, this.Buffer.Length - this.Position);
                Array.Copy(this.Buffer, this.Position, buffer, offset, copiedBytes);
                this.Position += copiedBytes;
                Assumes.False(this.Position > this.Buffer.Length);
                return copiedBytes;
            }
        }
    }
}
