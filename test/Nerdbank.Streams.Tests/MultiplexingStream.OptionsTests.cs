using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Nerdbank.Streams;
using NSubstitute;
using Xunit;

namespace Nerdbank.Streams.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="MultiplexingStream.Options"/> class.
    /// </summary>
    public class MultiplexingStreamOptionsTests
    {
        private readonly TraceSource _defaultTraceSource;

        public MultiplexingStreamOptionsTests()
        {
            _defaultTraceSource = new TraceSource(nameof(MultiplexingStream), SourceLevels.Critical);
        }

        /// <summary>
        /// Tests that the default constructor initializes SeededChannels to an empty modifiable list.
        /// </summary>
        [Fact]
        public void Constructor_Default_SeededChannelsIsNotNullAndEmpty()
        {
            // Arrange & Act
            var options = new MultiplexingStream.Options();

            // Assert
            Assert.NotNull(options.SeededChannels);
            Assert.Empty(options.SeededChannels);
        }

        /// <summary>
        /// Tests that the copy constructor throws an exception when provided with a null parameter.
        /// </summary>
        [Fact]
        public void Constructor_CopyConstructor_NullParameter_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new MultiplexingStream.Options(null));
        }

        /// <summary>
        /// Tests that the copy constructor correctly copies all properties from the source instance.
        /// </summary>
