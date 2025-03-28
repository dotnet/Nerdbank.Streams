using System;
using System.Reflection;
using Nerdbank.Streams;
using Xunit;

namespace Nerdbank.Streams.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="MultiplexingStream.QualifiedChannelId"/> struct.
    /// </summary>
    public class QualifiedChannelIdTests
    {
        private readonly ulong _sampleId;
        private readonly MultiplexingStream.ChannelSource _localSource;
        private readonly MultiplexingStream.ChannelSource _remoteSource;
        private readonly MultiplexingStream.ChannelSource _seededSource;

        public QualifiedChannelIdTests()
        {
            _sampleId = 123456789;
            _localSource = MultiplexingStream.ChannelSource.Local;
            _remoteSource = MultiplexingStream.ChannelSource.Remote;
            _seededSource = MultiplexingStream.ChannelSource.Seeded;
        }

        /// <summary>
        /// Tests that the constructor initializes the properties correctly.
        /// Arrange: Provide a sample id and channel source.
        /// Act: Instantiate a QualifiedChannelId.
        /// Assert: Verify that the Id and Source properties have been set accordingly.
        /// </summary>
        [Fact]
        public void Constructor_ShouldInitializeProperties()
        {
            // Act
            var qualifiedId = new MultiplexingStream.QualifiedChannelId(_sampleId, _localSource);

            // Assert
            Assert.Equal(_sampleId, qualifiedId.Id);
            Assert.Equal(_localSource, qualifiedId.Source);
        }

        /// <summary>
        /// Tests the Equals method for two QualifiedChannelId instances with identical values.
        /// Arrange: Create two instances with the same id and source.
        /// Act: Compare using the Equals(QualifiedChannelId) method.
        /// Assert: The method should return true.
        /// </summary>
        [Fact]
        public void Equals_WithSameValues_ReturnsTrue()
        {
            // Arrange
            var qualifiedId1 = new MultiplexingStream.QualifiedChannelId(_sampleId, _localSource);
            var qualifiedId2 = new MultiplexingStream.QualifiedChannelId(_sampleId, _localSource);

            // Act
            bool result = qualifiedId1.Equals(qualifiedId2);

            // Assert
            Assert.True(result, "Expected two QualifiedChannelId instances with identical values to be equal.");
        }

        /// <summary>
        /// Tests the Equals method for two QualifiedChannelId instances with different ids.
        /// Arrange: Create two instances with different ids.
        /// Act: Compare using the Equals(QualifiedChannelId) method.
        /// Assert: The method should return false.
        /// </summary>
        [Fact]
        public void Equals_WithDifferentId_ReturnsFalse()
        {
            // Arrange
            var qualifiedId1 = new MultiplexingStream.QualifiedChannelId(_sampleId, _localSource);
            var qualifiedId2 = new MultiplexingStream.QualifiedChannelId(_sampleId + 1, _localSource);

            // Act
            bool result = qualifiedId1.Equals(qualifiedId2);

            // Assert
            Assert.False(result, "Expected QualifiedChannelId instances with different ids to not be equal.");
        }

        /// <summary>
        /// Tests the Equals method for two QualifiedChannelId instances with different sources.
        /// Arrange: Create two instances with the same id but different sources.
        /// Act: Compare using the Equals(QualifiedChannelId) method.
        /// Assert: The method should return false.
        /// </summary>
        [Fact]
        public void Equals_WithDifferentSource_ReturnsFalse()
        {
            // Arrange
            var qualifiedId1 = new MultiplexingStream.QualifiedChannelId(_sampleId, _localSource);
            var qualifiedId2 = new MultiplexingStream.QualifiedChannelId(_sampleId, _remoteSource);

            // Act
            bool result = qualifiedId1.Equals(qualifiedId2);

            // Assert
            Assert.False(result, "Expected QualifiedChannelId instances with different sources to not be equal.");
        }

        /// <summary>
        /// Tests the overridden Equals(object) method using an object of type QualifiedChannelId with identical values.
        /// Arrange: Create an instance and box an identical instance.
        /// Act: Compare using the Equals(object) method.
        /// Assert: The method should return true.
        /// </summary>
        [Fact]
        public void Equals_Object_WithSameValues_ReturnsTrue()
        {
            // Arrange
            var qualifiedId = new MultiplexingStream.QualifiedChannelId(_sampleId, _localSource);
            object equivalentObject = new MultiplexingStream.QualifiedChannelId(_sampleId, _localSource);

            // Act
            bool result = qualifiedId.Equals(equivalentObject);

            // Assert
            Assert.True(result, "Expected Equals(object) to return true for an equivalent QualifiedChannelId instance.");
        }

        /// <summary>
        /// Tests the overridden Equals(object) method when comparing with an object of a different type.
        /// Arrange: Create an instance and an object of a different type.
        /// Act: Compare using the Equals(object) method.
        /// Assert: The method should return false.
        /// </summary>
        [Fact]
        public void Equals_Object_WithDifferentType_ReturnsFalse()
        {
            // Arrange
            var qualifiedId = new MultiplexingStream.QualifiedChannelId(_sampleId, _localSource);
            object nonQualifiedId = "Invalid Type";

            // Act
            bool result = qualifiedId.Equals(nonQualifiedId);

            // Assert
            Assert.False(result, "Expected Equals(object) to return false when comparing with an object of a different type.");
        }

        /// <summary>
        /// Tests that two equal QualifiedChannelId instances return the same hash code.
        /// Arrange: Create two instances with identical values.
        /// Act: Retrieve their hash codes.
        /// Assert: Both hash codes should be equal.
        /// </summary>
        [Fact]
        public void GetHashCode_EqualObjects_SameHashCode()
        {
            // Arrange
            var qualifiedId1 = new MultiplexingStream.QualifiedChannelId(_sampleId, _localSource);
            var qualifiedId2 = new MultiplexingStream.QualifiedChannelId(_sampleId, _localSource);

            // Act
            int hash1 = qualifiedId1.GetHashCode();
            int hash2 = qualifiedId2.GetHashCode();

            // Assert
            Assert.Equal(hash1, hash2);
        }

        /// <summary>
        /// Tests the ToString method to ensure it returns the expected string format.
        /// Arrange: Create an instance with a known id and source.
        /// Act: Call the ToString method.
        /// Assert: The returned string matches the "id (source)" format.
        /// </summary>
        [Fact]
        public void ToString_ReturnsCorrectFormat()
        {
            // Arrange
            ulong id = 100;
            var source = _remoteSource;
            var qualifiedId = new MultiplexingStream.QualifiedChannelId(id, source);
            string expectedFormat = $"{id} ({source})";

            // Act
            string result = qualifiedId.ToString();

            // Assert
            Assert.Equal(expectedFormat, result);
        }

        /// <summary>
        /// Tests that the internal DebuggerDisplay property returns the same string as the ToString method.
        /// Arrange: Create an instance with known values.
        /// Act: Retrieve the DebuggerDisplay value using reflection.
        /// Assert: The DebuggerDisplay value matches the output of ToString.
        /// </summary>
        [Fact]
        public void DebuggerDisplay_ReturnsSameAsToString()
        {
            // Arrange
            var qualifiedId = new MultiplexingStream.QualifiedChannelId(200, _seededSource);
            string expected = qualifiedId.ToString();

            // Act
            PropertyInfo debuggerDisplayProperty = typeof(MultiplexingStream.QualifiedChannelId)
                .GetProperty("DebuggerDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
            string actual = debuggerDisplayProperty?.GetValue(qualifiedId)?.ToString();

            // Assert
            Assert.Equal(expected, actual);
        }
    }
}
