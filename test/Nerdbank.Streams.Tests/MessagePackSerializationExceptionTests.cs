using System;
using System.Runtime.Serialization;
using MessagePack;
using Xunit;

namespace MessagePack.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="MessagePackSerializationException"/> class.
    /// </summary>
    public class MessagePackSerializationExceptionTests
    {
        /// <summary>
        /// Tests the parameterless constructor to ensure it creates an instance with the expected default values.
        /// </summary>
        [Fact]
        public void ParameterlessConstructor_ShouldCreateInstanceWithNoInnerException()
        {
            // Arrange & Act
            var exception = new MessagePackSerializationException();

            // Assert
            Assert.NotNull(exception);
            // The default exception message can be system dependent so we check that it is not null or empty
            Assert.False(string.IsNullOrEmpty(exception.Message));
            Assert.Null(exception.InnerException);
        }

        /// <summary>
        /// Tests the constructor accepting a message to ensure it correctly sets the Message property.
        /// </summary>
        [Fact]
        public void ConstructorWithMessage_ShouldSetMessageProperty()
        {
            // Arrange
            const string expectedMessage = "Test message";

            // Act
            var exception = new MessagePackSerializationException(expectedMessage);

            // Assert
            Assert.NotNull(exception);
            Assert.Equal(expectedMessage, exception.Message);
            Assert.Null(exception.InnerException);
        }

        /// <summary>
        /// Tests the constructor accepting a message and an inner exception to ensure both properties are set correctly.
        /// </summary>
        [Fact]
        public void ConstructorWithMessageAndInnerException_ShouldSetProperties()
        {
            // Arrange
            const string expectedMessage = "Test message";
            var expectedInnerException = new Exception("Inner exception");

            // Act
            var exception = new MessagePackSerializationException(expectedMessage, expectedInnerException);

            // Assert
            Assert.NotNull(exception);
            Assert.Equal(expectedMessage, exception.Message);
            Assert.Equal(expectedInnerException, exception.InnerException);
        }

        /// <summary>
        /// Tests the protected deserialization constructor by using a derived class.
        /// It verifies that the deserialized instance correctly retrieves the message from the SerializationInfo.
        /// </summary>
        [Fact]
        public void ProtectedConstructor_WithSerializationInfo_ShouldDeserializeProperties()
        {
            // Arrange
            const string serializedMessage = "Deserialized message";
            var info = new SerializationInfo(typeof(MessagePackSerializationException), new FormatterConverter());
            // Populate the SerializationInfo; note that Exception deserialization uses "Message" key.
            info.AddValue("Message", serializedMessage);
            info.AddValue("InnerException", null, typeof(Exception));
            var context = new StreamingContext(StreamingContextStates.All);

            // Act
            var exception = new TestableMessagePackSerializationException(info, context);

            // Assert
            Assert.NotNull(exception);
            Assert.Equal(serializedMessage, exception.Message);
            Assert.Null(exception.InnerException);
        }

        /// <summary>
        /// A testable subclass to expose the protected deserialization constructor of <see cref="MessagePackSerializationException"/>.
        /// </summary>
        private class TestableMessagePackSerializationException : MessagePackSerializationException
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="TestableMessagePackSerializationException"/> class using serialization info and context.
            /// </summary>
            /// <param name="info">The SerializationInfo to use.</param>
            /// <param name="context">The StreamingContext to use.</param>
            public TestableMessagePackSerializationException(SerializationInfo info, StreamingContext context)
                : base(info, context)
            {
            }
        }
    }
}
