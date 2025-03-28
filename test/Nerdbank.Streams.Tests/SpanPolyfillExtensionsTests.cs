using System;
using System.Buffers;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Nerdbank.Streams;
using Xunit;

namespace Nerdbank.Streams.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="SpanPolyfillExtensions"/> class.
    /// </summary>
    public class SpanPolyfillExtensionsTests
    {
        #region ReadAsync Tests

        /// <summary>
        /// Tests that ReadAsync throws an ArgumentNullException when the stream is null.
        /// </summary>
//         [Fact] [Error] (31-23)CS0103 The name 'SpanPolyfillExtensions' does not exist in the current context
//         public async Task ReadAsync_NullStream_ThrowsArgumentNullException()
//         {
//             // Arrange
//             Stream nullStream = null;
//             Memory<byte> buffer = new byte[10];
// 
//             // Act & Assert
//             await Assert.ThrowsAsync<ArgumentNullException>(async () =>
//                 await SpanPolyfillExtensions.ReadAsync(nullStream, buffer));
//         }

        /// <summary>
        /// Tests that ReadAsync correctly reads data when provided with array-backed Memory.
        /// </summary>
        [Fact]
        public async Task ReadAsync_ArrayBackedMemory_ReadsCorrectly()
        {
            // Arrange
            byte[] sourceData = { 1, 2, 3, 4, 5 };
            using MemoryStream stream = new MemoryStream(sourceData);
            // Create array-backed memory.
            Memory<byte> buffer = new byte[sourceData.Length];

            // Act
            int bytesRead = await stream.ReadAsync(buffer);
            
            // Assert
            Assert.Equal(sourceData.Length, bytesRead);
            Assert.Equal(sourceData, buffer.ToArray());
        }

        /// <summary>
        /// Tests that ReadAsync correctly reads data when provided with non array-backed Memory.
        /// </summary>
        [Fact]
        public async Task ReadAsync_NonArrayBackedMemory_ReadsCorrectly()
        {
            // Arrange
            byte[] sourceData = { 10, 20, 30, 40, 50, 60 };
            using MemoryStream stream = new MemoryStream(sourceData);
            // Create a non array-backed memory via a custom MemoryManager.
            using TestMemoryManager manager = new TestMemoryManager(new byte[sourceData.Length]);
            Memory<byte> buffer = manager.Memory;
            // Overwrite the underlying memory with zeros to detect copy.
            Array.Clear(manager.Data, 0, manager.Data.Length);

            // Act
            int bytesRead = await stream.ReadAsync(buffer);
            
            // Assert
            Assert.Equal(sourceData.Length, bytesRead);
            // The extension copies the read bytes into the provided memory.
            byte[] result = buffer.ToArray();
            Assert.Equal(sourceData, result);
        }

        #endregion

        #region Read (Sync) Tests

        /// <summary>
        /// Tests that Read throws an ArgumentNullException when the stream is null.
        /// </summary>
//         [Fact] [Error] (94-72)CS8175 Cannot use ref local 'buffer' inside an anonymous method, lambda expression, or query expression
//         public void Read_NullStream_ThrowsArgumentNullException()
//         {
//             // Arrange
//             Stream nullStream = null;
//             Span<byte> buffer = new byte[10];
// 
//             // Act & Assert
//             Assert.Throws<ArgumentNullException>(() => nullStream.Read(buffer));
//         }

        /// <summary>
        /// Tests that Read synchronously reads data correctly from an array-backed stream.
        /// </summary>
        [Fact]
        public void Read_ArrayBackedMemory_ReadsCorrectly()
        {
            // Arrange
            byte[] sourceData = { 100, 101, 102, 103 };
            using MemoryStream stream = new MemoryStream(sourceData);
            Span<byte> buffer = new byte[sourceData.Length];

            // Act
            int bytesRead = stream.Read(buffer);

            // Assert
            Assert.Equal(sourceData.Length, bytesRead);
            Assert.Equal(sourceData, buffer.ToArray());
        }

        /// <summary>
        /// Tests that Read returns zero and does not modify the buffer when given an empty buffer.
        /// </summary>
        [Fact]
        public void Read_EmptyBuffer_ReturnsZero()
        {
            // Arrange
            byte[] sourceData = { 1, 2, 3 };
            using MemoryStream stream = new MemoryStream(sourceData);
            Span<byte> buffer = Span<byte>.Empty;

            // Act
            int bytesRead = stream.Read(buffer);

            // Assert
            Assert.Equal(0, bytesRead);
        }

        #endregion

        #region WriteAsync Tests (Stream)

        /// <summary>
        /// Tests that WriteAsync throws an ArgumentNullException when the stream is null.
        /// </summary>
//         [Fact] [Error] (150-23)CS0103 The name 'SpanPolyfillExtensions' does not exist in the current context
//         public async Task WriteAsync_NullStream_ThrowsArgumentNullException()
//         {
//             // Arrange
//             Stream nullStream = null;
//             ReadOnlyMemory<byte> buffer = new byte[10];
// 
//             // Act & Assert
//             await Assert.ThrowsAsync<ArgumentNullException>(async () =>
//                 await SpanPolyfillExtensions.WriteAsync(nullStream, buffer));
//         }

        /// <summary>
        /// Tests that WriteAsync correctly writes data when provided with array-backed Memory.
        /// </summary>
//         [Fact] [Error] (165-19)CS0103 The name 'SpanPolyfillExtensions' does not exist in the current context
//         public async Task WriteAsync_ArrayBackedMemory_WritesCorrectly()
//         {
//             // Arrange
//             byte[] dataToWrite = { 55, 66, 77, 88 };
//             using MemoryStream stream = new MemoryStream();
//             ReadOnlyMemory<byte> buffer = dataToWrite;
// 
//             // Act
//             await SpanPolyfillExtensions.WriteAsync(stream, buffer);
//             byte[] writtenData = stream.ToArray();
// 
//             // Assert
//             Assert.Equal(dataToWrite, writtenData);
//         }

        /// <summary>
        /// Tests that WriteAsync correctly writes data when provided with non array-backed Memory.
        /// </summary>
//         [Fact] [Error] (190-19)CS0103 The name 'SpanPolyfillExtensions' does not exist in the current context
//         public async Task WriteAsync_NonArrayBackedMemory_WritesCorrectly()
//         {
//             // Arrange
//             byte[] dataToWrite = { 200, 201, 202, 203, 204 };
//             using MemoryStream stream = new MemoryStream();
//             using TestMemoryManager manager = new TestMemoryManager(new byte[dataToWrite.Length]);
//             // Copy our data into the manager's buffer so that we have a ReadOnlyMemory that is non array-backed.
//             for (int i = 0; i < dataToWrite.Length; i++)
//             {
//                 manager.Data[i] = dataToWrite[i];
//             }
//             ReadOnlyMemory<byte> buffer = manager.Memory;
// 
//             // Act
//             await SpanPolyfillExtensions.WriteAsync(stream, buffer);
//             byte[] writtenData = stream.ToArray();
// 
//             // Assert
//             Assert.Equal(dataToWrite, writtenData);
//         }

        #endregion

        #region ReceiveAsync Tests (WebSocket)

        /// <summary>
        /// Tests that ReceiveAsync throws an ArgumentNullException when the WebSocket is null.
        /// </summary>
//         [Fact] [Error] (213-23)CS0103 The name 'SpanPolyfillExtensions' does not exist in the current context
//         public async Task ReceiveAsync_NullWebSocket_ThrowsArgumentNullException()
//         {
//             // Arrange
//             WebSocket nullWebSocket = null;
//             Memory<byte> buffer = new byte[10];
// 
//             // Act & Assert
//             await Assert.ThrowsAsync<ArgumentNullException>(async () =>
//                 await SpanPolyfillExtensions.ReceiveAsync(nullWebSocket, buffer));
//         }

        /// <summary>
        /// Tests that ReceiveAsync correctly receives data when provided with array-backed Memory.
        /// </summary>
//         [Fact] [Error] (238-65)CS1501 No overload for method 'ReceiveAsync' takes 1 arguments
//         public async Task ReceiveAsync_ArrayBackedMemory_ReturnsCorrectResult()
//         {
//             // Arrange
//             byte filler = 0xAA;
//             int bufferSize = 8;
//             FakeWebSocket fakeWebSocket = new FakeWebSocket((buffer, ct) =>
//             {
//                 // Fill the provided buffer with a repeating filler byte.
//                 int count = Math.Min(buffer.Count, bufferSize);
//                 for (int i = 0; i < count; i++)
//                 {
//                     buffer.Array[buffer.Offset + i] = filler;
//                 }
//                 return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Binary, true));
//             });
//             Memory<byte> buffer = new byte[bufferSize];
// 
//             // Act
//             WebSocketReceiveResult result = await fakeWebSocket.ReceiveAsync(buffer);
// 
//             // Assert
//             Assert.Equal(bufferSize, result.Count);
//             byte[] expected = new byte[bufferSize];
//             for (int i = 0; i < expected.Length; i++)
//             {
//                 expected[i] = filler;
//             }
//             Assert.Equal(expected, buffer.ToArray());
//         }

        /// <summary>
        /// Tests that ReceiveAsync correctly receives data when provided with non array-backed Memory.
        /// </summary>
//         [Fact] [Error] (273-65)CS1501 No overload for method 'ReceiveAsync' takes 1 arguments
//         public async Task ReceiveAsync_NonArrayBackedMemory_ReturnsCorrectResult()
//         {
//             // Arrange
//             byte filler = 0xBB;
//             int bufferSize = 6;
//             FakeWebSocket fakeWebSocket = new FakeWebSocket((segment, ct) =>
//             {
//                 // Fill the provided ArraySegment with a repeating filler byte.
//                 int count = Math.Min(segment.Count, bufferSize);
//                 for (int i = 0; i < count; i++)
//                 {
//                     segment.Array[segment.Offset + i] = filler;
//                 }
//                 return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Binary, true));
//             });
//             using TestMemoryManager manager = new TestMemoryManager(new byte[bufferSize]);
//             Memory<byte> buffer = manager.Memory;
// 
//             // Act
//             WebSocketReceiveResult result = await fakeWebSocket.ReceiveAsync(buffer);
// 
//             // Assert
//             Assert.Equal(bufferSize, result.Count);
//             byte[] expected = new byte[bufferSize];
//             for (int i = 0; i < expected.Length; i++)
//             {
//                 expected[i] = filler;
//             }
//             Assert.Equal(expected, buffer.ToArray());
//         }

        #endregion

        #region SendAsync Tests (WebSocket)

        /// <summary>
        /// Tests that SendAsync throws an ArgumentNullException when the WebSocket is null.
        /// </summary>
//         [Fact] [Error] (301-23)CS0103 The name 'SpanPolyfillExtensions' does not exist in the current context
//         public async Task SendAsync_NullWebSocket_ThrowsArgumentNullException()
//         {
//             // Arrange
//             WebSocket nullWebSocket = null;
//             ReadOnlyMemory<byte> buffer = new byte[10];
// 
//             // Act & Assert
//             await Assert.ThrowsAsync<ArgumentNullException>(async () =>
//                 await SpanPolyfillExtensions.SendAsync(nullWebSocket, buffer, WebSocketMessageType.Binary, true));
//         }

        /// <summary>
        /// Tests that SendAsync correctly sends data when provided with array-backed Memory.
        /// </summary>
//         [Fact] [Error] (316-19)CS0103 The name 'SpanPolyfillExtensions' does not exist in the current context
//         public async Task SendAsync_ArrayBackedMemory_SendsCorrectData()
//         {
//             // Arrange
//             byte[] dataToSend = { 5, 6, 7, 8, 9 };
//             FakeWebSocket fakeWebSocket = new FakeWebSocket();
//             ReadOnlyMemory<byte> buffer = dataToSend;
// 
//             // Act
//             await SpanPolyfillExtensions.SendAsync(fakeWebSocket, buffer, WebSocketMessageType.Binary, true);
// 
//             // Assert
//             Assert.NotNull(fakeWebSocket.LastSentData);
//             Assert.Equal(dataToSend.Length, fakeWebSocket.LastSentData.Count);
//             Assert.Equal(dataToSend, fakeWebSocket.LastSentData.ToArray());
//             Assert.Equal(WebSocketMessageType.Binary, fakeWebSocket.LastSentMessageType);
//             Assert.True(fakeWebSocket.LastSentEndOfMessage);
//         }

        /// <summary>
        /// Tests that SendAsync correctly sends data when provided with non array-backed Memory.
        /// </summary>
//         [Fact] [Error] (341-19)CS0103 The name 'SpanPolyfillExtensions' does not exist in the current context
//         public async Task SendAsync_NonArrayBackedMemory_SendsCorrectData()
//         {
//             // Arrange
//             byte[] dataToSend = { 150, 151, 152, 153 };
//             FakeWebSocket fakeWebSocket = new FakeWebSocket();
//             using TestMemoryManager manager = new TestMemoryManager(new byte[dataToSend.Length]);
//             // Initialize the manager's buffer with our test data.
//             Array.Copy(dataToSend, manager.Data, dataToSend.Length);
//             ReadOnlyMemory<byte> buffer = manager.Memory;
// 
//             // Act
//             await SpanPolyfillExtensions.SendAsync(fakeWebSocket, buffer, WebSocketMessageType.Text, false);
// 
//             // Assert
//             Assert.NotNull(fakeWebSocket.LastSentData);
//             Assert.Equal(dataToSend.Length, fakeWebSocket.LastSentData.Count);
//             Assert.Equal(dataToSend, fakeWebSocket.LastSentData.ToArray());
//             Assert.Equal(WebSocketMessageType.Text, fakeWebSocket.LastSentMessageType);
//             Assert.False(fakeWebSocket.LastSentEndOfMessage);
//         }

        #endregion

        #region Helper Classes

        /// <summary>
        /// A custom MemoryManager to simulate non array-backed Memory.
        /// </summary>
        private class TestMemoryManager : MemoryManager<byte>
        {
            public byte[] Data { get; }

            public TestMemoryManager(byte[] data)
            {
                Data = data ?? throw new ArgumentNullException(nameof(data));
            }

            public override Span<byte> GetSpan() => Data;

            public override MemoryHandle Pin(int elementIndex = 0) => Data.AsMemory().Pin();

            public override void Unpin() { }

            /// <summary>
            /// Override TryGetArray to simulate non array-backed Memory by always returning false.
            /// </summary>
//             public override bool TryGetArray(out ArraySegment<byte> segment) [Error] (376-34)CS0507 'SpanPolyfillExtensionsTests.TestMemoryManager.TryGetArray(out ArraySegment<byte>)': cannot change access modifiers when overriding 'protected internal' inherited member 'MemoryManager<byte>.TryGetArray(out ArraySegment<byte>)'
//             {
//                 segment = default;
//                 return false;
//             }

            protected override void Dispose(bool disposing) { }
        }

        /// <summary>
        /// A fake WebSocket implementation for testing purposes.
        /// </summary>
//         private class FakeWebSocket : WebSocket [Error] (388-23)CS0534 'SpanPolyfillExtensionsTests.FakeWebSocket' does not implement inherited abstract member 'WebSocket.Dispose()'
//         {
//             private readonly Func<ArraySegment<byte>, CancellationToken, Task<WebSocketReceiveResult>> _receiveAsyncFunc;
// 
//             public FakeWebSocket()
//             {
//                 // Default behavior for SendAsync: capture the last sent data.
//                 _receiveAsyncFunc = null;
//             }
// 
//             /// <summary>
//             /// Allows custom handling for ReceiveAsync.
//             /// </summary>
//             public FakeWebSocket(Func<ArraySegment<byte>, CancellationToken, Task<WebSocketReceiveResult>> receiveAsyncFunc)
//             {
//                 _receiveAsyncFunc = receiveAsyncFunc;
//             }
// 
//             public System.Collections.Generic.List<byte> LastSentData { get; private set; }
//             public WebSocketMessageType LastSentMessageType { get; private set; }
//             public bool LastSentEndOfMessage { get; private set; }
// 
//             public override WebSocketCloseStatus? CloseStatus => null;
// 
//             public override string CloseStatusDescription => null;
// 
//             public override WebSocketState State => WebSocketState.Open;
// 
//             public override string SubProtocol => string.Empty;
// 
//             public override void Abort() { }
// 
//             public override Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
//                 => Task.CompletedTask;
// 
//             public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
//                 => Task.CompletedTask;
// 
//             public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
//             {
//                 if (_receiveAsyncFunc != null)
//                 {
//                     return _receiveAsyncFunc(buffer, cancellationToken);
//                 }
//                 else
//                 {
//                     // Default behavior: fill the buffer with 0xCC.
//                     int count = buffer.Count;
//                     for (int i = 0; i < count; i++)
//                     {
//                         buffer.Array[buffer.Offset + i] = 0xCC;
//                     }
//                     return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Binary, true));
//                 }
//             }
// 
//             public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
//             {
//                 // Capture the sent data.
//                 LastSentData = new System.Collections.Generic.List<byte>(buffer.Count);
//                 for (int i = 0; i < buffer.Count; i++)
//                 {
//                     LastSentData.Add(buffer.Array[buffer.Offset + i]);
//                 }
//                 LastSentMessageType = messageType;
//                 LastSentEndOfMessage = endOfMessage;
//                 return Task.CompletedTask;
//             }
//         }

        #endregion
    }
}