//         [Fact] [Error] (66-70)CS0029 Cannot implicitly convert type 'System.Func<Nerdbank.Streams.UnitTests.QualifiedChannelId, string, System.Diagnostics.TraceSource?>' to 'System.Func<Nerdbank.Streams.MultiplexingStream.QualifiedChannelId, string, System.Diagnostics.TraceSource?>' [Error] (69-41)CS1503 Argument 1: cannot convert from 'Nerdbank.Streams.UnitTests.ChannelOptions' to 'Nerdbank.Streams.MultiplexingStream.ChannelOptions'
//         public void Constructor_CopyConstructor_ValidCopy_CopiesAllProperties()
//         {
//             // Arrange
//             var original = new MultiplexingStream.Options();
//             original.ProtocolMajorVersion = 2;
//             original.DefaultChannelReceivingWindowSize = 100;
//             var originalTrace = new TraceSource("Original", SourceLevels.Information);
//             original.TraceSource = originalTrace;
//             original.StartSuspended = true;
//             original.FaultOpenChannelsOnStreamDisposal = true;
//             // Assign dummy delegates.
//             Func<int, string, TraceSource?> dummyFactory = (id, name) => new TraceSource(name);
//             original.DefaultChannelTraceSourceFactory = dummyFactory;
//             Func<QualifiedChannelId, string, TraceSource?> dummyFactoryWithQualifier = (qc, name) => new TraceSource(name);
//             original.DefaultChannelTraceSourceFactoryWithQualifier = dummyFactoryWithQualifier;
//             // Add a dummy seeded channel using NSubstitute.
//             var dummyChannelOptions = Substitute.For<ChannelOptions>();
//             original.SeededChannels.Add(dummyChannelOptions);
// 
//             // Act
//             var copy = new MultiplexingStream.Options(original);
// 
//             // Assert
//             Assert.Equal(original.ProtocolMajorVersion, copy.ProtocolMajorVersion);
//             Assert.Equal(original.DefaultChannelReceivingWindowSize, copy.DefaultChannelReceivingWindowSize);
//             Assert.Equal(original.TraceSource, copy.TraceSource);
//             Assert.Equal(original.StartSuspended, copy.StartSuspended);
//             Assert.Equal(original.FaultOpenChannelsOnStreamDisposal, copy.FaultOpenChannelsOnStreamDisposal);
//             Assert.Equal(original.DefaultChannelTraceSourceFactory, copy.DefaultChannelTraceSourceFactory);
//             Assert.Equal(original.DefaultChannelTraceSourceFactoryWithQualifier, copy.DefaultChannelTraceSourceFactoryWithQualifier);
//             Assert.Equal(original.SeededChannels.Count, copy.SeededChannels.Count);
//             // Ensure deep copy for SeededChannels: modifying the original list does not affect the copy.
//             original.SeededChannels.Clear();
//             Assert.NotEqual(original.SeededChannels.Count, copy.SeededChannels.Count);
//         }

        /// <summary>
        /// Tests that setting a valid DefaultChannelReceivingWindowSize succeeds.
        /// </summary>
        [Fact]
        public void DefaultChannelReceivingWindowSize_SetValidValue_Succeeds()
        {
            // Arrange
            var options = new MultiplexingStream.Options();
            long validValue = 200;

            // Act
            options.DefaultChannelReceivingWindowSize = validValue;

            // Assert
            Assert.Equal(validValue, options.DefaultChannelReceivingWindowSize);
        }

        /// <summary>
        /// Tests that setting an invalid DefaultChannelReceivingWindowSize (zero or negative) throws an exception.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(-10)]
        public void DefaultChannelReceivingWindowSize_SetInvalidValue_ThrowsArgumentOutOfRangeException(long invalidValue)
        {
            // Arrange
            var options = new MultiplexingStream.Options();

            // Act & Assert
            Assert.ThrowsAny<ArgumentOutOfRangeException>(() => options.DefaultChannelReceivingWindowSize = invalidValue);
        }

        /// <summary>
        /// Tests that setting a valid ProtocolMajorVersion succeeds.
        /// </summary>
        [Fact]
        public void ProtocolMajorVersion_SetValidValue_Succeeds()
        {
            // Arrange
            var options = new MultiplexingStream.Options();
            int validVersion = 3;

            // Act
            options.ProtocolMajorVersion = validVersion;

            // Assert
            Assert.Equal(validVersion, options.ProtocolMajorVersion);
        }

        /// <summary>
        /// Tests that setting an invalid ProtocolMajorVersion (zero or negative) throws an exception.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ProtocolMajorVersion_SetInvalidValue_ThrowsArgumentOutOfRangeException(int invalidVersion)
        {
            // Arrange
            var options = new MultiplexingStream.Options();

            // Act & Assert
            Assert.ThrowsAny<ArgumentOutOfRangeException>(() => options.ProtocolMajorVersion = invalidVersion);
        }

        /// <summary>
        /// Tests that setting a valid TraceSource succeeds.
        /// </summary>
        [Fact]
        public void TraceSource_SetValidValue_Succeeds()
        {
            // Arrange
            var options = new MultiplexingStream.Options();
            var newTraceSource = new TraceSource("TestTraceSource", SourceLevels.Warning);

            // Act
            options.TraceSource = newTraceSource;

            // Assert
            Assert.Equal(newTraceSource, options.TraceSource);
        }

        /// <summary>
        /// Tests that setting TraceSource to null throws an exception.
        /// </summary>
        [Fact]
        public void TraceSource_SetNullValue_ThrowsArgumentNullException()
        {
            // Arrange
            var options = new MultiplexingStream.Options();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => options.TraceSource = null);
        }

        /// <summary>
        /// Tests that setting a valid DefaultChannelTraceSourceFactory delegate succeeds.
        /// </summary>
        [Fact]
        public void DefaultChannelTraceSourceFactory_SetValidValue_Succeeds()
        {
            // Arrange
            var options = new MultiplexingStream.Options();
            Func<int, string, TraceSource?> factory = (id, name) => new TraceSource(name);

            // Act
            options.DefaultChannelTraceSourceFactory = factory;

            // Assert
            Assert.Equal(factory, options.DefaultChannelTraceSourceFactory);
        }

        /// <summary>
        /// Tests that setting a valid DefaultChannelTraceSourceFactoryWithQualifier delegate succeeds.
        /// </summary>
