using MessagePack;
using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MessagePack.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref = "MessagePackStreamReader"/> class.
    /// </summary>
    public class MessagePackStreamReaderTests
    {
        /// <summary>
        /// Tests that the constructor throws an ArgumentNullException when a null stream is passed.
        /// </summary>
        [Fact]
        public void Constructor_WithNullStream_ThrowsArgumentNullException()
        {
            // Arrange, Act & Assert
            Assert.Throws<ArgumentNullException>(() => new MessagePackStreamReader(null));
        }

        /// <summary>
        /// Tests that ReadAsync returns null when the underlying stream is empty.
        /// </summary>
        [Fact]
        public async Task ReadAsync_EmptyStream_ReturnsNull()
        {
            // Arrange
            using var stream = new MemoryStream(Array.Empty<byte>());
            using var reader = new MessagePackStreamReader(stream);
            var cancellationToken = CancellationToken.None;
            // Act
            var result = await reader.ReadAsync(cancellationToken);
            // Assert
            Assert.Null(result);
        }

        /// <summary>
        /// Tests that ReadAsync throws an OperationCanceledException when the cancellation token is cancelled.
        /// </summary>
        [Fact]
        public async Task ReadAsync_WithCancellationRequested_ThrowsOperationCanceledException()
        {
            // Arrange
            byte[] data = new byte[]
            {
                0xC0
            };
            using var stream = new MemoryStream(data);
            using var reader = new MessagePackStreamReader(stream);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await reader.ReadAsync(cts.Token));
        }

        /// <summary>
        /// Tests that ReadAsync successfully returns a complete message when available.
        /// A message is simulated using a single byte (0xC0) representing a valid MessagePack "nil" message.
        /// </summary>
        [Fact]
        public async Task ReadAsync_WithCompleteMessage_ReturnsMessage()
        {
            // Arrange
            byte[] message = new byte[]
            {
                0xC0
            };
            using var stream = new MemoryStream(message);
            using var reader = new MessagePackStreamReader(stream);
            var cancellationToken = CancellationToken.None;
            // Act
            var result = await reader.ReadAsync(cancellationToken);
            // Assert
            Assert.NotNull(result);
            byte[] resultArray = result.Value.ToArray();
            Assert.Equal(message, resultArray);
        }

        /// <summary>
        /// Tests that when the underlying stream contains two consecutive messages, ReadAsync returns the first complete message and RemainingBytes reflects the next message.
        /// </summary>
        [Fact]
        public async Task ReadAsync_WithMultipleMessages_ReturnsFirstMessageAndRemainingDataCorrectly()
        {
            // Arrange
            byte[] messages = new byte[]
            {
                0xC0,
                0xC0
            };
            using var stream = new MemoryStream(messages);
            using var reader = new MessagePackStreamReader(stream);
            var cancellationToken = CancellationToken.None;
            // Act
            var firstMessage = await reader.ReadAsync(cancellationToken);
            // Assert: First message matches the first byte.
            Assert.NotNull(firstMessage);
            byte[] firstMessageArray = firstMessage.Value.ToArray();
            Assert.Equal(new byte[] { 0xC0 }, firstMessageArray);
            // Verify RemainingBytes returns the second message.
            ReadOnlyMemory<byte> remainingBytes = reader.RemainingBytes.ToArray();
            Assert.Equal(new byte[] { 0xC0 }, remainingBytes.ToArray());
        }

        /// <summary>
        /// Tests that DiscardBufferedData resets the internal buffer by clearing any buffered data.
        /// </summary>
        [Fact]
        public async Task DiscardBufferedData_ResetsBuffer()
        {
            // Arrange
            byte[] messages = new byte[]
            {
                0xC0,
                0xC0
            };
            using var stream = new MemoryStream(messages);
            using var reader = new MessagePackStreamReader(stream);
            var cancellationToken = CancellationToken.None;
            // Act: Read a message to cause buffering of data.
            var _ = await reader.ReadAsync(cancellationToken);
            Assert.NotEmpty(reader.RemainingBytes.ToArray());
            // Discard buffered data.
            reader.DiscardBufferedData();
            // Assert: The buffered data has been reset.
            Assert.Empty(reader.RemainingBytes.ToArray());
        }

        /// <summary>
        /// Tests that calling Dispose on MessagePackStreamReader disposes the underlying stream when leaveOpen is false.
        /// </summary>
        [Fact]
        public void Dispose_WithLeaveOpenFalse_DisposesUnderlyingStream()
        {
            // Arrange
            var testStream = new TestStream();
            var reader = new MessagePackStreamReader(testStream, leaveOpen: false);
            // Act
            reader.Dispose();
            // Assert
            Assert.True(testStream.IsDisposed);
        }

        /// <summary>
        /// Tests that calling Dispose on MessagePackStreamReader does not dispose the underlying stream when leaveOpen is true.
        /// </summary>
        [Fact]
        public void Dispose_WithLeaveOpenTrue_DoesNotDisposeUnderlyingStream()
        {
            // Arrange
            var testStream = new TestStream();
            var reader = new MessagePackStreamReader(testStream, leaveOpen: true);
            // Act
            reader.Dispose();
            // Assert
            Assert.False(testStream.IsDisposed);
        }

        /// <summary>
        /// A helper stream class to track disposal state.
        /// </summary>
        private class TestStream : MemoryStream
        {
            /// <summary>
            /// Indicates whether the stream has been disposed.
            /// </summary>
            public bool IsDisposed { get; private set; }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                IsDisposed = true;
            }
        }
    }
}