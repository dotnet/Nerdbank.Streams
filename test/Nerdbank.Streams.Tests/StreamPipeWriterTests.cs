// using Nerdbank.Streams;
// using NSubstitute;
// using System;
// using System.IO;
// using System.IO.Pipelines;
// using System.Text;
// using System.Threading;
// using System.Threading.Tasks;
// using Xunit;
// 
// namespace Nerdbank.Streams.UnitTests
// {
//     /// <summary>
//     /// Unit tests for the <see cref = "StreamPipeWriter"/> class.
//     /// </summary>
// //     public class StreamPipeWriterTests [Error] (24-27)CS0122 'StreamPipeWriter' is inaccessible due to its protection level
// //     {
// //         private readonly MemoryStream _memoryStream;
// //         private readonly StreamPipeWriter _writer; [Error] (19-26)CS0122 'StreamPipeWriter' is inaccessible due to its protection level
//         public StreamPipeWriterTests()
//         {
//             // Use MemoryStream as the underlying stream.
//             _memoryStream = new MemoryStream();
//             _writer = new StreamPipeWriter(_memoryStream);
//         }
// 
// #region Advance Tests
//         /// <summary>
//         /// Tests that Advance with positive bytes correctly causes the written data to be flushed to the stream.
//         /// </summary>
//         [Fact]
//         public async Task Advance_WithPositiveBytes_WritesDataToStream()
//         {
//             // Arrange
//             byte[] expectedData = Encoding.UTF8.GetBytes("abc");
//             Memory<byte> memory = _writer.GetMemory(expectedData.Length);
//             expectedData.CopyTo(memory);
//             _writer.Advance(expectedData.Length);
//             // Act
//             FlushResult result = await _writer.FlushAsync();
//             // Assert
//             Assert.False(result.IsCanceled);
//             Assert.True(result.IsCompleted);
//             _memoryStream.Seek(0, SeekOrigin.Begin);
//             byte[] actual = _memoryStream.ToArray();
//             Assert.Equal(expectedData, actual);
//         }
// 
//         /// <summary>
//         /// Tests that calling Advance after the writer has been completed throws an InvalidOperationException.
//         /// </summary>
//         [Fact]
//         public void Advance_AfterComplete_ThrowsInvalidOperationException()
//         {
//             // Arrange
//             _writer.Complete();
//             // Act & Assert
//             Assert.Throws<InvalidOperationException>(() => _writer.Advance(1));
//         }
// 
// #endregion
// #region FlushAsync Tests
//         /// <summary>
//         /// Tests that FlushAsync flushes buffered data to the underlying stream.
//         /// </summary>
//         [Fact]
//         public async Task FlushAsync_WithBufferedData_WritesDataToStream()
//         {
//             // Arrange
//             byte[] expectedData = Encoding.UTF8.GetBytes("flushTest");
//             Memory<byte> memory = _writer.GetMemory(expectedData.Length);
//             expectedData.CopyTo(memory);
//             _writer.Advance(expectedData.Length);
//             // Act
//             FlushResult result = await _writer.FlushAsync();
//             // Assert
//             Assert.False(result.IsCanceled);
//             Assert.True(result.IsCompleted);
//             _memoryStream.Seek(0, SeekOrigin.Begin);
//             byte[] actual = _memoryStream.ToArray();
//             Assert.Equal(expectedData, actual);
//         }
// 
//         /// <summary>
//         /// Tests that FlushAsync immediately throws an OperationCanceledException when provided a cancelled cancellation token.
//         /// </summary>
// //         [Fact] [Error] (94-72)CS0029 Cannot implicitly convert type 'System.Threading.Tasks.ValueTask<System.IO.Pipelines.FlushResult>' to 'System.Threading.Tasks.Task' [Error] (94-72)CS1662 Cannot convert lambda expression to intended delegate type because some of the return types in the block are not implicitly convertible to the delegate return type
// //         public async Task FlushAsync_WhenCancellationTokenAlreadyCancelled_ThrowsOperationCanceledException()
// //         {
// //             // Arrange
// //             using CancellationTokenSource cts = new CancellationTokenSource();
// //             cts.Cancel();
// //             // Act & Assert
// //             await Assert.ThrowsAsync<OperationCanceledException>(() => _writer.FlushAsync(cts.Token));
// //         }
// 
//         /// <summary>
//         /// Tests that FlushAsync returns a canceled FlushResult when CancelPendingFlush is called during flushing.
//         /// </summary>
// //         [Fact] [Error] (106-13)CS0122 'StreamPipeWriter' is inaccessible due to its protection level [Error] (106-47)CS0122 'StreamPipeWriter' is inaccessible due to its protection level [Error] (112-43)CS0029 Cannot implicitly convert type 'System.Threading.Tasks.ValueTask<System.IO.Pipelines.FlushResult>' to 'System.Threading.Tasks.Task<System.IO.Pipelines.FlushResult>'
// //         public async Task FlushAsync_WhenFlushIsCancelledViaCancelPendingFlush_ReturnsCanceledFlushResult()
// //         {
// //             // Arrange
// //             // Use a slow stream to simulate delay in WriteAsync and FlushAsync.
// //             using SlowWriteStream slowStream = new SlowWriteStream();
// //             StreamPipeWriter slowWriter = new StreamPipeWriter(slowStream);
// //             byte[] data = Encoding.UTF8.GetBytes("delayedData");
// //             Memory<byte> memory = slowWriter.GetMemory(data.Length);
// //             data.CopyTo(memory);
// //             slowWriter.Advance(data.Length);
// //             // Act
// //             Task<FlushResult> flushTask = slowWriter.FlushAsync();
// //             // Allow the flush to start.
// //             await Task.Delay(50);
// //             slowWriter.CancelPendingFlush();
// //             FlushResult result = await flushTask;
// //             // Assert
// //             Assert.True(result.IsCanceled);
// //             Assert.False(result.IsCompleted);
// //         }
// 
// #endregion
// #region Complete Tests
//         /// <summary>
//         /// Tests that calling Complete without buffered data triggers immediate reader completion callback.
//         /// </summary>
//         [Fact]
//         public async Task Complete_WithoutBufferedData_InvokesOnReaderCompletedImmediately()
//         {
//             // Arrange
//             bool callbackInvoked = false;
//             Exception? callbackException = null;
//             _writer.OnReaderCompleted((ex, state) =>
//             {
//                 callbackInvoked = true;
//                 callbackException = ex;
//             }, null);
//             // Act
//             _writer.Complete();
//             // Give a small delay to allow callback to execute.
//             await Task.Delay(50);
//             // Assert
//             Assert.True(callbackInvoked);
//             Assert.Null(callbackException);
//         }
// 
//         /// <summary>
//         /// Tests that calling Complete with buffered data flushes the data and then invokes the reader completion callback.
//         /// </summary>
//         [Fact]
//         public async Task Complete_WithBufferedData_FlushesDataAndInvokesOnReaderCompleted()
//         {
//             // Arrange
//             byte[] expectedData = Encoding.UTF8.GetBytes("completeTest");
//             Memory<byte> memory = _writer.GetMemory(expectedData.Length);
//             expectedData.CopyTo(memory);
//             _writer.Advance(expectedData.Length);
//             bool callbackInvoked = false;
//             Exception? callbackException = null;
//             _writer.OnReaderCompleted((ex, state) =>
//             {
//                 callbackInvoked = true;
//                 callbackException = ex;
//             }, null);
//             // Act
//             _writer.Complete();
//             // Wait sufficiently for implicit flush to complete.
//             await Task.Delay(100);
//             // Assert
//             _memoryStream.Seek(0, SeekOrigin.Begin);
//             byte[] actual = _memoryStream.ToArray();
//             Assert.Equal(expectedData, actual);
//             Assert.True(callbackInvoked);
//             Assert.Null(callbackException);
//         }
// 
// #endregion
// #region GetMemory Tests
//         /// <summary>
//         /// Tests that GetMemory returns writable memory when the writer has not been completed.
//         /// </summary>
//         [Fact]
//         public void GetMemory_BeforeComplete_ReturnsWritableMemory()
//         {
//             // Arrange
//             int sizeHint = 10;
//             // Act
//             Memory<byte> memory = _writer.GetMemory(sizeHint);
//             // Assert
//             Assert.True(memory.Length >= sizeHint);
//         }
// 
//         /// <summary>
//         /// Tests that GetMemory throws an exception when called after completing the writer.
//         /// </summary>
//         [Fact]
//         public void GetMemory_AfterComplete_ThrowsInvalidOperationException()
//         {
//             // Arrange
//             _writer.Complete();
//             // Act & Assert
//             Assert.Throws<InvalidOperationException>(() => _writer.GetMemory(5));
//         }
// 
// #endregion
// #region GetSpan Tests
//         /// <summary>
//         /// Tests that GetSpan returns a writable span when the writer has not been completed.
//         /// </summary>
//         [Fact]
//         public void GetSpan_BeforeComplete_ReturnsWritableSpan()
//         {
//             // Arrange
//             int sizeHint = 10;
//             // Act
//             Span<byte> span = _writer.GetSpan(sizeHint);
//             // Assert
//             Assert.True(span.Length >= sizeHint);
//         }
// 
//         /// <summary>
//         /// Tests that GetSpan throws an exception when called after completing the writer.
//         /// </summary>
//         [Fact]
//         public void GetSpan_AfterComplete_ThrowsInvalidOperationException()
//         {
//             // Arrange
//             _writer.Complete();
//             // Act & Assert
//             Assert.Throws<InvalidOperationException>(() => _writer.GetSpan(5));
//         }
// 
// #endregion
// #region OnReaderCompleted Tests
//         /// <summary>
//         /// Tests that OnReaderCompleted when called on an already completed writer invokes the callback immediately with the writer exception.
//         /// </summary>
//         [Fact]
//         public void OnReaderCompleted_WhenAlreadyCompleted_CallbackInvokedImmediately()
//         {
//             // Arrange
//             Exception completeException = new Exception("Writer complete exception");
//             bool callbackInvoked = false;
//             Exception? callbackException = null;
//             _writer.Complete(completeException);
//             // Act
//             _writer.OnReaderCompleted((ex, state) =>
//             {
//                 callbackInvoked = true;
//                 callbackException = ex;
//             }, null);
//             // Assert
//             Assert.True(callbackInvoked);
//             Assert.Equal(completeException, callbackException);
//         }
// 
//         /// <summary>
//         /// Tests that registering a callback with OnReaderCompleted when the writer is not yet completed does not invoke the callback immediately,
//         /// but does so after calling Complete.
//         /// </summary>
//         [Fact]
//         public async Task OnReaderCompleted_WhenNotCompleted_CallbackInvokedAfterComplete()
//         {
//             // Arrange
//             bool callbackInvoked = false;
//             Exception? callbackException = null;
//             _writer.OnReaderCompleted((ex, state) =>
//             {
//                 callbackInvoked = true;
//                 callbackException = ex;
//             }, null);
//             // Act
//             _writer.Complete();
//             await Task.Delay(50);
//             // Assert
//             Assert.True(callbackInvoked);
//             Assert.Null(callbackException);
//         }
// 
// #endregion
// #region SlowWriteStream Helper
//         /// <summary>
//         /// A helper stream that introduces delays in WriteAsync and FlushAsync to simulate slow operations, enabling cancellation tests.
//         /// </summary>
//         private class SlowWriteStream : MemoryStream
//         {
// //             public override async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) [Error] (287-40)CS0508 'StreamPipeWriterTests.SlowWriteStream.WriteAsync(ReadOnlyMemory<byte>, CancellationToken)': return type must be 'ValueTask' to match overridden member 'MemoryStream.WriteAsync(ReadOnlyMemory<byte>, CancellationToken)'
// //             {
// //                 // Introduce a delay to simulate a long-running write.
// //                 await Task.Delay(500, cancellationToken);
// //                 await base.WriteAsync(buffer, cancellationToken);
// //             }
// 
//             public override async Task FlushAsync(CancellationToken cancellationToken)
//             {
//                 // Introduce a delay to simulate a long-running flush.
//                 await Task.Delay(500, cancellationToken);
//                 await base.FlushAsync(cancellationToken);
//             }
//         }
// #endregion
//     }
// }
