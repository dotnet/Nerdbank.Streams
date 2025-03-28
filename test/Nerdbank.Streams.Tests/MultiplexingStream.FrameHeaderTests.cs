using System;
using Nerdbank.Streams;
using Xunit;

namespace Nerdbank.Streams.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="MultiplexingStream.FrameHeader"/> struct.
    /// </summary>
    public class MultiplexingStream_FrameHeaderTests
    {
        /// <summary>
        /// Tests that the RequiredChannelId property returns the set ChannelId.
        /// </summary>
//         [Fact] [Error] (22-49)CS0122 'MultiplexingStream.FrameHeader' is inaccessible due to its protection level [Error] (28-48)CS0122 'MultiplexingStream.FrameHeader.RequiredChannelId' is inaccessible due to its protection level
//         public void RequiredChannelId_ChannelIdSet_ReturnsChannelId()
//         {
//             // Arrange
//             var expectedId = 42;
//             var expectedSource = (ChannelSource)1;
//             var expectedQualifiedChannelId = new QualifiedChannelId(expectedId, expectedSource);
//             var header = new MultiplexingStream.FrameHeader
//             {
//                 ChannelId = expectedQualifiedChannelId
//             };
// 
//             // Act
//             QualifiedChannelId actual = header.RequiredChannelId;
// 
//             // Assert
//             Assert.Equal(expectedQualifiedChannelId, actual);
//         }

        /// <summary>
        /// Tests that the RequiredChannelId property throws a MultiplexingProtocolException when ChannelId is null.
        /// </summary>
//         [Fact] [Error] (41-49)CS0122 'MultiplexingStream.FrameHeader' is inaccessible due to its protection level [Error] (49-32)CS0122 'MultiplexingStream.FrameHeader.RequiredChannelId' is inaccessible due to its protection level
//         public void RequiredChannelId_ChannelIdNull_ThrowsMultiplexingProtocolException()
//         {
//             // Arrange
//             var header = new MultiplexingStream.FrameHeader
//             {
//                 ChannelId = null
//             };
// 
//             // Act & Assert
//             MultiplexingProtocolException exception = Assert.Throws<MultiplexingProtocolException>(() =>
//             {
//                 var _ = header.RequiredChannelId;
//             });
//             Assert.Equal("Expected ChannelId not present in frame header.", exception.Message);
//         }

        /// <summary>
        /// Tests that the FlipChannelPerspective method correctly flips the ChannelSource value when ChannelId is not null.
        /// </summary>
//         [Fact] [Error] (64-49)CS0122 'MultiplexingStream.FrameHeader' is inaccessible due to its protection level [Error] (70-20)CS0122 'MultiplexingStream.FrameHeader.FlipChannelPerspective()' is inaccessible due to its protection level [Error] (73-32)CS0122 'MultiplexingStream.FrameHeader.ChannelId' is inaccessible due to its protection level [Error] (74-45)CS0122 'MultiplexingStream.FrameHeader.ChannelId' is inaccessible due to its protection level [Error] (75-73)CS0122 'MultiplexingStream.FrameHeader.ChannelId' is inaccessible due to its protection level
//         public void FlipChannelPerspective_ChannelIdSet_FlipsChannelSource()
//         {
//             // Arrange
//             int expectedId = 100;
//             // Set initial source to a positive value (e.g., 3), expecting flipped value to be -3.
//             var initialSource = (ChannelSource)3;
//             var header = new MultiplexingStream.FrameHeader
//             {
//                 ChannelId = new QualifiedChannelId(expectedId, initialSource)
//             };
// 
//             // Act
//             header.FlipChannelPerspective();
// 
//             // Assert
//             Assert.True(header.ChannelId.HasValue);
//             Assert.Equal(expectedId, header.ChannelId.Value.Id);
//             Assert.Equal((ChannelSource)(-((int)initialSource)), header.ChannelId.Value.Source);
//         }

        /// <summary>
        /// Tests that the FlipChannelPerspective method does nothing when ChannelId is null.
        /// </summary>
//         [Fact] [Error] (85-49)CS0122 'MultiplexingStream.FrameHeader' is inaccessible due to its protection level [Error] (91-20)CS0122 'MultiplexingStream.FrameHeader.FlipChannelPerspective()' is inaccessible due to its protection level [Error] (94-32)CS0122 'MultiplexingStream.FrameHeader.ChannelId' is inaccessible due to its protection level
//         public void FlipChannelPerspective_ChannelIdNull_DoesNothing()
//         {
//             // Arrange
//             var header = new MultiplexingStream.FrameHeader
//             {
//                 ChannelId = null
//             };
// 
//             // Act
//             header.FlipChannelPerspective();
// 
//             // Assert
//             Assert.Null(header.ChannelId);
//         }
    }
}
