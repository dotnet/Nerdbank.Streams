using MessagePack;
using NSubstitute;
using System;
using System.Buffers;
using Xunit;

namespace MessagePack.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="Utilities"/> class.
    /// </summary>
    public class UtilitiesTests
    {
        /// <summary>
        /// Tests the GetWriterBytes method with a valid action delegate that writes an Int32 to the underlying writer.
        /// The expected outcome is that the returned byte array correctly encodes the input integer.
        /// </summary>
//         [Fact] [Error] (25-29)CS0122 'Utilities' is inaccessible due to its protection level
//         public void GetWriterBytes_WithValidAction_ReturnsExpectedByteArray()
//         {
//             // Arrange
//             int valueToWrite = 42;
//             
//             // Act
//             byte[] result = Utilities.GetWriterBytes(valueToWrite, (ref MessagePackWriter writer, int arg) =>
//             {
//                 // Write the integer using MessagePackWriter's WriteInt32 method.
//                 // This should result in a MessagePack encoded integer.
//                 writer.WriteInt32(arg);
//             });
// 
//             // Assert
//             Assert.NotNull(result);
//             Assert.NotEmpty(result);
// 
//             // To verify, decode the written value using MessagePackReader.
//             var reader = new MessagePackReader(result);
//             int decoded = reader.ReadInt32();
//             Assert.Equal(valueToWrite, decoded);
//         }

        /// <summary>
        /// Tests that GetWriterBytes throws a NullReferenceException when a null action delegate is passed.
        /// The expected outcome is that an exception is thrown.
        /// </summary>
//         [Fact] [Error] (51-32)CS0122 'Utilities' is inaccessible due to its protection level
//         public void GetWriterBytes_WithNullAction_ThrowsNullReferenceException()
//         {
//             // Arrange
//             int valueToWrite = 42;
//             Action act = () => Utilities.GetWriterBytes(valueToWrite, null);
// 
//             // Act & Assert
//             Assert.Throws<NullReferenceException>(act);
//         }

        /// <summary>
        /// Tests the GetMemoryCheckResult extension method when the underlying IBufferWriter returns a non-empty memory block.
        /// The expected outcome is that the returned memory block matches the one provided by GetMemory.
        /// </summary>
//         [Fact] [Error] (70-58)CS1061 'IBufferWriter<byte>' does not contain a definition for 'GetMemoryCheckResult' and no accessible extension method 'GetMemoryCheckResult' accepting a first argument of type 'IBufferWriter<byte>' could be found (are you missing a using directive or an assembly reference?)
//         public void GetMemoryCheckResult_WithNonEmptyMemory_ReturnsSameMemory()
//         {
//             // Arrange
//             var expectedArray = new byte[10];
//             IBufferWriter<byte> fakeBufferWriter = Substitute.For<IBufferWriter<byte>>();
//             fakeBufferWriter.GetMemory(Arg.Any<int>()).Returns(expectedArray);
// 
//             // Act
//             Memory<byte> resultMemory = fakeBufferWriter.GetMemoryCheckResult();
// 
//             // Assert
//             Assert.False(resultMemory.IsEmpty);
//             Assert.Equal(expectedArray.Length, resultMemory.Length);
//         }

        /// <summary>
        /// Tests that GetMemoryCheckResult throws an InvalidOperationException when the underlying IBufferWriter returns an empty memory block.
        /// The expected outcome is that the exception message contains the expected error message and the type name of the buffer writer.
        /// </summary>
//         [Fact] [Error] (90-115)CS1061 'IBufferWriter<byte>' does not contain a definition for 'GetMemoryCheckResult' and no accessible extension method 'GetMemoryCheckResult' accepting a first argument of type 'IBufferWriter<byte>' could be found (are you missing a using directive or an assembly reference?)
//         public void GetMemoryCheckResult_WithEmptyMemory_ThrowsInvalidOperationException()
//         {
//             // Arrange
//             IBufferWriter<byte> fakeBufferWriter = Substitute.For<IBufferWriter<byte>>();
//             fakeBufferWriter.GetMemory(Arg.Any<int>()).Returns(Memory<byte>.Empty);
//             string expectedMessagePart = "The underlying IBufferWriter<byte>.GetMemory(int) method returned an empty memory block, which is not allowed. This is a bug in ";
// 
//             // Act & Assert
//             InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => fakeBufferWriter.GetMemoryCheckResult());
//             Assert.Contains(expectedMessagePart, exception.Message);
//             Assert.Contains(fakeBufferWriter.GetType().FullName, exception.Message);
//         }
    }
}
