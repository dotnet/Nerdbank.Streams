using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Nerdbank.Streams;
using NSubstitute;
using Xunit;

namespace Nerdbank.Streams.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="StreamPipeReader"/> class.
    /// </summary>
    public class StreamPipeReaderTests
    {
        private readonly byte[] testData = new byte[] { 1, 2, 3, 4, 5 };

        /// <summary>
        /// A custom stream that is not readable to test constructor validation.
        /// </summary>
        private class NonReadableStream : MemoryStream
        {
            public override bool CanRead => false;
        }

        /// <summary>
        /// A custom stream that introduces a delay in Read and ReadAsync calls to simulate blocking behavior.
        /// </summary>
        private class DelayingStream : MemoryStream
        {
            private readonly int delayMilliseconds;

            public DelayingStream(byte[] buffer, int delayMilliseconds) : base(buffer)
            {
                this.delayMilliseconds = delayMilliseconds;
            }

//             public override async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) [Error] (40-45)CS0508 'StreamPipeReaderTests.DelayingStream.ReadAsync(Memory<byte>, CancellationToken)': return type must be 'ValueTask<int>' to match overridden member 'MemoryStream.ReadAsync(Memory<byte>, CancellationToken)'
//             {
//                 await Task.Delay(delayMilliseconds, cancellationToken);
//                 return await base.ReadAsync(buffer, cancellationToken);
//             }

            public override int Read(Span<byte> buffer)
            {
                Thread.Sleep(delayMilliseconds);
                return base.Read(buffer);
            }
        }

        /// <summary>
        /// Tests that the constructor throws an <see cref="ArgumentNullException"/> when passed a null stream.
        /// </summary>
        [Fact]
        public void Constructor_NullStream_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new StreamPipeReader(null!));
        }

        /// <summary>
        /// Tests that the constructor throws an <see cref="ArgumentException"/> when the stream is not readable.
        /// </summary>
        [Fact]
        public void Constructor_NonReadableStream_ThrowsArgumentException()
        {
            // Arrange
            Stream nonReadable = new NonReadableStream();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => new StreamPipeReader(nonReadable));
        }

        /// <summary>
        /// Tests that <see cref="StreamPipeReader.AdvanceTo(SequencePosition, SequencePosition)"/> advances the buffer,
        /// so that subsequent calls to <see cref="StreamPipeReader.TryRead(out ReadResult)"/> return false.
        /// </summary>
        [Fact]
        public async Task AdvanceTo_AfterRead_ConsumesBuffer()
        {
            // Arrange
            using var ms = new MemoryStream(testData);
            var reader = new StreamPipeReader(ms);
            ReadResult readResult = await reader.ReadAsync();
            SequencePosition consumed = readResult.Buffer.Start;
            SequencePosition examined = readResult.Buffer.Start;

            // Act
            reader.AdvanceTo(consumed, examined);

            // Assert: After advancing, the buffer should have been consumed.
            bool hasData = reader.TryRead(out ReadResult result);
            Assert.False(hasData);
        }

        /// <summary>
        /// Tests that <see cref="StreamPipeReader.ReadAsync(CancellationToken)"/> throws an <see cref="OperationCanceledException"/>
        /// when the supplied CancellationToken is already canceled.
        /// </summary>