//         [Fact] [Error] (210-69)CS0029 Cannot implicitly convert type 'System.Func<Nerdbank.Streams.UnitTests.QualifiedChannelId, string, System.Diagnostics.TraceSource?>' to 'System.Func<Nerdbank.Streams.MultiplexingStream.QualifiedChannelId, string, System.Diagnostics.TraceSource?>' [Error] (213-26)CS1503 Argument 1: cannot convert from 'System.Func<Nerdbank.Streams.UnitTests.QualifiedChannelId, string, System.Diagnostics.TraceSource?>' to 'System.DateTime' [Error] (213-48)CS1503 Argument 2: cannot convert from 'System.Func<Nerdbank.Streams.MultiplexingStream.QualifiedChannelId, string, System.Diagnostics.TraceSource?>' to 'System.DateTime'
//         public void DefaultChannelTraceSourceFactoryWithQualifier_SetValidValue_Succeeds()
//         {
//             // Arrange
//             var options = new MultiplexingStream.Options();
//             Func<QualifiedChannelId, string, TraceSource?> factoryWithQualifier = (qc, name) => new TraceSource(name);
// 
//             // Act
//             options.DefaultChannelTraceSourceFactoryWithQualifier = factoryWithQualifier;
// 
//             // Assert
//             Assert.Equal(factoryWithQualifier, options.DefaultChannelTraceSourceFactoryWithQualifier);
//         }

        /// <summary>
        /// Tests that setting the StartSuspended property succeeds.
        /// </summary>
        [Fact]
        public void StartSuspended_SetValidValue_Succeeds()
        {
            // Arrange
            var options = new MultiplexingStream.Options();
            bool newValue = true;

            // Act
            options.StartSuspended = newValue;

            // Assert
            Assert.Equal(newValue, options.StartSuspended);
        }

        /// <summary>
        /// Tests that setting the FaultOpenChannelsOnStreamDisposal property succeeds.
        /// </summary>
        [Fact]
        public void FaultOpenChannelsOnStreamDisposal_SetValidValue_Succeeds()
        {
            // Arrange
            var options = new MultiplexingStream.Options();
            bool newValue = true;

            // Act
            options.FaultOpenChannelsOnStreamDisposal = newValue;

            // Assert
            Assert.Equal(newValue, options.FaultOpenChannelsOnStreamDisposal);
        }

        /// <summary>
        /// Tests that GetFrozenCopy returns a frozen copy of the instance and makes SeededChannels read-only.
        /// </summary>
        [Fact]
        public void GetFrozenCopy_NotFrozenInstance_ReturnsFrozenCopy()
        {
            // Arrange
            var options = new MultiplexingStream.Options();
            options.ProtocolMajorVersion = 2;
            options.DefaultChannelReceivingWindowSize = 150;
            options.StartSuspended = true;
            options.FaultOpenChannelsOnStreamDisposal = true;

            // Act
            var frozenCopy = options.GetFrozenCopy();

            // Assert
            Assert.True(frozenCopy.IsFrozen);
            Assert.Equal(options.ProtocolMajorVersion, frozenCopy.ProtocolMajorVersion);
            Assert.Equal(options.DefaultChannelReceivingWindowSize, frozenCopy.DefaultChannelReceivingWindowSize);
            Assert.Equal(options.StartSuspended, frozenCopy.StartSuspended);
            Assert.Equal(options.FaultOpenChannelsOnStreamDisposal, frozenCopy.FaultOpenChannelsOnStreamDisposal);
            // SeededChannels should be a ReadOnlyCollection in the frozen copy.
            Assert.IsType<ReadOnlyCollection<ChannelOptions>>(frozenCopy.SeededChannels);
        }

        /// <summary>
        /// Tests that calling GetFrozenCopy on an already frozen instance returns the same instance.
        /// </summary>
        [Fact]
        public void GetFrozenCopy_AlreadyFrozen_ReturnsSameInstance()
        {
            // Arrange
            var options = new MultiplexingStream.Options();
            var frozenCopy = options.GetFrozenCopy();

            // Act
            var result = frozenCopy.GetFrozenCopy();

            // Assert
            Assert.Same(frozenCopy, result);
        }

        /// <summary>
        /// Tests that modifying a frozen instance throws an exception.
        /// </summary>
        [Fact]
        public void FrozenInstance_Modification_ThrowsException()
        {
            // Arrange
            var options = new MultiplexingStream.Options();
            var frozenOptions = options.GetFrozenCopy();

            // Act & Assert
            Assert.Throws<Exception>(() => frozenOptions.DefaultChannelReceivingWindowSize = 300);
            Assert.Throws<Exception>(() => frozenOptions.ProtocolMajorVersion = 4);
            Assert.Throws<Exception>(() => frozenOptions.TraceSource = new TraceSource("NewSource"));
            Assert.Throws<Exception>(() => frozenOptions.DefaultChannelTraceSourceFactory = (id, name) => new TraceSource(name));
            Assert.Throws<Exception>(() => frozenOptions.DefaultChannelTraceSourceFactoryWithQualifier = (qc, name) => new TraceSource(name));
            Assert.Throws<Exception>(() => frozenOptions.StartSuspended = true);
            Assert.Throws<Exception>(() => frozenOptions.FaultOpenChannelsOnStreamDisposal = true);
        }
    }
}
