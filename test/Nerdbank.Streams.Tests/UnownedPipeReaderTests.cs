// using System;
// using System.IO.Pipelines;
// using System.Threading;
// using System.Threading.Tasks;
// using NSubstitute;
// using Nerdbank.Streams;
// using Xunit;
// 
// namespace Nerdbank.Streams.UnitTests
// {
//     /// <summary>
//     /// Unit tests for the <see cref="UnownedPipeReader"/> class.
//     /// </summary>
// //     public class UnownedPipeReaderTests [Error] (26-38)CS0122 'UnownedPipeReader' is inaccessible due to its protection level
// //     {
// //         private readonly PipeReader _mockUnderlyingReader;
// //         private readonly UnownedPipeReader _unownedPipeReader; [Error] (17-26)CS0122 'UnownedPipeReader' is inaccessible due to its protection level
//         private const string ExpectedExceptionMessage = "This object is owned by another context and should not be accessed from another.";
// 
//         /// <summary>
//         /// Initializes a new instance of the <see cref="UnownedPipeReaderTests"/> class and sets up the underlying PipeReader mock.
//         /// </summary>
//         public UnownedPipeReaderTests()
//         {
//             _mockUnderlyingReader = Substitute.For<PipeReader>();
//             _unownedPipeReader = new UnownedPipeReader(_mockUnderlyingReader);
//         }
// 
//         /// <summary>
//         /// Verifies that CancelPendingRead delegates the call to the underlying PipeReader.
//         /// </summary>
//         [Fact]
//         public void CancelPendingRead_WhenCalled_DelegatesToUnderlyingReader()
//         {
//             // Act
//             _unownedPipeReader.CancelPendingRead();
// 
//             // Assert
//             _mockUnderlyingReader.Received(1).CancelPendingRead();
//         }
// 
//         /// <summary>
//         /// Verifies that AdvanceTo with a single SequencePosition parameter throws an exception indicating the object is unowned.
//         /// </summary>
//         [Fact]
//         public void AdvanceTo_SingleParameter_ThrowsException()
//         {
//             // Arrange
//             SequencePosition position = default;
// 
//             // Act & Assert
//             Exception ex = Assert.Throws<Exception>(() => _unownedPipeReader.AdvanceTo(position));
//             Assert.Equal(ExpectedExceptionMessage, ex.Message);
//         }
// 
//         /// <summary>
//         /// Verifies that AdvanceTo with both consumed and examined SequencePosition parameters throws an exception indicating the object is unowned.
//         /// </summary>
//         [Fact]
//         public void AdvanceTo_TwoParameters_ThrowsException()
//         {
//             // Arrange
//             SequencePosition consumed = default;
//             SequencePosition examined = default;
// 
//             // Act & Assert
//             Exception ex = Assert.Throws<Exception>(() => _unownedPipeReader.AdvanceTo(consumed, examined));
//             Assert.Equal(ExpectedExceptionMessage, ex.Message);
//         }
// 
//         /// <summary>
//         /// Verifies that Complete throwing an exception when invoked with a null exception.
//         /// </summary>
//         [Fact]
//         public void Complete_NullException_ThrowsException()
//         {
//             // Act & Assert
//             Exception ex = Assert.Throws<Exception>(() => _unownedPipeReader.Complete(null));
//             Assert.Equal(ExpectedExceptionMessage, ex.Message);
//         }
// 
//         /// <summary>
//         /// Verifies that Complete throwing an exception when invoked with a non-null exception.
//         /// </summary>
//         [Fact]
//         public void Complete_WithException_ThrowsException()
//         {
//             // Arrange
//             Exception testException = new Exception("Test");
// 
//             // Act & Assert
//             Exception ex = Assert.Throws<Exception>(() => _unownedPipeReader.Complete(testException));
//             Assert.Equal(ExpectedExceptionMessage, ex.Message);
//         }
// 
//         /// <summary>
//         /// Verifies that CompleteAsync throws an exception when invoked with a null exception.
//         /// </summary>
//         [Fact]
//         public async Task CompleteAsync_NullException_ThrowsException()
//         {
//             // Act & Assert
//             Exception ex = await Assert.ThrowsAsync<Exception>(() => _unownedPipeReader.CompleteAsync(null).AsTask());
//             Assert.Equal(ExpectedExceptionMessage, ex.Message);
//         }
// 
//         /// <summary>
//         /// Verifies that CompleteAsync throws an exception when invoked with a non-null exception.
//         /// </summary>
//         [Fact]
//         public async Task CompleteAsync_WithException_ThrowsException()
//         {
//             // Arrange
//             Exception testException = new Exception("Test");
// 
//             // Act & Assert
//             Exception ex = await Assert.ThrowsAsync<Exception>(() => _unownedPipeReader.CompleteAsync(testException).AsTask());
//             Assert.Equal(ExpectedExceptionMessage, ex.Message);
//         }
// 
//         /// <summary>
//         /// Verifies that ReadAsync throws an exception when called.
//         /// </summary>
//         [Fact]
//         public async Task ReadAsync_WhenCalled_ThrowsException()
//         {
//             // Act & Assert
//             Exception ex = await Assert.ThrowsAsync<Exception>(() => _unownedPipeReader.ReadAsync(CancellationToken.None).AsTask());
//             Assert.Equal(ExpectedExceptionMessage, ex.Message);
//         }
// 
//         /// <summary>
//         /// Verifies that TryRead throws an exception when called.
//         /// </summary>
//         [Fact]
//         public void TryRead_WhenCalled_ThrowsException()
//         {
//             // Act & Assert
//             Exception ex = Assert.Throws<Exception>(() =>
//             {
//                 ReadResult result;
//                 _unownedPipeReader.TryRead(out result);
//             });
//             Assert.Equal(ExpectedExceptionMessage, ex.Message);
//         }
//     }
// }
