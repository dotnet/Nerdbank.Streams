using MessagePack;
using System;
using Xunit;

namespace MessagePack.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="ExtensionHeader"/> struct.
    /// </summary>
    public class ExtensionHeaderTests
    {
        /// <summary>
        /// Tests that the constructor ExtensionHeader(sbyte, uint) correctly sets the TypeCode and Length properties.
        /// </summary>
        [Fact]
        public void Ctor_WithUintLength_SetsProperties()
        {
            // Arrange
            sbyte typeCode = 5;
            uint length = 100;

            // Act
            var header = new ExtensionHeader(typeCode, length);

            // Assert
            Assert.Equal(typeCode, header.TypeCode);
            Assert.Equal(length, header.Length);
        }

        /// <summary>
        /// Tests that the constructor ExtensionHeader(sbyte, int) correctly sets the TypeCode and Length properties when given a positive integer.
        /// </summary>
        [Fact]
        public void Ctor_WithIntLength_PositiveValue_SetsProperties()
        {
            // Arrange
            sbyte typeCode = -10;
            int length = 200;

            // Act
            var header = new ExtensionHeader(typeCode, length);

            // Assert
            Assert.Equal(typeCode, header.TypeCode);
            Assert.Equal((uint)length, header.Length);
        }

        /// <summary>
        /// Tests that the constructor ExtensionHeader(sbyte, int) correctly handles a negative integer by converting it to uint.
        /// This edge case demonstrates the conversion of a negative integer to its corresponding unsigned value.
        /// </summary>
        [Fact]
        public void Ctor_WithIntLength_NegativeValue_ConvertsToUint()
        {
            // Arrange
            sbyte typeCode = 1;
            int negativeLength = -1;
            uint expectedLength = unchecked((uint)negativeLength);

            // Act
            var header = new ExtensionHeader(typeCode, negativeLength);

            // Assert
            Assert.Equal(typeCode, header.TypeCode);
            Assert.Equal(expectedLength, header.Length);
        }

        /// <summary>
        /// Tests that the Equals method returns true when comparing two ExtensionHeader instances with identical TypeCode and Length.
        /// </summary>
        [Fact]
        public void Equals_SameValues_ReturnsTrue()
        {
            // Arrange
            var header1 = new ExtensionHeader(10, 500);
            var header2 = new ExtensionHeader(10, 500);

            // Act
            bool areEqual = header1.Equals(header2);

            // Assert
            Assert.True(areEqual, "Equals should return true for headers with identical values.");
        }

        /// <summary>
        /// Tests that the Equals method returns false when comparing two ExtensionHeader instances with different TypeCode values.
        /// </summary>
        [Fact]
        public void Equals_DifferentTypeCode_ReturnsFalse()
        {
            // Arrange
            var header1 = new ExtensionHeader(10, 500);
            var header2 = new ExtensionHeader(11, 500);

            // Act
            bool areEqual = header1.Equals(header2);

            // Assert
            Assert.False(areEqual, "Equals should return false for headers with different TypeCode values.");
        }

        /// <summary>
        /// Tests that the Equals method returns false when comparing two ExtensionHeader instances with different Length values.
        /// </summary>
        [Fact]
        public void Equals_DifferentLength_ReturnsFalse()
        {
            // Arrange
            var header1 = new ExtensionHeader(10, 500);
            var header2 = new ExtensionHeader(10, 600);

            // Act
            bool areEqual = header1.Equals(header2);

            // Assert
            Assert.False(areEqual, "Equals should return false for headers with different Length values.");
        }
    }
}
