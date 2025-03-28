using System;
using MessagePack;
using Xunit;

namespace MessagePack.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="Nil"/> struct.
    /// </summary>
    public class NilTests
    {
        private readonly Nil _nilInstance;

        /// <summary>
        /// Initializes a new instance of the <see cref="NilTests"/> class.
        /// </summary>
        public NilTests()
        {
            _nilInstance = Nil.Default;
        }

        /// <summary>
        /// Tests that the Equals(object) method returns true when passed an instance of Nil.
        /// </summary>
        [Fact]
        public void Equals_Object_WithNilInstance_ReturnsTrue()
        {
            // Arrange
            object otherNil = new Nil();

            // Act
            bool result = _nilInstance.Equals(otherNil);

            // Assert
            Assert.True(result);
        }

        /// <summary>
        /// Tests that the Equals(object) method returns false when passed an object of a different type.
        /// </summary>
        [Fact]
        public void Equals_Object_WithDifferentType_ReturnsFalse()
        {
            // Arrange
            object nonNilObject = new object();

            // Act
            bool result = _nilInstance.Equals(nonNilObject);

            // Assert
            Assert.False(result);
        }

        /// <summary>
        /// Tests that the Equals(object) method returns false when passed a null value.
        /// </summary>
        [Fact]
        public void Equals_Object_WithNull_ReturnsFalse()
        {
            // Arrange
            object nullObject = null;

            // Act
            bool result = _nilInstance.Equals(nullObject);

            // Assert
            Assert.False(result);
        }

        /// <summary>
        /// Tests that the Equals(Nil) method always returns true irrespective of the instance values.
        /// </summary>
        [Fact]
        public void Equals_Nil_AlwaysReturnsTrue()
        {
            // Arrange
            Nil otherNil = new Nil();

            // Act
            bool result = _nilInstance.Equals(otherNil);

            // Assert
            Assert.True(result);
        }

        /// <summary>
        /// Tests that the GetHashCode method always returns 0.
        /// </summary>
        [Fact]
        public void GetHashCode_AlwaysReturnsZero()
        {
            // Act
            int hashCode = _nilInstance.GetHashCode();

            // Assert
            Assert.Equal(0, hashCode);
        }

        /// <summary>
        /// Tests that the ToString method returns the string representation "()".
        /// </summary>
        [Fact]
        public void ToString_ReturnsParentheses()
        {
            // Act
            string result = _nilInstance.ToString();

            // Assert
            Assert.Equal("()", result);
        }
    }
}
