namespace Nerdbank.FullDuplexStream
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using Validation;

    public class FullDuplexStream : Stream
    {
        /// <summary>
        /// The messages posted by the <see cref="other"/> party,
        /// for this stream to read.
        /// </summary>
        private readonly List<Message> readQueue = new List<Message>();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length
        {
            get { throw new NotSupportedException(); }
        }

        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        private FullDuplexStream other;

        public static Tuple<Stream, Stream> CreateStreams()
        {
            var stream1 = new FullDuplexStream();
            var stream2 = new FullDuplexStream();
            stream1.other = stream2;
            stream2.other = stream1;
            return Tuple.Create<Stream, Stream>(stream1, stream2);
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Requires.NotNull(buffer, nameof(buffer));
            Requires.Range(offset >= 0, nameof(offset));
            Requires.Range(count >= 0, nameof(count));
            Requires.Range(offset + count <= buffer.Length, nameof(count));

            return 0;
        }

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

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

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

            /// <summary>
            /// Gets the buffer to read from.
            /// </summary>
            public byte[] Buffer { get; }

            /// <summary>
            /// Gets or sets the position within the buffer that indicates the first
            /// character that has not yet been read.
            /// </summary>
            public int Position { get; private set; }

            public void Consume(int bytesRead)
            {
                Requires.Range(bytesRead >= 0, nameof(bytesRead));
                this.Position += bytesRead;
            }
        }
    }
}
