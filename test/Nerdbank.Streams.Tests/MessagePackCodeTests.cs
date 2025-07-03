using System;
using MessagePack;
using Xunit;

namespace MessagePack.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="MessagePackCode"/> class.
    /// </summary>
    public class MessagePackCodeTests
    {
        /// <summary>
        /// Tests that MessagePackCode.ToMessagePackType returns the expected MessagePackType for given byte codes.
        /// </summary>
        /// <param name="code">The byte code to test.</param>
        /// <param name="expectedType">The expected MessagePackType.</param>
        [Theory]
        [InlineData((byte)0, MessagePackType.Integer)]
        [InlineData((byte)127, MessagePackType.Integer)]
        [InlineData((byte)128, MessagePackType.Map)]
        [InlineData((byte)143, MessagePackType.Map)]
        [InlineData((byte)144, MessagePackType.Array)]
        [InlineData((byte)159, MessagePackType.Array)]
        [InlineData((byte)160, MessagePackType.String)]
        [InlineData((byte)191, MessagePackType.String)]
        [InlineData((byte)192, MessagePackType.Nil)]
        [InlineData((byte)193, MessagePackType.Unknown)]
        [InlineData((byte)194, MessagePackType.Boolean)]
        [InlineData((byte)195, MessagePackType.Boolean)]
        [InlineData((byte)208, MessagePackType.Integer)]
        [InlineData((byte)209, MessagePackType.Integer)]
        [InlineData((byte)210, MessagePackType.Integer)]
        [InlineData((byte)211, MessagePackType.Integer)]
        [InlineData((byte)224, MessagePackType.Integer)]
        [InlineData((byte)255, MessagePackType.Integer)]
        [InlineData((byte)217, MessagePackType.String)] // Str8
        [InlineData((byte)218, MessagePackType.String)] // Str16
        [InlineData((byte)219, MessagePackType.String)] // Str32
        [InlineData((byte)220, MessagePackType.Array)]  // Array16
        [InlineData((byte)221, MessagePackType.Array)]  // Array32
        [InlineData((byte)222, MessagePackType.Map)]    // Map16
        [InlineData((byte)223, MessagePackType.Map)]    // Map32
        [InlineData((byte)196, MessagePackType.Binary)] // Bin8
        [InlineData((byte)197, MessagePackType.Binary)] // Bin16
        [InlineData((byte)198, MessagePackType.Binary)] // Bin32
        [InlineData((byte)199, MessagePackType.Extension)] // Ext8
        [InlineData((byte)200, MessagePackType.Extension)] // Ext16
        [InlineData((byte)201, MessagePackType.Extension)] // Ext32
        public void ToMessagePackType_ValidCode_ReturnsExpectedMessagePackType(byte code, MessagePackType expectedType)
        {
            // Act
            MessagePackType result = MessagePackCode.ToMessagePackType(code);

            // Assert
            Assert.Equal(expectedType, result);
        }

        /// <summary>
        /// Tests that MessagePackCode.ToFormatName returns the expected format name for given byte codes.
        /// </summary>
        /// <param name="code">The byte code to test.</param>
        /// <param name="expectedFormatName">The expected format name.</param>
        [Theory]
        [InlineData((byte)0, "positive fixint")]
        [InlineData((byte)127, "positive fixint")]
        [InlineData((byte)128, "fixmap")]
        [InlineData((byte)143, "fixmap")]
        [InlineData((byte)144, "fixarray")]
        [InlineData((byte)159, "fixarray")]
        [InlineData((byte)160, "fixstr")]
        [InlineData((byte)191, "fixstr")]
        [InlineData((byte)192, "nil")]
        [InlineData((byte)193, "(never used)")]
        [InlineData((byte)194, "false")]
        [InlineData((byte)195, "true")]
        [InlineData((byte)208, "int 8")]
        [InlineData((byte)209, "int 16")]
        [InlineData((byte)210, "int 32")]
        [InlineData((byte)211, "int 64")]
        [InlineData((byte)224, "negative fixint")]
        [InlineData((byte)255, "negative fixint")]
        [InlineData((byte)217, "str 8")]
        [InlineData((byte)218, "str 16")]
        [InlineData((byte)219, "str 32")]
        [InlineData((byte)220, "array 16")]
        [InlineData((byte)221, "array 32")]
        [InlineData((byte)222, "map 16")]
        [InlineData((byte)223, "map 32")]
        [InlineData((byte)196, "bin 8")]
        [InlineData((byte)197, "bin 16")]
        [InlineData((byte)198, "bin 32")]
        [InlineData((byte)199, "ext 8")]
        [InlineData((byte)200, "ext 16")]
        [InlineData((byte)201, "ext 32")]
        [InlineData((byte)202, "float 32")]
        [InlineData((byte)203, "float 64")]
        [InlineData((byte)204, "uint 8")]
        [InlineData((byte)205, "uint 16")]
        [InlineData((byte)206, "uint 32")]
        [InlineData((byte)207, "uint 64")]
        [InlineData((byte)212, "fixext 1")]
        [InlineData((byte)213, "fixext 2")]
        [InlineData((byte)214, "fixext 4")]
        [InlineData((byte)215, "fixext 8")]
        [InlineData((byte)216, "fixext 16")]
        public void ToFormatName_ValidCode_ReturnsExpectedFormatName(byte code, string expectedFormatName)
        {
            // Act
            string result = MessagePackCode.ToFormatName(code);

            // Assert
            Assert.Equal(expectedFormatName, result);
        }

        /// <summary>
        /// Tests that MessagePackCode.IsSignedInteger returns true for codes representing signed integers and false for others.
        /// </summary>
        /// <param name="code">The byte code to test.</param>
        /// <param name="expectedResult">The expected boolean result indicating if the code represents a signed integer.</param>
//         [Theory] [Error] (135-43)CS0117 'MessagePackCode' does not contain a definition for 'IsSignedInteger'
//         [InlineData((byte)208, true)]  // Int8
//         [InlineData((byte)209, true)]  // Int16
//         [InlineData((byte)210, true)]  // Int32
//         [InlineData((byte)211, true)]  // Int64
//         [InlineData((byte)225, true)]  // Negative fixint (>=224 and <=255)
//         [InlineData((byte)255, true)]  // Negative fixint
//         [InlineData((byte)127, false)] // Positive fixint not considered signed
//         [InlineData((byte)192, false)] // Nil
//         [InlineData((byte)217, false)] // str 8
//         [InlineData((byte)0, false)]   // Positive fixint
//         [InlineData((byte)223, false)] // map32 falls outside signed integer range
//         public void IsSignedInteger_VariousCodes_ReturnsExpectedResult(byte code, bool expectedResult)
//         {
//             // Act
//             bool result = MessagePackCode.IsSignedInteger(code);
// 
//             // Assert
//             Assert.Equal(expectedResult, result);
//         }
    }
}
