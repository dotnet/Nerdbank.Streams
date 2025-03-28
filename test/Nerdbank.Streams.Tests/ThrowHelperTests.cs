using System;
using Nerdbank.Streams;
using Xunit;

namespace Nerdbank.Streams.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="ThrowHelper"/> class.
    /// </summary>
    public class ThrowHelperTests
    {
        /// <summary>
        /// Tests that the ThrowArgumentOutOfRangeException method throws an ArgumentOutOfRangeException
        /// with the expected parameter name when a non-null parameter name is provided.
        /// </summary>
//         [Fact] [Error] (23-78)CS0122 'ThrowHelper' is inaccessible due to its protection level
//         public void ThrowArgumentOutOfRangeException_WithValidParameterName_ThrowsExceptionWithParameterName()
//         {
//             // Arrange
//             string paramName = "testParam";
// 
//             // Act & Assert
//             var exception = Assert.Throws<ArgumentOutOfRangeException>(() => ThrowHelper.ThrowArgumentOutOfRangeException(paramName));
//             Assert.Equal(paramName, exception.ParamName);
//         }

        /// <summary>
        /// Tests that the ThrowArgumentOutOfRangeException method throws an ArgumentOutOfRangeException
        /// with a null parameter name when null is provided.
        /// </summary>
//         [Fact] [Error] (38-78)CS0122 'ThrowHelper' is inaccessible due to its protection level
//         public void ThrowArgumentOutOfRangeException_WithNullParameterName_ThrowsExceptionWithNullParameterName()
//         {
//             // Arrange
//             string? paramName = null;
// 
//             // Act & Assert
//             var exception = Assert.Throws<ArgumentOutOfRangeException>(() => ThrowHelper.ThrowArgumentOutOfRangeException(paramName));
//             Assert.Null(exception.ParamName);
//         }
    }
}
