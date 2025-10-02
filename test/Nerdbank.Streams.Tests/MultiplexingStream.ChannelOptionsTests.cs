using NSubstitute;
using Nerdbank.Streams;
using System;
using System.Diagnostics;
using System.IO.Pipelines;
using Xunit;

namespace Nerdbank.Streams.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="MultiplexingStream.ChannelOptions"/> class.
    /// </summary>
    public class MultiplexingStreamChannelOptionsTests
    {
        /// <summary>
        /// Tests that the constructor initializes all properties to their default values.
        /// Expected: All properties (TraceSource, ExistingPipe, InputPipeOptions, ChannelReceivingWindowSize) are null.
        /// </summary>
        [Fact]
        public void Constructor_DefaultProperties_AreNull()
        {
            // Arrange & Act
            var options = new MultiplexingStream.ChannelOptions();

            // Assert
            Assert.Null(options.TraceSource);
            Assert.Null(options.ExistingPipe);
            Assert.Null(options.InputPipeOptions);
            Assert.Null(options.ChannelReceivingWindowSize);
        }

        /// <summary>
        /// Tests that the TraceSource property can be set and retrieved correctly.
        /// Expected: The set TraceSource is returned.
        /// </summary>
        [Fact]
        public void TraceSource_SetAndGet_ReturnsSameInstance()
        {
            // Arrange
            var options = new MultiplexingStream.ChannelOptions();
            var traceSource = new TraceSource("TestSource");

            // Act
            options.TraceSource = traceSource;

            // Assert
            Assert.Equal(traceSource, options.TraceSource);
        }

        /// <summary>
        /// Tests that setting the ExistingPipe property to null works as expected.
        /// Expected: ExistingPipe property remains null.
        /// </summary>
        [Fact]
        public void ExistingPipe_SetToNull_StoresNull()
        {
            // Arrange
            var options = new MultiplexingStream.ChannelOptions();

            // Act
            options.ExistingPipe = null;

            // Assert
            Assert.Null(options.ExistingPipe);
        }

        /// <summary>
        /// Tests that setting the ExistingPipe property to an invalid pipe (both Input and Output are null) throws an ArgumentException.
        /// Expected: An ArgumentException with a specific message is thrown.
        /// </summary>
        [Fact]
        public void ExistingPipe_SetToInvalidPipe_ThrowsArgumentException()
        {
            // Arrange
            var options = new MultiplexingStream.ChannelOptions();
            var invalidPipe = Substitute.For<IDuplexPipe>();
            // Both Input and Output return null.
            invalidPipe.Input.Returns((PipeReader)null);
            invalidPipe.Output.Returns((PipeWriter)null);

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => options.ExistingPipe = invalidPipe);
            Assert.Equal("At least a reader or writer must be specified.", exception.Message);
        }

        /// <summary>
        /// Tests that setting the ExistingPipe property to a valid non-DuplexPipe instance with only Input non-null wraps the pipe and preserves the Input.
        /// Expected: The ExistingPipe property returns an instance of DuplexPipe wrapping the provided Input.
        /// </summary>
        [Fact]
        public void ExistingPipe_SetValidPipeWithOnlyInputNonNull_WrapsPipeAndPreservesInput()
        {
            // Arrange
            var options = new MultiplexingStream.ChannelOptions();
            var dummyInput = Substitute.For<PipeReader>();
            var dummyPipe = Substitute.For<IDuplexPipe>();
            dummyPipe.Input.Returns(dummyInput);
            // Set Output to null (allowed because at least one is non-null).
            dummyPipe.Output.Returns((PipeWriter)null);

            // Act
            options.ExistingPipe = dummyPipe;
            var result = options.ExistingPipe;

            // Assert
            Assert.NotNull(result);
            // Verify that the returned instance is a wrapped instance (of type DuplexPipe) and not the same as dummyPipe.
            Assert.NotEqual(dummyPipe, result);
            // Using reflection to get the type name since DuplexPipe might be internal.
            Assert.Equal("DuplexPipe", result.GetType().Name);
            Assert.Equal(dummyInput, result.Input);
            Assert.Null(result.Output);
        }

        /// <summary>
        /// Tests that setting the ExistingPipe property to a valid non-DuplexPipe instance with only Output non-null wraps the pipe and preserves the Output.
        /// Expected: The ExistingPipe property returns an instance of DuplexPipe wrapping the provided Output.
        /// </summary>
        [Fact]
        public void ExistingPipe_SetValidPipeWithOnlyOutputNonNull_WrapsPipeAndPreservesOutput()
        {
            // Arrange
            var options = new MultiplexingStream.ChannelOptions();
            var dummyOutput = Substitute.For<PipeWriter>();
            var dummyPipe = Substitute.For<IDuplexPipe>();
            // Set Input to null and Output to non-null.
            dummyPipe.Input.Returns((PipeReader)null);
            dummyPipe.Output.Returns(dummyOutput);

            // Act
            options.ExistingPipe = dummyPipe;
            var result = options.ExistingPipe;

            // Assert
            Assert.NotNull(result);
            Assert.NotEqual(dummyPipe, result);
            Assert.Equal("DuplexPipe", result.GetType().Name);
            Assert.Null(result.Input);
            Assert.Equal(dummyOutput, result.Output);
        }

        /// <summary>
        /// Tests that setting the ExistingPipe property to an instance of DuplexPipe returns the same instance.
        /// Expected: The ExistingPipe property returns the same DuplexPipe instance that was provided.
        /// </summary>
        [Fact]
        public void ExistingPipe_SetValidPipeThatIsAlreadyDuplexPipe_ReturnsSameInstance()
        {
            // Arrange
            var options = new MultiplexingStream.ChannelOptions();
            // Create dummy non-null PipeReader and PipeWriter
            var dummyInput = Substitute.For<PipeReader>();
            var dummyOutput = Substitute.For<PipeWriter>();

            // Instantiate DuplexPipe directly. We assume DuplexPipe has a public constructor.
            var duplexPipe = new DuplexPipe(dummyInput, dummyOutput);

            // Act
            options.ExistingPipe = duplexPipe;
            var result = options.ExistingPipe;

            // Assert
            Assert.NotNull(result);
            Assert.Same(duplexPipe, result);
        }

        /// <summary>
        /// Tests that the InputPipeOptions property can be set and retrieved correctly.
        /// Expected: The set value is returned.
        /// </summary>
        [Fact]
        public void InputPipeOptions_SetAndGet_ReturnsSameValue()
        {
            // Arrange
            var options = new MultiplexingStream.ChannelOptions();
            // For testing purposes, we simply instantiate a new PipeOptions if possible.
            // As PipeOptions may require parameters, here we test setting to null and then to a non-null value.
            Assert.Null(options.InputPipeOptions);

            // Act
            // Since constructing a real PipeOptions might be non-trivial, we create a simple instance.
            var pipeOptions = new PipeOptions();
            options.InputPipeOptions = pipeOptions;

            // Assert
            Assert.Equal(pipeOptions, options.InputPipeOptions);
        }

        /// <summary>
        /// Tests that the ChannelReceivingWindowSize property can be set and retrieved correctly.
        /// Expected: The set numeric value is returned.
        /// </summary>
        [Fact]
        public void ChannelReceivingWindowSize_SetAndGet_ReturnsSameValue()
        {
            // Arrange
            var options = new MultiplexingStream.ChannelOptions();
            Assert.Null(options.ChannelReceivingWindowSize);

            // Act
            long testValue = 1024;
            options.ChannelReceivingWindowSize = testValue;

            // Assert
            Assert.Equal(testValue, options.ChannelReceivingWindowSize);
        }
    }
}
