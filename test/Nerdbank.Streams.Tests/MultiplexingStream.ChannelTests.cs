// using System;
// using System.Buffers;
// using System.Diagnostics;
// using System.IO.Pipelines;
// using System.Threading;
// using System.Threading.Tasks;
// using Nerdbank.Streams;
// using NSubstitute;
// using Xunit;
// 
// namespace Nerdbank.Streams.UnitTests
// {
//     // Minimal implementations required for testing
// 
//     internal enum ChannelSource
//     {
//         Local,
//         Remote,
//         Seeded
//     }
// 
//     /// <summary>
//     /// Minimal fake implementation for QualifiedChannelId.
//     /// </summary>
//     internal class FakeQualifiedChannelId
//     {
//         public long Id { get; }
//         public ChannelSource Source { get; }
//         public string DebuggerDisplay => $"ID:{Id}";
// 
//         public FakeQualifiedChannelId(long id, ChannelSource source)
//         {
//             Id = id;
//             Source = source;
//         }
// 
//         public static implicit operator QualifiedChannelId(FakeQualifiedChannelId fake)
//         {
//             return new QualifiedChannelId(fake.Id, fake.Source);
//         }
//     }
// 
//     /// <summary>
//     /// Minimal implementation for QualifiedChannelId used by Channel.
//     /// </summary>
//     public readonly struct QualifiedChannelId
//     {
//         public long Id { get; }
// //         public ChannelSource Source { get; } [Error] (49-30)CS0053 Inconsistent accessibility: property type 'ChannelSource' is less accessible than property 'QualifiedChannelId.Source'
//         public string DebuggerDisplay => $"ID:{Id}";
// 
//         public QualifiedChannelId(long id, ChannelSource source)
//         {
//             Id = id;
//             Source = source;
//         }
//     }
// 
//     /// <summary>
//     /// Minimal fake formatter to support serialization calls.
//     /// </summary>
//     internal class FakeFormatter
//     {
// //         public ReadOnlySequence<byte> Serialize(MultiplexingStream.Channel.AcceptanceParameters parameters) [Error] (64-76)CS0122 'MultiplexingStream.Channel.AcceptanceParameters' is inaccessible due to its protection level [Error] (66-44)CS0122 'MultiplexingStream.Channel.AcceptanceParameters.RemoteWindowSize' is inaccessible due to its protection level
// //         {
// //             byte value = (byte)(parameters.RemoteWindowSize ?? 0);
// //             return new ReadOnlySequence<byte>(new byte[] { value });
// //         }
// 
//         public ReadOnlySequence<byte> SerializeContentProcessed(long bytes)
//         {
//             byte value = (byte)(bytes & 0xFF);
//             return new ReadOnlySequence<byte>(new byte[] { value });
//         }
//     }
// 
//     /// <summary>
//     /// Minimal fake implementation for MultiplexingStream.
//     /// </summary>
// //     internal class FakeMultiplexingStream : MultiplexingStream [Error] (89-16)CS1729 'MultiplexingStream' does not contain a constructor that takes 0 arguments [Error] (91-13)CS0122 'MultiplexingStream.protocolMajorVersion' is inaccessible due to its protection level [Error] (92-13)CS0200 Property or indexer 'MultiplexingStream.DefaultChannelReceivingWindowSize' cannot be assigned to -- it is read only [Error] (93-13)CS0122 'MultiplexingStream.DefaultChannelTraceSourceFactory' is inaccessible due to its protection level [Error] (94-13)CS0122 'MultiplexingStream.formatter' is inaccessible due to its protection level
// //     {
// //         public int OnChannelDisposedCallCount { get; private set; }
// //         public int OnChannelWritingCompletedCallCount { get; private set; }
// //         public FrameHeader LastSentFrameHeader { get; private set; }
// //         public ReadOnlySequence<byte> LastSentFramePayload { get; private set; }
// //         public bool SendFrameAsyncCalled { get; private set; }
// //         public FakeFormatter FakeFormatter { get; } = new FakeFormatter();
// // 
// //         public FakeMultiplexingStream()
// //         {
// //             protocolMajorVersion = 2;
// //             DefaultChannelReceivingWindowSize = 1024;
// //             DefaultChannelTraceSourceFactory = (id, name) => new TraceSource($"{id.DebuggerDisplay}_{name}", SourceLevels.All);
// //             formatter = FakeFormatter;
// //         }
// // 
// //         public override void SendFrame(FrameHeader header, ReadOnlySequence<byte> payload, CancellationToken cancellationToken) [Error] (97-30)CS0115 'FakeMultiplexingStream.SendFrame(FrameHeader, ReadOnlySequence<byte>, CancellationToken)': no suitable method found to override
// //         {
// //             LastSentFrameHeader = header;
// //             LastSentFramePayload = payload;
// //         }
// 
// //         public override async ValueTask SendFrameAsync(FrameHeader header, ReadOnlySequence<byte> payload, CancellationToken cancellationToken) [Error] (103-41)CS0115 'FakeMultiplexingStream.SendFrameAsync(FrameHeader, ReadOnlySequence<byte>, CancellationToken)': no suitable method found to override
// //         {
// //             LastSentFrameHeader = header;
// //             LastSentFramePayload = payload;
// //             SendFrameAsyncCalled = true;
// //             await Task.CompletedTask;
// //         }
// 
// //         public override void OnChannelDisposed(MultiplexingStream.Channel channel, Exception? ex) [Error] (111-30)CS0115 'FakeMultiplexingStream.OnChannelDisposed(MultiplexingStream.Channel, Exception?)': no suitable method found to override
// //         {
// //             OnChannelDisposedCallCount++;
// //         }
// 
// //         public override void OnChannelWritingCompleted(MultiplexingStream.Channel channel) [Error] (116-30)CS0115 'FakeMultiplexingStream.OnChannelWritingCompleted(MultiplexingStream.Channel)': no suitable method found to override
// //         {
// //             OnChannelWritingCompletedCallCount++;
// //         }
//     }
// 
//     /// <summary>
//     /// Minimal fake implementation for IDuplexPipe.
//     /// </summary>
//     internal class FakeDuplexPipe : IDuplexPipe
//     {
//         public PipeReader Input { get; }
//         public PipeWriter Output { get; }
// 
//         public FakeDuplexPipe()
//         {
//             var pipe = new Pipe();
//             Input = pipe.Reader;
//             Output = pipe.Writer;
//         }
//     }
// 
//     /// <summary>
//     /// Minimal fake implementation for ChannelOptions.
//     /// </summary>
//     internal class FakeChannelOptions : ChannelOptions
//     {
//         public override TraceSource? TraceSource { get; set; }
//         public override IDuplexPipe? ExistingPipe { get; set; }
//         public override long? ChannelReceivingWindowSize { get; set; }
//     }
// 
//     /// <summary>
//     /// Minimal abstract definition for ChannelOptions required by Channel.
//     /// </summary>
//     public abstract class ChannelOptions
//     {
//         public abstract TraceSource? TraceSource { get; set; }
//         public abstract IDuplexPipe? ExistingPipe { get; set; }
//         public abstract long? ChannelReceivingWindowSize { get; set; }
//     }
// 
//     /// <summary>
//     /// Minimal fake implementation for FrameHeader.
//     /// </summary>
//     public class FrameHeader
//     {
//         public ControlCode Code { get; set; }
//         public QualifiedChannelId ChannelId { get; set; }
//     }
// 
//     /// <summary>
//     /// Minimal definition for ControlCode.
//     /// </summary>
//     public enum ControlCode
//     {
//         OfferAccepted,
//         Content,
//         ContentProcessed
//     }
// 
//     /// <summary>
//     /// Unit tests for the <see cref="MultiplexingStream.Channel"/> class.
//     /// </summary>
// //     public class MultiplexingStreamChannelTests [Error] (190-62)CS0122 'MultiplexingStream.Channel.OfferParameters' is inaccessible due to its protection level
// //     {
// //         private readonly FakeMultiplexingStream fakeStream;
// //         private readonly FakeQualifiedChannelId fakeChannelId;
// //         private readonly MultiplexingStream.Channel.OfferParameters offerParameters; [Error] (184-53)CS0122 'MultiplexingStream.Channel.OfferParameters' is inaccessible due to its protection level
// 
//         public MultiplexingStreamChannelTests()
//         {
//             fakeStream = new FakeMultiplexingStream();
//             fakeChannelId = new FakeQualifiedChannelId(100, ChannelSource.Local);
//             offerParameters = new MultiplexingStream.Channel.OfferParameters("TestChannel", 512);
//         }
// 
//         /// <summary>
//         /// Tests that the QualifiedId and Id properties return the correct identifiers.
//         /// </summary>
// //         [Fact] [Error] (200-31)CS1729 'MultiplexingStream.Channel' does not contain a constructor that takes 3 arguments [Error] (207-26)CS1503 Argument 1: cannot convert from 'long' to 'System.DateTime' [Error] (207-44)CS1503 Argument 2: cannot convert from 'ulong' to 'System.DateTime'
// //         public void QualifiedIdAndId_ValidChannel_ReturnsCorrectIdentifiers()
// //         {
// //             // Arrange
// //             var channel = new MultiplexingStream.Channel(fakeStream, fakeChannelId, offerParameters);
// // 
// //             // Act
// //             var qualifiedId = channel.QualifiedId;
// //             int id = channel.Id;
// // 
// //             // Assert
// //             Assert.Equal(fakeChannelId.Id, qualifiedId.Id);
// //             Assert.Equal((int)fakeChannelId.Id, id);
// //         }
// 
//         /// <summary>
//         /// Tests that TryAcceptOffer successfully accepts an offer and completes the Acceptance task.
//         /// </summary>
// //         [Fact] [Error] (218-31)CS1729 'MultiplexingStream.Channel' does not contain a constructor that takes 3 arguments [Error] (224-35)CS0122 'MultiplexingStream.Channel.TryAcceptOffer(MultiplexingStream.ChannelOptions)' is inaccessible due to its protection level [Error] (229-55)CS1061 'Task' does not contain a definition for 'Result' and no accessible extension method 'Result' accepting a first argument of type 'Task' could be found (are you missing a using directive or an assembly reference?)
// //         public void TryAcceptOffer_ValidOptions_ReturnsTrueAndCompletesAcceptanceTask()
// //         {
// //             // Arrange
// //             var channel = new MultiplexingStream.Channel(fakeStream, fakeChannelId, offerParameters);
// //             var options = Substitute.For<ChannelOptions>();
// //             options.ChannelReceivingWindowSize.Returns(2048);
// //             options.TraceSource.Returns(new TraceSource("Test", SourceLevels.All));
// // 
// //             // Act
// //             bool result = channel.TryAcceptOffer(options);
// // 
// //             // Assert
// //             Assert.True(result);
// //             Assert.True(channel.Acceptance.IsCompleted);
// //             var acceptanceResult = channel.Acceptance.Result;
// //             Assert.Equal(2048, acceptanceResult.RemoteWindowSize);
// //         }
// 
//         /// <summary>
//         /// Tests that TryAcceptOffer returns false when called after the offer has already been accepted.
//         /// </summary>
// //         [Fact] [Error] (240-31)CS1729 'MultiplexingStream.Channel' does not contain a constructor that takes 3 arguments [Error] (244-40)CS0122 'MultiplexingStream.Channel.TryAcceptOffer(MultiplexingStream.ChannelOptions)' is inaccessible due to its protection level [Error] (247-41)CS0122 'MultiplexingStream.Channel.TryAcceptOffer(MultiplexingStream.ChannelOptions)' is inaccessible due to its protection level
// //         public void TryAcceptOffer_AlreadyAccepted_ReturnsFalse()
// //         {
// //             // Arrange
// //             var channel = new MultiplexingStream.Channel(fakeStream, fakeChannelId, offerParameters);
// //             var options = Substitute.For<ChannelOptions>();
// //             options.ChannelReceivingWindowSize.Returns(2048);
// //             options.TraceSource.Returns(new TraceSource("Test", SourceLevels.All));
// //             bool firstResult = channel.TryAcceptOffer(options);
// // 
// //             // Act
// //             bool secondResult = channel.TryAcceptOffer(options);
// // 
// //             // Assert
// //             Assert.True(firstResult);
// //             Assert.False(secondResult);
// //         }
// 
//         /// <summary>
//         /// Tests that OnAccepted successfully sets the acceptance result and updates the remote window size.
//         /// </summary>
// //         [Fact] [Error] (261-31)CS1729 'MultiplexingStream.Channel' does not contain a constructor that takes 3 arguments [Error] (262-67)CS0122 'MultiplexingStream.Channel.AcceptanceParameters' is inaccessible due to its protection level [Error] (265-35)CS0122 'MultiplexingStream.Channel.OnAccepted(MultiplexingStream.Channel.AcceptanceParameters)' is inaccessible due to its protection level [Error] (270-51)CS1061 'Task' does not contain a definition for 'Result' and no accessible extension method 'Result' accepting a first argument of type 'Task' could be found (are you missing a using directive or an assembly reference?)
// //         public void OnAccepted_ValidParameters_ReturnsTrueAndSetsWindowSize()
// //         {
// //             // Arrange
// //             var channel = new MultiplexingStream.Channel(fakeStream, fakeChannelId, offerParameters);
// //             var acceptanceParams = new MultiplexingStream.Channel.AcceptanceParameters(4096);
// // 
// //             // Act
// //             bool result = channel.OnAccepted(acceptanceParams);
// // 
// //             // Assert
// //             Assert.True(result);
// //             Assert.True(channel.Acceptance.IsCompleted);
// //             Assert.Equal(4096, channel.Acceptance.Result.RemoteWindowSize);
// //         }
// 
//         /// <summary>
//         /// Tests that calling Dispose correctly disposes the channel and completes the Completion task.
//         /// </summary>
// //         [Fact] [Error] (280-31)CS1729 'MultiplexingStream.Channel' does not contain a constructor that takes 3 arguments
// //         public void Dispose_Called_ChannelIsDisposedAndCompletionTaskIsCompleted()
// //         {
// //             // Arrange
// //             var channel = new MultiplexingStream.Channel(fakeStream, fakeChannelId, offerParameters);
// //             Assert.False(channel.IsDisposed);
// // 
// //             // Act
// //             channel.Dispose();
// //             Task.Delay(100).Wait();
// // 
// //             // Assert
// //             Assert.True(channel.IsDisposed);
// //             Assert.True(channel.Completion.IsCompleted);
// //         }
// 
//         /// <summary>
//         /// Tests that accessing Input and Output properties throws NotSupportedException when ExistingPipe is specified.
//         /// </summary>
// //         [Fact] [Error] (304-31)CS1729 'MultiplexingStream.Channel' does not contain a constructor that takes 4 arguments
// //         public void InputOutput_WhenExistingPipeSpecified_ThrowsNotSupportedException()
// //         {
// //             // Arrange
// //             var fakeExistingPipe = new FakeDuplexPipe();
// //             var options = Substitute.For<ChannelOptions>();
// //             options.ExistingPipe.Returns(fakeExistingPipe);
// //             options.TraceSource.Returns(new TraceSource("Test", SourceLevels.All));
// //             options.ChannelReceivingWindowSize.Returns(1024);
// //             var channel = new MultiplexingStream.Channel(fakeStream, fakeChannelId, offerParameters, options);
// // 
// //             // Act & Assert
// //             Assert.Throws<NotSupportedException>(() => { var _ = channel.Input; });
// //             Assert.Throws<NotSupportedException>(() => { var _ = channel.Output; });
// //         }
// 
//         /// <summary>
//         /// Tests that calling OnContentProcessed with valid byte count does not throw an exception.
//         /// </summary>
// //         [Fact] [Error] (318-31)CS1729 'MultiplexingStream.Channel' does not contain a constructor that takes 3 arguments [Error] (322-21)CS0122 'MultiplexingStream.Channel.TryAcceptOffer(MultiplexingStream.ChannelOptions)' is inaccessible due to its protection level [Error] (325-60)CS0122 'MultiplexingStream.Channel.OnContentProcessed(long)' is inaccessible due to its protection level
// //         public void OnContentProcessed_ValidBytes_DoesNotThrow()
// //         {
// //             // Arrange
// //             var channel = new MultiplexingStream.Channel(fakeStream, fakeChannelId, offerParameters);
// //             var options = Substitute.For<ChannelOptions>();
// //             options.ChannelReceivingWindowSize.Returns(1024);
// //             options.TraceSource.Returns(new TraceSource("Test", SourceLevels.All));
// //             channel.TryAcceptOffer(options);
// // 
// //             // Act & Assert
// //             var exception = Record.Exception(() => channel.OnContentProcessed(100));
// //             Assert.Null(exception);
// //         }
// 
//         /// <summary>
//         /// Tests that OnChannelTerminatedAsync disposes the channel and sets the IsRemotelyTerminated flag.
//         /// </summary>
// //         [Fact] [Error] (336-31)CS1729 'MultiplexingStream.Channel' does not contain a constructor that takes 3 arguments [Error] (337-63)CS0122 'MultiplexingStream.Channel.AcceptanceParameters' is inaccessible due to its protection level [Error] (337-21)CS0122 'MultiplexingStream.Channel.OnAccepted(MultiplexingStream.Channel.AcceptanceParameters)' is inaccessible due to its protection level [Error] (340-27)CS0122 'MultiplexingStream.Channel.OnChannelTerminatedAsync(Exception?)' is inaccessible due to its protection level [Error] (345-33)CS0122 'MultiplexingStream.Channel.IsRemotelyTerminated' is inaccessible due to its protection level
// //         public async Task OnChannelTerminatedAsync_ValidCall_ChannelTerminates()
// //         {
// //             // Arrange
// //             var channel = new MultiplexingStream.Channel(fakeStream, fakeChannelId, offerParameters);
// //             channel.OnAccepted(new MultiplexingStream.Channel.AcceptanceParameters(1024));
// // 
// //             // Act
// //             await channel.OnChannelTerminatedAsync(new Exception("Remote termination"));
// //             await Task.Delay(50);
// // 
// //             // Assert
// //             Assert.True(channel.IsDisposed);
// //             Assert.True(channel.IsRemotelyTerminated);
// //         }
// 
//         /// <summary>
//         /// Tests that OnContentWritingCompleted triggers writer completion and the channel's Completion task eventually completes.
//         /// </summary>
// //         [Fact] [Error] (355-31)CS1729 'MultiplexingStream.Channel' does not contain a constructor that takes 3 arguments [Error] (356-63)CS0122 'MultiplexingStream.Channel.AcceptanceParameters' is inaccessible due to its protection level [Error] (356-21)CS0122 'MultiplexingStream.Channel.OnAccepted(MultiplexingStream.Channel.AcceptanceParameters)' is inaccessible due to its protection level [Error] (359-21)CS0122 'MultiplexingStream.Channel.OnContentWritingCompleted()' is inaccessible due to its protection level
// //         public async Task OnContentWritingCompleted_WriterCompletes_ChannelWriterCompleted()
// //         {
// //             // Arrange
// //             var channel = new MultiplexingStream.Channel(fakeStream, fakeChannelId, offerParameters);
// //             channel.OnAccepted(new MultiplexingStream.Channel.AcceptanceParameters(1024));
// // 
// //             // Act
// //             channel.OnContentWritingCompleted();
// //             await Task.Delay(50);
// // 
// //             // Assert
// //             Assert.True(channel.Completion.IsCompleted);
// //             Assert.True(fakeStream.OnChannelWritingCompletedCallCount > 0);
// //         }
// 
//         /// <summary>
//         /// Tests that the OptionsApplied task completes after channel options are applied.
//         /// </summary>
// //         [Fact] [Error] (377-31)CS1729 'MultiplexingStream.Channel' does not contain a constructor that takes 4 arguments [Error] (380-27)CS0122 'MultiplexingStream.Channel.OptionsApplied' is inaccessible due to its protection level [Error] (383-33)CS0122 'MultiplexingStream.Channel.OptionsApplied' is inaccessible due to its protection level
// //         public async Task OptionsApplied_ChannelOptionsApplied_TaskCompletes()
// //         {
// //             // Arrange
// //             var options = Substitute.For<ChannelOptions>();
// //             options.ChannelReceivingWindowSize.Returns(2048);
// //             options.TraceSource.Returns(new TraceSource("Test", SourceLevels.All));
// //             var channel = new MultiplexingStream.Channel(fakeStream, fakeChannelId, offerParameters, options);
// // 
// //             // Act
// //             await channel.OptionsApplied;
// // 
// //             // Assert
// //             Assert.True(channel.OptionsApplied.IsCompleted);
// //         }
//     }
// }
