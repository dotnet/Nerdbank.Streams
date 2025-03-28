using System;
using System.Text;
using MessagePack;
using Xunit;

namespace MessagePack.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="StringEncoding"/> class.
    /// </summary>
    public class StringEncodingTests
    {
        /// <summary>
        /// Tests that the UTF8 field returns a valid UTF8Encoding instance configured to not emit a byte order mark.
        /// </summary>
//         [Fact] [Error] (20-33)CS0122 'StringEncoding' is inaccessible due to its protection level
//         public void UTF8_Field_ReturnsValidEncoding()
//         {
//             // Arrange & Act
//             Encoding encoding = StringEncoding.UTF8;
// 
//             // Assert
//             Assert.NotNull(encoding);
//             Assert.IsType<UTF8Encoding>(encoding);
//             
//             // Check that the encoding does not emit BOM.
//             byte[] preamble = encoding.GetPreamble();
//             Assert.Empty(preamble);
//         }

#if !NETCOREAPP
        /// <summary>
        /// Tests the GetString extension method to ensure it returns an empty string when provided with an empty byte span.
        /// </summary>
        [Fact]
        public void GetString_WhenEmptySpan_ReturnsEmptyString()
        {
            // Arrange
            Encoding encoding = StringEncoding.UTF8;
            ReadOnlySpan<byte> emptySpan = ReadOnlySpan<byte>.Empty;

            // Act
            string result = encoding.GetString(emptySpan);

            // Assert
            Assert.Equal(string.Empty, result);
        }

        /// <summary>
        /// Tests the GetString extension method using a valid UTF8 byte span to verify it decodes correctly.
        /// </summary>
        [Fact]
        public void GetString_WhenValidUtf8Bytes_ReturnsExpectedString()
        {
            // Arrange
            Encoding encoding = StringEncoding.UTF8;
            string expected = "Hello, World!";
            byte[] encodedBytes = encoding.GetBytes(expected);
            ReadOnlySpan<byte> byteSpan = new ReadOnlySpan<byte>(encodedBytes);

            // Act
            string result = encoding.GetString(byteSpan);

            // Assert
            Assert.Equal(expected, result);
        }

        /// <summary>
        /// Tests the GetString extension method to check proper decoding when provided with a subset of a byte array.
        /// </summary>
        [Fact]
        public void GetString_WhenPartialSpan_ReturnsExpectedSubstring()
        {
            // Arrange
            Encoding encoding = StringEncoding.UTF8;
            string fullText = "Hello, World!";
            string expectedSubstring = "World";
            byte[] fullBytes = encoding.GetBytes(fullText);
            int start = fullText.IndexOf(expectedSubstring, StringComparison.Ordinal);
            int length = expectedSubstring.Length;
            ReadOnlySpan<byte> partialSpan = new ReadOnlySpan<byte>(fullBytes, start, length);

            // Act
            string result = encoding.GetString(partialSpan);

            // Assert
            Assert.Equal(expectedSubstring, result);
        }
#endif
    }
}
