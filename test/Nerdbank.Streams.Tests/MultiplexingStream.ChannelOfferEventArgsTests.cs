using System;
using Nerdbank.Streams;
using Xunit;

namespace Nerdbank.Streams.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="MultiplexingStream.ChannelOfferEventArgs"/> class.
    /// </summary>
    public class ChannelOfferEventArgsTests
    {
        /// <summary>
        /// Tests the constructor of <see cref="MultiplexingStream.ChannelOfferEventArgs"/>
        /// to ensure that all properties are initialized correctly with valid parameters.
        /// </summary>
//         [Fact] [Error] (23-35)CS7036 There is no argument given that corresponds to the required parameter 'source' of 'QualifiedChannelId.QualifiedChannelId(long, ChannelSource)' [Error] (26-33)CS1729 'MultiplexingStream.ChannelOfferEventArgs' does not contain a constructor that takes 3 arguments [Error] (29-26)CS1503 Argument 1: cannot convert from 'Nerdbank.Streams.UnitTests.QualifiedChannelId' to 'System.DateTime' [Error] (29-39)CS1503 Argument 2: cannot convert from 'Nerdbank.Streams.MultiplexingStream.QualifiedChannelId' to 'System.DateTime'
//         public void Constructor_ValidParameters_InitializesProperties()
//         {
//             // Arrange
//             var expectedIdValue = 1L;
//             var expectedName = "TestChannel";
//             var expectedAccepted = false;
//             var qualifiedId = new QualifiedChannelId(expectedIdValue);
// 
//             // Act
//             var eventArgs = new MultiplexingStream.ChannelOfferEventArgs(qualifiedId, expectedName, expectedAccepted);
// 
//             // Assert
//             Assert.Equal(qualifiedId, eventArgs.QualifiedId);
//             Assert.Equal(expectedName, eventArgs.Name);
//             Assert.Equal(expectedAccepted, eventArgs.IsAccepted);
//             Assert.Equal((int)expectedIdValue, eventArgs.Id);
//         }

        /// <summary>
        /// Tests the constructor of <see cref="MultiplexingStream.ChannelOfferEventArgs"/>
        /// when the channel name is null. Verifies that the Name property is set to null.
        /// </summary>
//         [Fact] [Error] (46-35)CS7036 There is no argument given that corresponds to the required parameter 'source' of 'QualifiedChannelId.QualifiedChannelId(long, ChannelSource)' [Error] (49-33)CS1729 'MultiplexingStream.ChannelOfferEventArgs' does not contain a constructor that takes 3 arguments [Error] (52-26)CS1503 Argument 1: cannot convert from 'Nerdbank.Streams.UnitTests.QualifiedChannelId' to 'System.DateTime' [Error] (52-39)CS1503 Argument 2: cannot convert from 'Nerdbank.Streams.MultiplexingStream.QualifiedChannelId' to 'System.DateTime'
//         public void Constructor_NullName_InitializesProperties()
//         {
//             // Arrange
//             var expectedIdValue = 2L;
//             string expectedName = null;
//             var expectedAccepted = true;
//             var qualifiedId = new QualifiedChannelId(expectedIdValue);
// 
//             // Act
//             var eventArgs = new MultiplexingStream.ChannelOfferEventArgs(qualifiedId, expectedName, expectedAccepted);
// 
//             // Assert
//             Assert.Equal(qualifiedId, eventArgs.QualifiedId);
//             Assert.Null(eventArgs.Name);
//             Assert.Equal(expectedAccepted, eventArgs.IsAccepted);
//             Assert.Equal((int)expectedIdValue, eventArgs.Id);
//         }

        /// <summary>
        /// Tests that the <see cref="MultiplexingStream.ChannelOfferEventArgs.Id"/> property
        /// throws an <see cref="OverflowException"/> when the underlying QualifiedId.Id value exceeds the range of an Int32.
        /// </summary>
//         [Fact] [Error] (67-35)CS7036 There is no argument given that corresponds to the required parameter 'source' of 'QualifiedChannelId.QualifiedChannelId(long, ChannelSource)' [Error] (68-33)CS1729 'MultiplexingStream.ChannelOfferEventArgs' does not contain a constructor that takes 3 arguments
//         public void Id_ValueExceedsIntRange_ThrowsOverflowException()
//         {
//             // Arrange
//             var overflowId = (long)int.MaxValue + 1;
//             var qualifiedId = new QualifiedChannelId(overflowId);
//             var eventArgs = new MultiplexingStream.ChannelOfferEventArgs(qualifiedId, "OverflowChannel", false);
// 
//             // Act & Assert
//             Assert.Throws<OverflowException>(() =>
//             {
//                 var id = eventArgs.Id;
//             });
//         }
    }
}
