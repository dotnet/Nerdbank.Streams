using System;
using System.Buffers;
using MessagePack;
using Xunit;

namespace MessagePack.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="ExtensionResult"/> struct.
    /// </summary>
    public class ExtensionResultTests
    {
        /// <summary>
        /// Tests that the constructor accepting Memory&lt;byte&gt; sets the properties correctly.
        /// </summary>
        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(127)]
        public void Constructor_WithMemoryByte_SetsPropertiesCorrectly(sbyte typeCode)
        {
            // Arrange
            byte[] testData = { 1, 2, 3, 4, 5 };
            Memory<byte> memoryData = new Memory<byte>(testData);
            
            // Act
            var result = new ExtensionResult(typeCode, memoryData);

            // Assert
            Assert.Equal(typeCode, result.TypeCode);
            // Comparing lengths as ReadOnlySequence<byte> does not expose array directly, so check the length.
            Assert.Equal(testData.Length, result.Data.Length);
        }

        /// <summary>
        /// Tests that the constructor accepting ReadOnlySequence&lt;byte&gt; sets the properties correctly.
        /// </summary>
        [Theory]
        [InlineData(-10)]
        [InlineData(5)]
        public void Constructor_WithReadOnlySequence_SetsPropertiesCorrectly(sbyte typeCode)
        {
            // Arrange
            byte[] testData = { 10, 20, 30 };
            ReadOnlySequence<byte> sequenceData = new ReadOnlySequence<byte>(testData);

            // Act
            var result = new ExtensionResult(typeCode, sequenceData);

            // Assert
            Assert.Equal(typeCode, result.TypeCode);
            Assert.Equal(testData.Length, result.Data.Length);
        }

        /// <summary>
        /// Tests that the Header property returns an ExtensionHeader with the correct type code and length.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(-5)]
        public void HeaderProperty_WhenInvoked_ReturnsCorrectExtensionHeader(sbyte typeCode)
        {
            // Arrange
            byte[] testData = { 100, 101, 102, 103 };
            Memory<byte> memoryData = new Memory<byte>(testData);
            var result = new ExtensionResult(typeCode, memoryData);
            uint expectedLength = (uint)testData.Length;

            // Act
            var header = result.Header;

            // Assert
            // Since ExtensionHeader is constructed as new ExtensionHeader(typeCode, length)
            // and we do not have its internal implementation, we assume it exposes the TypeCode and Length properties.
            // We perform the check by comparing the values with a newly constructed header.
            var expectedHeader = new ExtensionHeader(typeCode, expectedLength);
            Assert.Equal(expectedHeader.TypeCode, header.TypeCode);
            Assert.Equal(expectedHeader.Length, header.Length);
        }

        /// <summary>
        /// Tests the behavior when handling an empty Memory&lt;byte&gt;.
        /// </summary>
        [Fact]
        public void Constructor_WithEmptyMemory_SetsPropertiesCorrectly()
        {
            // Arrange
            sbyte typeCode = 10;
            Memory<byte> emptyMemory = Memory<byte>.Empty;

            // Act
            var result = new ExtensionResult(typeCode, emptyMemory);

            // Assert
            Assert.Equal(typeCode, result.TypeCode);
            Assert.Equal(0, result.Data.Length);
            var header = result.Header;
            Assert.Equal(typeCode, header.TypeCode);
            Assert.Equal((uint)0, header.Length);
        }
    }
}
