using System;
using System.Text;
using MessagePack.Internal;
using Xunit;

namespace MessagePack.Internal.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="GuidBits"/> struct.
    /// </summary>
    public class GuidBitsTests
    {
        /// <summary>
        /// Tests that constructing GuidBits via the ref Guid constructor and writing its value produces the expected GUID string.
        /// </summary>
//         [Fact] [Error] (22-13)CS0122 'GuidBits' is inaccessible due to its protection level [Error] (22-37)CS0122 'GuidBits' is inaccessible due to its protection level
//         public void Write_FromRefConstructor_ReturnsCorrectGuidString()
//         {
//             // Arrange
//             Guid expectedGuid = new Guid("00112233-4455-6677-8899-aabbccddeeff");
//             Guid guid = expectedGuid;
//             GuidBits guidBits = new GuidBits(ref guid);
//             byte[] buffer = new byte[36];
// 
//             // Act
//             guidBits.Write(buffer);
//             string result = Encoding.ASCII.GetString(buffer);
// 
//             // Assert
//             Assert.Equal(expectedGuid.ToString("D"), result);
//         }

        /// <summary>
        /// Tests that constructing GuidBits from a valid 32-digit GUID string creates a struct that writes out the expected GUID string.
        /// </summary>
//         [Fact] [Error] (43-13)CS0122 'GuidBits' is inaccessible due to its protection level [Error] (43-37)CS0122 'GuidBits' is inaccessible due to its protection level
//         public void Constructor32DigitString_ValidInput_ReturnsExpectedGuid()
//         {
//             // Arrange
//             string guidString32 = "00112233445566778899aabbccddeeff";
//             byte[] inputBytes = Encoding.ASCII.GetBytes(guidString32);
//             Guid expectedGuid = new Guid("00112233-4455-6677-8899-aabbccddeeff");
//             GuidBits guidBits = new GuidBits(inputBytes);
//             byte[] buffer = new byte[36];
// 
//             // Act
//             guidBits.Write(buffer);
//             string result = Encoding.ASCII.GetString(buffer);
// 
//             // Assert
//             Assert.Equal(expectedGuid.ToString("D"), result);
//         }

        /// <summary>
        /// Tests that constructing GuidBits from a valid 36-digit GUID string creates a struct that writes out the expected GUID string.
        /// </summary>
//         [Fact] [Error] (64-13)CS0122 'GuidBits' is inaccessible due to its protection level [Error] (64-37)CS0122 'GuidBits' is inaccessible due to its protection level
//         public void Constructor36DigitString_ValidInput_ReturnsExpectedGuid()
//         {
//             // Arrange
//             string guidString36 = "00112233-4455-6677-8899-aabbccddeeff";
//             byte[] inputBytes = Encoding.ASCII.GetBytes(guidString36);
//             Guid expectedGuid = new Guid(guidString36);
//             GuidBits guidBits = new GuidBits(inputBytes);
//             byte[] buffer = new byte[36];
// 
//             // Act
//             guidBits.Write(buffer);
//             string result = Encoding.ASCII.GetString(buffer);
// 
//             // Assert
//             Assert.Equal(expectedGuid.ToString("D"), result);
//         }

        /// <summary>
        /// Tests that constructing GuidBits with an input of invalid length throws a MessagePackSerializationException.
        /// </summary>
        /// <param name="invalidInput">An invalid GUID string input.</param>
//         [Theory] [Error] (88-72)CS0122 'GuidBits' is inaccessible due to its protection level
//         [InlineData("1234567890")]
//         [InlineData("00112233-4455-6677-8899-aabbccddeef")]
//         public void Constructor_InvalidLength_ThrowsMessagePackSerializationException(string invalidInput)
//         {
//             // Arrange
//             byte[] inputBytes = Encoding.ASCII.GetBytes(invalidInput);
// 
//             // Act & Assert
//             Assert.Throws<MessagePackSerializationException>(() => new GuidBits(inputBytes));
//         }

        /// <summary>
        /// Tests that constructing GuidBits from a 36-digit GUID string with an invalid dash position throws a MessagePackSerializationException.
        /// </summary>
//         [Fact] [Error] (105-72)CS0122 'GuidBits' is inaccessible due to its protection level
//         public void Constructor36_InvalidDashPosition_ThrowsMessagePackSerializationException()
//         {
//             // Arrange
//             // Replace the expected dash at index 8 with an invalid character.
//             char[] chars = "00112233-4455-6677-8899-aabbccddeeff".ToCharArray();
//             chars[8] = 'X';
//             string invalidGuidString = new string(chars);
//             byte[] inputBytes = Encoding.ASCII.GetBytes(invalidGuidString);
// 
//             // Act & Assert
//             Assert.Throws<MessagePackSerializationException>(() => new GuidBits(inputBytes));
//         }
    }
}
