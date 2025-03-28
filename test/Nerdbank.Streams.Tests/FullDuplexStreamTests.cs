using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nerdbank.Streams;
using Xunit;

namespace Nerdbank.Streams.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="FullDuplexStream"/> class.
    /// </summary>
    public class FullDuplexStreamTests
    {
        /// <summary>
        /// Verifies that CreatePair returns a pair of interconnected streams with full duplex communication.
        /// The test writes data from one stream and validates that it is received by the paired stream, and vice versa.
        /// </summary>
        [Fact]
        public async Task CreatePair_WhenCalled_ReturnsConnectedStreams()
        {
            // Arrange & Act
            (Stream stream1, Stream stream2) = FullDuplexStream.CreatePair();

            // Arrange message data for communication from stream1 to stream2.
            byte[] messageFrom1 = Encoding.UTF8.GetBytes("Hello from stream1");
            byte[] readBuffer1 = new byte[messageFrom1.Length];

            // Act: Write data on stream1.
            await stream1.WriteAsync(messageFrom1, 0, messageFrom1.Length);
            await stream1.FlushAsync();
            // Allow some time for internal propagation.
            await Task.Delay(50);
            int bytesRead1 = await stream2.ReadAsync(readBuffer1, 0, readBuffer1.Length);

            // Assert: Verify that stream2 received the correct data.
            Assert.Equal(messageFrom1.Length, bytesRead1);
            Assert.Equal(messageFrom1, readBuffer1);

            // Arrange message data for communication from stream2 to stream1.
            byte[] messageFrom2 = Encoding.UTF8.GetBytes("Reply from stream2");
            byte[] readBuffer2 = new byte[messageFrom2.Length];

            // Act: Write data on stream2.
            await stream2.WriteAsync(messageFrom2, 0, messageFrom2.Length);
            await stream2.FlushAsync();
            await Task.Delay(50);
            int bytesRead2 = await stream1.ReadAsync(readBuffer2, 0, readBuffer2.Length);

            // Assert: Verify that stream1 received the correct reply.
            Assert.Equal(messageFrom2.Length, bytesRead2);
            Assert.Equal(messageFrom2, readBuffer2);
        }

        /// <summary>
        /// Verifies that CreatePipePair returns a pair of interconnected IDuplexPipe objects.
        /// The test converts the pipes to streams and performs bidirectional communication.
        /// </summary>
        [Fact]
        public async Task CreatePipePair_WhenCalled_ReturnsConnectedPipePair()
        {
            // Arrange & Act
            (IDuplexPipe pipe1, IDuplexPipe pipe2) = FullDuplexStream.CreatePipePair();

            // Convert the duplex pipes to streams to test the I/O behavior.
            Stream stream1 = pipe1.AsStream();
            Stream stream2 = pipe2.AsStream();

            // Arrange message data for communication from pipe1 to pipe2.
            byte[] messageFromPipe1 = Encoding.UTF8.GetBytes("Hello from pipe1");
            byte[] readBuffer1 = new byte[messageFromPipe1.Length];

            // Act: Write data on stream1.
            await stream1.WriteAsync(messageFromPipe1, 0, messageFromPipe1.Length);
            await stream1.FlushAsync();
            await Task.Delay(50);
            int bytesRead1 = await stream2.ReadAsync(readBuffer1, 0, readBuffer1.Length);

            // Assert: Validate that stream2 received correct data.
            Assert.Equal(messageFromPipe1.Length, bytesRead1);
            Assert.Equal(messageFromPipe1, readBuffer1);

            // Arrange message data for communication from pipe2 to pipe1.
            byte[] messageFromPipe2 = Encoding.UTF8.GetBytes("Reply from pipe2");
            byte[] readBuffer2 = new byte[messageFromPipe2.Length];

            // Act: Write data on stream2.
            await stream2.WriteAsync(messageFromPipe2, 0, messageFromPipe2.Length);
            await stream2.FlushAsync();
            await Task.Delay(50);
            int bytesRead2 = await stream1.ReadAsync(readBuffer2, 0, readBuffer2.Length);

            // Assert: Validate that stream1 received the correct response.
            Assert.Equal(messageFromPipe2.Length, bytesRead2);
            Assert.Equal(messageFromPipe2, readBuffer2);
        }

        /// <summary>
        /// Verifies that Splice correctly creates a full duplex stream from a readable stream and a writable stream.
        /// The test confirms that data is correctly read from the provided readable stream and written to the writable stream.
        /// Additionally, properties and disposal behavior are validated.
        /// </summary>
        [Fact]
        public async Task Splice_WhenCalledWithValidStreams_ReadAndWriteWork()
        {
            // Arrange: Create a readable stream with preset content and an empty writable stream.
            byte[] initialData = Encoding.UTF8.GetBytes("Initial data");
            MemoryStream readable = new MemoryStream(initialData);
            MemoryStream writable = new MemoryStream();

            // Act: Create a spliced combined stream.
            Stream combined = FullDuplexStream.Splice(readable, writable);

            // Act: Reading from the combined stream should return the underlying readable stream's data.
            byte[] readBuffer = new byte[initialData.Length];
            int bytesRead = await combined.ReadAsync(readBuffer, 0, readBuffer.Length);

            // Assert: Validate the read content.
            Assert.Equal(initialData.Length, bytesRead);
            Assert.Equal(initialData, readBuffer);

            // Act: Writing to the combined stream should write to the underlying writable stream.
            byte[] newData = Encoding.UTF8.GetBytes("New data");
            await combined.WriteAsync(newData, 0, newData.Length);
            combined.Flush();
            // Reset writable stream's position to verify written data.
            writable.Position = 0;
            byte[] writeBuffer = new byte[newData.Length];
            int bytesWritten = await writable.ReadAsync(writeBuffer, 0, writeBuffer.Length);

            // Assert: Validate the written content.
            Assert.Equal(newData.Length, bytesWritten);
            Assert.Equal(newData, writeBuffer);

            // Verify properties before disposal.
            Assert.True(combined.CanRead);
            Assert.True(combined.CanWrite);

            // Act: Dispose the combined stream.
            combined.Dispose();

            // Verify properties after disposal.
            Assert.False(combined.CanRead);
            Assert.False(combined.CanWrite);

            // Assert: Subsequent read and write operations should throw ObjectDisposedException.
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await combined.ReadAsync(new byte[1], 0, 1));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await combined.WriteAsync(new byte[1], 0, 1));
        }

        /// <summary>
        /// Verifies that Splice throws an ArgumentNullException when the readable stream is null.
        /// </summary>
        [Fact]
        public void Splice_NullReadableStream_ThrowsArgumentNullException()
        {
            // Arrange: Prepare a valid writable stream.
            MemoryStream writable = new MemoryStream();

            // Act & Assert: Expect ArgumentNullException when the readable stream is null.
            Assert.Throws<ArgumentNullException>(() => FullDuplexStream.Splice(null, writable));
        }

        /// <summary>
        /// Verifies that Splice throws an ArgumentNullException when the writable stream is null.
        /// </summary>
        [Fact]
        public void Splice_NullWritableStream_ThrowsArgumentNullException()
        {
            // Arrange: Prepare a valid readable stream.
            MemoryStream readable = new MemoryStream();

            // Act & Assert: Expect ArgumentNullException when the writable stream is null.
            Assert.Throws<ArgumentNullException>(() => FullDuplexStream.Splice(readable, null));
        }

        /// <summary>
        /// Verifies that Splice throws an ArgumentException when the provided readable stream is not readable.
        /// </summary>
        [Fact]
        public void Splice_NonReadableStream_ThrowsArgumentException()
        {
            // Arrange: Create a stream that reports CanRead as false.
            Stream nonReadable = new NonReadableStream();
            MemoryStream writable = new MemoryStream();

            // Act & Assert: Expect ArgumentException with a message indicating the stream must be readable.
            ArgumentException ex = Assert.Throws<ArgumentException>(() => FullDuplexStream.Splice(nonReadable, writable));
            Assert.Contains("Must be readable", ex.Message);
        }

        /// <summary>
        /// Verifies that Splice throws an ArgumentException when the provided writable stream is not writable.
        /// </summary>
        [Fact]
        public void Splice_NonWritableStream_ThrowsArgumentException()
        {
            // Arrange: Create a stream that reports CanWrite as false.
            MemoryStream readable = new MemoryStream(new byte[] { 1, 2, 3 });
            Stream nonWritable = new NonWritableStream();

            // Act & Assert: Expect ArgumentException with a message indicating the stream must be writable.
            ArgumentException ex = Assert.Throws<ArgumentException>(() => FullDuplexStream.Splice(readable, nonWritable));
            Assert.Contains("Must be writable", ex.Message);
        }

        /// <summary>
        /// A helper stream that simulates a non-readable stream by overriding CanRead to false.
        /// </summary>
        private class NonReadableStream : MemoryStream
        {
            public override bool CanRead => false;
        }

        /// <summary>
        /// A helper stream that simulates a non-writable stream by overriding CanWrite to false.
        /// </summary>
        private class NonWritableStream : MemoryStream
        {
            public override bool CanWrite => false;
        }
    }
}