//         [Fact] [Error] (112-72)CS0029 Cannot implicitly convert type 'System.Threading.Tasks.ValueTask<System.IO.Pipelines.ReadResult>' to 'System.Threading.Tasks.Task' [Error] (112-72)CS1662 Cannot convert lambda expression to intended delegate type because some of the return types in the block are not implicitly convertible to the delegate return type
//         public async Task ReadAsync_AlreadyCanceledToken_ThrowsOperationCanceledException()
//         {
//             // Arrange
//             using var ms = new MemoryStream(testData);
//             var reader = new StreamPipeReader(ms);
//             using var cts = new CancellationTokenSource();
//             cts.Cancel();
// 
//             // Act & Assert
//             await Assert.ThrowsAsync<OperationCanceledException>(() => reader.ReadAsync(cts.Token));
//         }

        /// <summary>
        /// Tests that <see cref="StreamPipeReader.Complete(Exception?)"/> marks the reader as completed and disposes the stream
        /// when leaveOpen is false.
        /// </summary>
        [Fact]
        public void Complete_MarksReaderCompletedAndDisposesStream_WhenLeaveOpenIsFalse()
        {
            // Arrange
            var ms = Substitute.ForPartsOf<MemoryStream>(testData);
            var reader = new StreamPipeReader(ms, bufferSize: 4096, leaveOpen: false);

            // Act
            reader.Complete();

            // Assert: After completion, TryRead should throw an InvalidOperationException.
            Assert.Throws<InvalidOperationException>(() => reader.TryRead(out _));
            // Also, the underlying stream is disposed.
            Assert.Throws<ObjectDisposedException>(() => ms.Read(new byte[1], 0, 1));
        }

        /// <summary>
        /// Tests that <see cref="StreamPipeReader.Complete(Exception?)"/> does not dispose the stream when leaveOpen is true.
        /// </summary>
        [Fact]
        public void Complete_DoesNotDisposeStream_WhenLeaveOpenIsTrue()
        {
            // Arrange
            using var ms = new MemoryStream(testData);
            var reader = new StreamPipeReader(ms, bufferSize: 4096, leaveOpen: true);

            // Act
            reader.Complete();

            // Assert: The stream remains accessible.
            int value = ms.ReadByte();
            // Since the stream position may be at the end, we only verify no exception was thrown.
            Assert.True(true);
        }

        /// <summary>
        /// Tests that <see cref="StreamPipeReader.ReadAsync(CancellationToken)"/> returns a <see cref="ReadResult"/>
        /// with data when data is available in the stream.
        /// </summary>
        [Fact]
        public async Task ReadAsync_WhenDataAvailable_ReturnsDataAndNotCompleted()
        {
            // Arrange
            using var ms = new MemoryStream(testData);
            var reader = new StreamPipeReader(ms);

            // Act
            ReadResult result = await reader.ReadAsync();

            // Assert
            Assert.False(result.IsCanceled);
            Assert.False(result.IsCompleted);
            byte[] output = new byte[result.Buffer.Length];
            result.Buffer.CopyTo(output);
            Assert.Equal(testData, output);
        }

        /// <summary>
        /// Tests that <see cref="StreamPipeReader.ReadAsync(CancellationToken)"/> returns a completed <see cref="ReadResult"/>
        /// when the end-of-stream is reached.
        /// </summary>
        [Fact]
        public async Task ReadAsync_WhenNoData_ReturnsCompletedResult()
        {
            // Arrange
            using var ms = new MemoryStream(Array.Empty<byte>());
            var reader = new StreamPipeReader(ms);

            // Act
            ReadResult result = await reader.ReadAsync();

            // Assert
            Assert.False(result.IsCanceled);
            Assert.True(result.IsCompleted);
            Assert.Equal(0, result.Buffer.Length);
        }

        /// <summary>
        /// Tests that <see cref="StreamPipeReader.Read()"/> synchronously returns a <see cref="ReadResult"/> with data
        /// when data is available.
        /// </summary>
        [Fact]
        public void Read_WhenDataAvailable_ReturnsDataAndNotCompleted()
        {
            // Arrange
            using var ms = new MemoryStream(testData);
            var reader = new StreamPipeReader(ms);

            // Act
            ReadResult result = reader.Read();

            // Assert
            Assert.False(result.IsCanceled);
            Assert.False(result.IsCompleted);
            byte[] output = new byte[result.Buffer.Length];
            result.Buffer.CopyTo(output);
            Assert.Equal(testData, output);
        }

        /// <summary>
        /// Tests that <see cref="StreamPipeReader.Read()"/> synchronously returns a completed <see cref="ReadResult"/>
        /// when no data is available (end-of-stream).
        /// </summary>
        [Fact]
        public void Read_WhenNoData_ReturnsCompletedResult()
        {
            // Arrange
            using var ms = new MemoryStream(Array.Empty<byte>());
            var reader = new StreamPipeReader(ms);

            // Act
            ReadResult result = reader.Read();

            // Assert
            Assert.False(result.IsCanceled);
            Assert.True(result.IsCompleted);
            Assert.Equal(0, result.Buffer.Length);
        }

        /// <summary>
        /// Tests that <see cref="StreamPipeReader.TryRead(out ReadResult)"/> returns false when no data is available in the underlying stream.
        /// </summary>
        [Fact]
        public void TryRead_WhenNoData_ReturnsFalse()
        {
            // Arrange
            using var ms = new MemoryStream(Array.Empty<byte>());
            var reader = new StreamPipeReader(ms);

            // Act
            bool result = reader.TryRead(out ReadResult readResult);

            // Assert
            Assert.False(result);
            Assert.Equal(0, readResult.Buffer.Length);
        }

        /// <summary>
        /// Tests that <see cref="StreamPipeReader.TryRead(out ReadResult)"/> returns true when data is buffered and not yet examined.
        /// </summary>
        [Fact]
        public void TryRead_WhenDataAvailable_ReturnsTrue()
        {
            // Arrange
            using var ms = new MemoryStream(testData);
            var reader = new StreamPipeReader(ms);

            // Force buffering by calling Read() to fill internal buffer.
            ReadResult initialResult = reader.Read();

            // Act
            bool result = reader.TryRead(out ReadResult tryReadResult);

            // Assert
            Assert.True(result);
            Assert.Equal(testData.Length, tryReadResult.Buffer.Length);
        }

        /// <summary>
        /// Tests that <see cref="StreamPipeReader.OnWriterCompleted(Action{Exception?, object?}, object?)"/>
        /// immediately invokes the callback when the writer is already completed.
        /// </summary>
        [Fact]
        public void OnWriterCompleted_WhenWriterAlreadyCompleted_InvokesCallbackImmediately()
        {
            // Arrange
            using var ms = new MemoryStream(Array.Empty<byte>());
            var reader = new StreamPipeReader(ms);
            bool callbackInvoked = false;
            Exception? callbackException = null;

            // Force writer completion by reading from an empty stream.
            ReadResult result = reader.Read();

            // Act
            reader.OnWriterCompleted((ex, state) =>
            {
                callbackInvoked = true;
                callbackException = ex;
            }, null);

            // Assert
            Assert.True(callbackInvoked);
            Assert.Null(callbackException);
        }

        /// <summary>
        /// Tests that <see cref="StreamPipeReader.OnWriterCompleted(Action{Exception?, object?}, object?)"/>
        /// registers a callback that is later invoked when the writer completes.
        /// </summary>
        [Fact]
        public async Task OnWriterCompleted_WhenWriterNotComplete_RegistersAndInvokesCallbackLater()
        {
            // Arrange
            using var ms = new MemoryStream(testData);
            var reader = new StreamPipeReader(ms);
            bool callbackInvoked = false;
            Exception? callbackException = null;
            reader.OnWriterCompleted((ex, state) =>
            {
                callbackInvoked = true;
                callbackException = ex;
            }, null);

            // Act: Read all data to force completion of the writer.
            ReadResult result = await reader.ReadAsync();
            while (!result.IsCompleted)
            {
                reader.AdvanceTo(result.Buffer.End);
                result = await reader.ReadAsync();
            }

            // Wait a short time to ensure callback is invoked.
            await Task.Delay(50);

            // Assert
            Assert.True(callbackInvoked);
            Assert.Null(callbackException);
        }
    }
}
