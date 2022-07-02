// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;
    using System.CodeDom.Compiler;
    using System.Data;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Pipelines;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Runtime.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;
    using Microsoft.VisualStudio.Threading;

    /// <content>
    /// Contains the <see cref="Channel"/> nested type.
    /// </content>
    public partial class MultiplexingStream
    {
        /// <summary>
        /// An individual channel within a <see cref="Streams.MultiplexingStream"/>.
        /// </summary>
        [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
        public class Channel : IDisposableObservable, IDuplexPipe
        {
            /// <summary>
            /// This task source completes when the channel has been accepted, rejected, or the offer is canceled.
            /// </summary>
            private readonly TaskCompletionSource<AcceptanceParameters> acceptanceSource = new TaskCompletionSource<AcceptanceParameters>(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// The source for the <see cref="Completion"/> property.
            /// </summary>
            private readonly TaskCompletionSource<object?> completionSource = new TaskCompletionSource<object?>();

            /// <summary>
            /// The source for a token that will be canceled when this channel has completed.
            /// </summary>
            private readonly CancellationTokenSource disposalTokenSource = new CancellationTokenSource();

            /// <summary>
            /// The source for the <see cref="OptionsApplied"/> property. May be null if options were provided in ctor.
            /// </summary>
            private readonly TaskCompletionSource<object?>? optionsAppliedTaskSource;

            /// <summary>
            /// Tracks the end of any copying from the mxstream to this channel.
            /// </summary>
            private readonly AsyncManualResetEvent mxStreamIOWriterCompleted = new AsyncManualResetEvent();

            /// <summary>
            /// Gets a signal which indicates when the <see cref="RemoteWindowRemaining"/> is non-zero.
            /// </summary>
            private readonly AsyncManualResetEvent remoteWindowHasCapacity = new AsyncManualResetEvent(initialState: true);

            /// <summary>
            /// The party-qualified id of the channel.
            /// </summary>
            private readonly QualifiedChannelId channelId;

            /// <summary>
            /// The number of bytes transmitted from here but not yet acknowledged as processed from there,
            /// and thus occupying some portion of the full <see cref="AcceptanceParameters.RemoteWindowSize"/>.
            /// </summary>
            /// <remarks>
            /// All access to this field should be made within a lock on the <see cref="SyncObject"/> object.
            /// </remarks>
            private long remoteWindowFilled = 0;

            /// <summary>
            /// The number of bytes that may be transmitted before receiving acknowledgment that those bytes have been processed.
            /// </summary>
            /// <remarks>
            /// This field is set to the value of <see cref="OfferParameters.RemoteWindowSize"/> if we accepted the channel,
            /// or the value of <see cref="AcceptanceParameters.RemoteWindowSize"/> if we offered the channel.
            /// </remarks>
            private long? remoteWindowSize;

            /// <summary>
            /// The number of bytes that may be received and buffered for processing.
            /// </summary>
            /// <remarks>
            /// This field is set to the value of <see cref="OfferParameters.RemoteWindowSize"/> if we offered the channel,
            /// or the value of <see cref="AcceptanceParameters.RemoteWindowSize"/> if we accepted the channel.
            /// </remarks>
            private long? localWindowSize;

            /// <summary>
            /// Indicates whether the <see cref="Dispose"/> method has been called.
            /// </summary>
            private bool isDisposed;

            /// <summary>
            /// The <see cref="PipeReader"/> to use to get data to be transmitted over the <see cref="Streams.MultiplexingStream"/>.
            /// </summary>
            private PipeReader? mxStreamIOReader;

            /// <summary>
            /// A task that represents the completion of the <see cref="mxStreamIOReader"/>,
            /// signifying the point where we will stop relaying data from the channel to the <see cref="MultiplexingStream"/> for transmission to the remote party.
            /// </summary>
            private Task? mxStreamIOReaderCompleted;

            /// <summary>
            /// The <see cref="PipeWriter"/> the underlying <see cref="Streams.MultiplexingStream"/> should use.
            /// </summary>
            private PipeWriter? mxStreamIOWriter;

            /// <summary>
            /// The I/O to expose on this channel if <see cref="ChannelOptions.ExistingPipe"/> was not specified;
            /// otherwise it is the buffering pipe we use as an intermediary with the specified <see cref="ChannelOptions.ExistingPipe"/>.
            /// </summary>
            private IDuplexPipe? channelIO;

            /// <summary>
            /// The value of <see cref="ChannelOptions.ExistingPipe"/> as it was when we received it.
            /// We don't use this field, but we set it for diagnostic purposes later.
            /// </summary>
            private IDuplexPipe? existingPipe;

            /// <summary>
            /// A value indicating whether this <see cref="Channel"/> was created or accepted with a non-null value for <see cref="ChannelOptions.ExistingPipe"/>.
            /// </summary>
            private bool? existingPipeGiven;

            /// <summary>
            /// Initializes a new instance of the <see cref="Channel"/> class.
            /// </summary>
            /// <param name="multiplexingStream">The owning <see cref="Streams.MultiplexingStream"/>.</param>
            /// <param name="channelId">The party-qualified ID of the channel.</param>
            /// <param name="offerParameters">The parameters of the channel from the offering party.</param>
            /// <param name="channelOptions">The channel options. Should only be null if the channel is created in response to an offer that is not immediately accepted.</param>
            internal Channel(MultiplexingStream multiplexingStream, QualifiedChannelId channelId, OfferParameters offerParameters, ChannelOptions? channelOptions = null)
            {
                Requires.NotNull(multiplexingStream, nameof(multiplexingStream));
                Requires.NotNull(offerParameters, nameof(offerParameters));

                this.MultiplexingStream = multiplexingStream;
                this.channelId = channelId;
                this.OfferParams = offerParameters;

                switch (channelId.Source)
                {
                    case ChannelSource.Local:
                        this.localWindowSize = offerParameters.RemoteWindowSize;
                        break;
                    case ChannelSource.Remote:
                        this.remoteWindowSize = offerParameters.RemoteWindowSize;
                        break;
                    case ChannelSource.Seeded:
                        this.remoteWindowSize = offerParameters.RemoteWindowSize;
                        this.localWindowSize = offerParameters.RemoteWindowSize;
                        break;
                    default:
                        throw new NotSupportedException();
                }

                if (channelOptions == null)
                {
                    this.optionsAppliedTaskSource = new TaskCompletionSource<object?>();
                }
                else
                {
                    this.ApplyChannelOptions(channelOptions);
                }
            }

            /// <summary>
            /// Gets the unique ID for this channel.
            /// </summary>
            /// <remarks>
            /// This value is usually shared for an anonymous channel so the remote party
            /// can accept it with <see cref="AcceptChannel(int, ChannelOptions)"/> or
            /// reject it with <see cref="RejectChannel(int)"/>.
            /// </remarks>
            [Obsolete("Use " + nameof(QualifiedId) + " instead.")]
            public int Id => checked((int)this.channelId.Id);

            /// <summary>
            /// Gets the unique ID for this channel.
            /// </summary>
            /// <remarks>
            /// This value is usually shared for an anonymous channel so the remote party
            /// can accept it with <see cref="AcceptChannel(int, ChannelOptions)"/> or
            /// reject it with <see cref="RejectChannel(int)"/>.
            /// </remarks>
            public QualifiedChannelId QualifiedId => this.channelId;

            /// <summary>
            /// Gets the mechanism used for tracing activity related to this channel.
            /// </summary>
            /// <value>A non-null value, once <see cref="ApplyChannelOptions(ChannelOptions)"/> has been called.</value>
            public TraceSource? TraceSource { get; private set; }

            /// <inheritdoc />
            public bool IsDisposed => this.isDisposed || this.Completion.IsCompleted;

            /// <summary>
            /// Gets the reader used to receive data over the channel.
            /// </summary>
            /// <exception cref="NotSupportedException">Thrown if the channel was created with a non-null value in <see cref="ChannelOptions.ExistingPipe"/>.</exception>
            public PipeReader Input
            {
                get
                {
                    // Before the user should ever have a chance to call this property (before we expose this Channel object)
                    // we should have received a ChannelOptions object from them and initialized these fields.
                    Assumes.True(this.existingPipeGiven.HasValue);
                    Assumes.NotNull(this.channelIO);

                    return this.existingPipeGiven.Value ? throw new NotSupportedException(Strings.NotSupportedWhenExistingPipeSpecified) : this.channelIO.Input;
                }
            }

            /// <summary>
            /// Gets the writer used to transmit data over the channel.
            /// </summary>
            /// <exception cref="NotSupportedException">Thrown if the channel was created with a non-null value in <see cref="ChannelOptions.ExistingPipe"/>.</exception>
            public PipeWriter Output
            {
                get
                {
                    // Before the user should ever have a chance to call this property (before we expose this Channel object)
                    // we should have received a ChannelOptions object from them and initialized these fields.
                    Assumes.True(this.existingPipeGiven.HasValue);
                    Assumes.NotNull(this.channelIO);

                    return this.existingPipeGiven.Value ? throw new NotSupportedException(Strings.NotSupportedWhenExistingPipeSpecified) : this.channelIO.Output;
                }
            }

            /// <summary>
            /// Gets a <see cref="Task"/> that completes when the channel is accepted, rejected, or canceled.
            /// </summary>
            /// <remarks>
            /// If the channel is accepted, this task transitions to <see cref="TaskStatus.RanToCompletion"/> state.
            /// If the channel offer is canceled, this task transitions to a <see cref="TaskStatus.Canceled"/> state.
            /// If the channel offer is rejected, this task transitions to a <see cref="TaskStatus.Canceled"/> state.
            /// </remarks>
            public Task Acceptance => this.acceptanceSource.Task;

            /// <summary>
            /// Gets a <see cref="Task"/> that completes when the channel is disposed,
            /// which occurs when <see cref="Dispose()"/> is invoked or when both sides
            /// have indicated they are done writing to the channel.
            /// </summary>
            public Task Completion => this.completionSource.Task;

            /// <summary>
            /// Gets the underlying <see cref="Streams.MultiplexingStream"/> instance.
            /// </summary>
            public MultiplexingStream MultiplexingStream { get; }

            /// <summary>
            /// Gets a token that is canceled just before <see cref="Completion" /> has transitioned to its final state.
            /// </summary>
            internal CancellationToken DisposalToken => this.disposalTokenSource.Token;

            internal OfferParameters OfferParams { get; }

            internal string Name => this.OfferParams.Name;

            internal bool IsAccepted => this.Acceptance.Status == TaskStatus.RanToCompletion;

            internal bool IsRejectedOrCanceled => this.Acceptance.Status == TaskStatus.Canceled;

            internal bool IsRemotelyTerminated { get; set; }

            /// <summary>
            /// Gets a <see cref="Task"/> that completes when options have been applied to this <see cref="Channel"/>.
            /// </summary>
            internal Task OptionsApplied => this.optionsAppliedTaskSource?.Task ?? Task.CompletedTask;

            private string DebuggerDisplay => $"{this.QualifiedId.DebuggerDisplay} {this.Name ?? "(anonymous)"}";

            /// <summary>
            /// Gets an object that can be locked to make critical changes to this instance's fields.
            /// </summary>
            /// <remarks>
            /// We reuse an object we already have to avoid having to create a new System.Object instance just to lock with.
            /// </remarks>
            private object SyncObject => this.acceptanceSource;

            /// <summary>
            /// Gets the number of bytes that may be transmitted over this channel given the
            /// remaining space in the <see cref="remoteWindowSize"/>.
            /// </summary>
            private long RemoteWindowRemaining
            {
                get
                {
                    lock (this.SyncObject)
                    {
                        Assumes.True(this.remoteWindowSize > 0);
                        return this.remoteWindowSize.Value - this.remoteWindowFilled;
                    }
                }
            }

            /// <summary>
            /// Gets a value indicating whether backpressure support is enabled.
            /// </summary>
            private bool BackpressureSupportEnabled => this.MultiplexingStream.protocolMajorVersion > 1;

            /// <summary>
            /// Closes this channel and releases all resources associated with it.
            /// </summary>
            /// <remarks>
            /// Because this method may terminate the channel immediately and thus can cause previously queued content to not actually be received by the remote party,
            /// consider this method a "break glass" way of terminating a channel. The preferred method is that both sides "complete writing" and let the channel dispose itself.
            /// </remarks>
            public void Dispose()
            {
                if (!this.IsDisposed)
                {
                    // The code in this delegate needs to happen in several branches including possibly asynchronously.
                    // We carefully define it here with no closure so that the C# compiler generates a static field for the delegate
                    // thus avoiding any extra allocations from reusing code in this way.
                    Action<object?, object> finalDisposalAction = (exOrAntecedent, state) =>
                    {
                        var self = (Channel)state;
                        self.disposalTokenSource.Cancel();
                        self.completionSource.TrySetResult(null);
                        self.MultiplexingStream.OnChannelDisposed(self);
                    };

                    this.acceptanceSource.TrySetCanceled();
                    this.optionsAppliedTaskSource?.TrySetCanceled();

                    PipeWriter? mxStreamIOWriter;
                    lock (this.SyncObject)
                    {
                        this.isDisposed = true;
                        mxStreamIOWriter = this.mxStreamIOWriter;
                    }

                    // Complete writing so that the mxstream cannot write to this channel any more.
                    // We must also cancel a pending flush since no one is guaranteed to be reading this any more
                    // and we don't want to deadlock on a full buffer in a disposed channel's pipe.
                    mxStreamIOWriter?.Complete();
                    mxStreamIOWriter?.CancelPendingFlush();
                    this.mxStreamIOWriterCompleted.Set();

                    if (this.channelIO != null)
                    {
                        // We're using our own Pipe to relay user messages, so we can shutdown writing and allow for our reader to propagate what was already written
                        // before actually shutting down.
                        this.channelIO.Output.Complete();
                    }
                    else
                    {
                        // We don't own the user's PipeWriter to complete it (so they can't write anything more to this channel).
                        // We can't know whether there is or will be more bytes written to the user's PipeWriter,
                        // but we need to terminate our reader for their writer as part of reclaiming resources.
                        // We want to complete reading immediately and cancel any pending read.
                        this.mxStreamIOReader?.Complete();
                        this.mxStreamIOReader?.CancelPendingRead();
                    }

                    // Unblock the reader that might be waiting on this.
                    this.remoteWindowHasCapacity.Set();

                    // As a minor perf optimization, avoid allocating a continuation task if the antecedent is already completed.
                    if (this.mxStreamIOReaderCompleted?.IsCompleted ?? true)
                    {
                        finalDisposalAction(null, this);
                    }
                    else
                    {
                        this.mxStreamIOReaderCompleted!.ContinueWith(finalDisposalAction!, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Forget();
                    }
                }
            }

            internal async Task OnChannelTerminatedAsync()
            {
                if (this.IsDisposed)
                {
                    return;
                }

                try
                {
                    // We Complete the writer because only the writing (logical) thread should complete it
                    // to avoid race conditions, and Channel.Dispose can be called from any thread.
                    var writer = this.GetReceivedMessagePipeWriter();
                    await writer.CompleteAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // We fell victim to a race condition. It's OK to just swallow it because the writer was never created, so it needn't be completed.
                }
            }

            /// <summary>
            /// Receives content from the <see cref="MultiplexingStream"/> that is bound for this channel.
            /// </summary>
            /// <param name="payload">The content for this channel.</param>
            /// <param name="cancellationToken">A token that is canceled if the overall <see cref="MultiplexingStream"/> gets disposed of.</param>
            /// <returns>
            /// A task that completes when content has been accepted.
            /// All multiplexing stream reads are held up till this completes, so this should only pause in exceptional circumstances.
            /// Faulting the returned <see cref="ValueTask"/> will fault the whole multiplexing stream.
            /// </returns>
            internal async ValueTask OnContentAsync(ReadOnlySequence<byte> payload, CancellationToken cancellationToken)
            {
                try
                {
                    PipeWriter writer = this.GetReceivedMessagePipeWriter();
                    foreach (var segment in payload)
                    {
                        try
                        {
                            var memory = writer.GetMemory(segment.Length);
                            segment.CopyTo(memory);
                            writer.Advance(segment.Length);
                        }
                        catch (InvalidOperationException)
                        {
                            // Someone completed the writer.
                            return;
                        }
                    }

                    if (!payload.IsEmpty && this.MultiplexingStream.TraceSource.Switch.ShouldTrace(TraceEventType.Verbose))
                    {
                        this.MultiplexingStream.TraceSource.TraceData(TraceEventType.Verbose, (int)TraceEventId.FrameReceivedPayload, payload);
                    }

                    ValueTask<FlushResult> flushResult = writer.FlushAsync(cancellationToken);
                    if (this.BackpressureSupportEnabled)
                    {
                        if (!flushResult.IsCompleted)
                        {
                            // The incoming data has overrun the size of the write buffer inside the PipeWriter.
                            // This should never happen if we created the Pipe because we specify the Pause threshold to exceed the window size.
                            // If it happens, it should be because someone specified an ExistingPipe with an inappropriately sized buffer in its PipeWriter.
                            Assumes.True(this.existingPipeGiven == true); // Make sure this isn't an internal error
                            this.Fault(new InvalidOperationException(Strings.ExistingPipeOutputHasPauseThresholdSetTooLow));
                        }
                    }
                    else
                    {
                        await flushResult.ConfigureAwait(false);
                    }

                    if (flushResult.IsCanceled)
                    {
                        // This happens when the channel is disposed (while or before flushing).
                        Assumes.True(this.IsDisposed);
                        await writer.CompleteAsync().ConfigureAwait(false);
                    }
                }
                catch (ObjectDisposedException) when (this.IsDisposed)
                {
                    // Just eat these.
                }
                catch (Exception ex)
                {
                    // Contain the damage for any other failure so we don't fault the entire multiplexing stream.
                    this.Fault(ex);
                }
            }

            /// <summary>
            /// Called by the <see cref="MultiplexingStream"/> when when it will not be writing any more data to the channel.
            /// </summary>
            internal void OnContentWritingCompleted()
            {
                this.DisposeSelfOnFailure(Task.Run(async delegate
                {
                    if (!this.IsDisposed)
                    {
                        try
                        {
                            var writer = this.GetReceivedMessagePipeWriter();
                            await writer.CompleteAsync().ConfigureAwait(false);
                        }
                        catch (ObjectDisposedException)
                        {
                            if (this.mxStreamIOWriter != null)
                            {
                                await this.mxStreamIOWriter.CompleteAsync().ConfigureAwait(false);
                            }
                        }
                    }
                    else
                    {
                        if (this.mxStreamIOWriter != null)
                        {
                            await this.mxStreamIOWriter.CompleteAsync().ConfigureAwait(false);
                        }
                    }

                    this.mxStreamIOWriterCompleted.Set();
                }));
            }

            /// <summary>
            /// Accepts an offer made by the remote party.
            /// </summary>
            /// <param name="channelOptions">The options to apply to the channel.</param>
            /// <returns>A value indicating whether the offer was accepted. It may fail if the channel was already closed or the offer rescinded.</returns>
            internal bool TryAcceptOffer(ChannelOptions channelOptions)
            {
                lock (this.SyncObject)
                {
                    // If the local window size has already been determined, we have to keep that since it can't be expanded once the Pipe is created.
                    // Otherwise use what the ChannelOptions asked for, so long as it is no smaller than the default channel size, since we can't make it smaller either.
                    this.localWindowSize ??= channelOptions.ChannelReceivingWindowSize is long windowSize ? Math.Max(windowSize, this.MultiplexingStream.DefaultChannelReceivingWindowSize) : this.MultiplexingStream.DefaultChannelReceivingWindowSize;
                }

                var acceptanceParameters = new AcceptanceParameters(this.localWindowSize.Value);
                if (this.acceptanceSource.TrySetResult(acceptanceParameters))
                {
                    if (this.QualifiedId.Source != ChannelSource.Seeded)
                    {
                        var payload = this.MultiplexingStream.formatter.Serialize(acceptanceParameters);
                        this.MultiplexingStream.SendFrame(
                            new FrameHeader
                            {
                                Code = ControlCode.OfferAccepted,
                                ChannelId = this.QualifiedId,
                            },
                            payload,
                            CancellationToken.None);
                    }

                    try
                    {
                        this.ApplyChannelOptions(channelOptions);
                        return true;
                    }
                    catch (ObjectDisposedException)
                    {
                        // A (harmless) race condition was hit.
                        // Swallow it and return false below.
                    }
                }

                return false;
            }

            /// <summary>
            /// Occurs when the remote party has accepted our offer of this channel.
            /// </summary>
            /// <param name="acceptanceParameters">The channel parameters provided by the accepting party.</param>
            /// <returns>A value indicating whether the acceptance went through; <c>false</c> if the channel is already accepted, rejected or offer rescinded.</returns>
            internal bool OnAccepted(AcceptanceParameters acceptanceParameters)
            {
                lock (this.SyncObject)
                {
                    if (this.acceptanceSource.TrySetResult(acceptanceParameters))
                    {
                        this.remoteWindowSize = acceptanceParameters.RemoteWindowSize;
                        return true;
                    }

                    return false;
                }
            }

            /// <summary>
            /// Invoked when the remote party acknowledges bytes we previously transmitted as processed,
            /// thereby allowing us to consider that data removed from the remote party's "window"
            /// and thus enables us to send more data to them.
            /// </summary>
            /// <param name="bytesProcessed">The number of bytes processed by the remote party.</param>
            internal void OnContentProcessed(long bytesProcessed)
            {
                Requires.Range(bytesProcessed >= 0, nameof(bytesProcessed), "A non-negative number is required.");
                lock (this.SyncObject)
                {
                    Assumes.True(bytesProcessed <= this.remoteWindowFilled);
                    this.remoteWindowFilled -= bytesProcessed;
                    if (this.remoteWindowFilled < this.remoteWindowSize)
                    {
                        this.remoteWindowHasCapacity.Set();
                    }
                }
            }

            /// <summary>
            /// Gets the pipe writer to use when a message is received for this channel, so that the channel owner will notice and read it.
            /// </summary>
            /// <returns>A <see cref="PipeWriter"/>.</returns>
            private PipeWriter GetReceivedMessagePipeWriter()
            {
                lock (this.SyncObject)
                {
                    Verify.NotDisposed(this);

                    PipeWriter? result = this.mxStreamIOWriter;
                    if (result == null)
                    {
                        this.InitializeOwnPipes();
                        result = this.mxStreamIOWriter!;
                    }

                    return result;
                }
            }

            /// <summary>
            /// Apply channel options to this channel, including setting up or linking to an user-supplied pipe writer/reader pair.
            /// </summary>
            /// <param name="channelOptions">The channel options to apply.</param>
            private void ApplyChannelOptions(ChannelOptions channelOptions)
            {
                Requires.NotNull(channelOptions, nameof(channelOptions));
                Assumes.Null(this.TraceSource); // We've already applied options

                try
                {
                    this.TraceSource = channelOptions.TraceSource
                        ?? this.MultiplexingStream.DefaultChannelTraceSourceFactory?.Invoke(this.QualifiedId, this.Name)
                        ?? new TraceSource($"{nameof(Streams.MultiplexingStream)}.{nameof(Channel)} {this.QualifiedId} ({this.Name})", SourceLevels.Critical);

                    lock (this.SyncObject)
                    {
                        Verify.NotDisposed(this);
                        this.InitializeOwnPipes();
                        if (channelOptions.ExistingPipe is object)
                        {
                            Assumes.NotNull(this.channelIO);
                            this.existingPipe = channelOptions.ExistingPipe;
                            this.existingPipeGiven = true;

                            // We always want to write ALL received data to the user's ExistingPipe, rather than truncating it on disposal, so don't use a cancellation token in that direction.
                            this.DisposeSelfOnFailure(this.channelIO.Input.LinkToAsync(channelOptions.ExistingPipe.Output));

                            // Upon disposal, we no longer want to continue reading from the user's ExistingPipe into our buffer since we won't be propagating it any further, so use our DisposalToken.
                            this.DisposeSelfOnFailure(channelOptions.ExistingPipe.Input.LinkToAsync(this.channelIO.Output, this.DisposalToken));
                        }
                        else
                        {
                            this.existingPipeGiven = false;
                        }
                    }

                    this.mxStreamIOReaderCompleted = this.ProcessOutboundTransmissionsAsync();
                    this.DisposeSelfOnFailure(this.mxStreamIOReaderCompleted);
                    this.DisposeSelfOnFailure(this.AutoCloseOnPipesClosureAsync());
                }
                catch (Exception ex)
                {
                    this.optionsAppliedTaskSource?.TrySetException(ex);
                    throw;
                }
                finally
                {
                    this.optionsAppliedTaskSource?.TrySetResult(null);
                }
            }

            /// <summary>
            /// Set up our own (buffering) Pipes if they have not been set up yet.
            /// </summary>
            private void InitializeOwnPipes()
            {
                lock (this.SyncObject)
                {
                    Verify.NotDisposed(this);
                    if (this.mxStreamIOReader is null)
                    {
                        if (this.localWindowSize is null)
                        {
                            // If an offer came in along with data before we accepted the channel, we have to set up the pipe
                            // before we know what the preferred local window size is. We can't change it after the fact, so just use the default.
                            this.localWindowSize = this.MultiplexingStream.DefaultChannelReceivingWindowSize;
                        }

                        var writerRelay = new Pipe();
                        var readerRelay = this.BackpressureSupportEnabled
                            ? new Pipe(new PipeOptions(pauseWriterThreshold: this.localWindowSize.Value + 1)) // +1 prevents pause when remote window is exactly filled
                            : new Pipe();
                        this.mxStreamIOReader = writerRelay.Reader;
                        this.mxStreamIOWriter = readerRelay.Writer;
                        this.channelIO = new DuplexPipe(this.BackpressureSupportEnabled ? new WindowPipeReader(this, readerRelay.Reader) : readerRelay.Reader, writerRelay.Writer);
                    }
                }
            }

            /// <summary>
            /// Relays data that the local channel owner wants to send to the remote party.
            /// </summary>
            private async Task ProcessOutboundTransmissionsAsync()
            {
                try
                {
                    // Don't transmit data on the channel until the remote party has accepted it.
                    // This is not just a courtesy: it ensure we don't transmit data from the offering party before the offer frame itself.
                    // Likewise: it may help prevent transmitting data from the accepting party before the acceptance frame itself.
                    await this.Acceptance.ConfigureAwait(false);

                    while (!this.Completion.IsCompleted)
                    {
                        if (!this.remoteWindowHasCapacity.IsSet && this.TraceSource!.Switch.ShouldTrace(TraceEventType.Verbose))
                        {
                            this.TraceSource.TraceEvent(TraceEventType.Verbose, 0, "Remote window is full. Waiting for remote party to process data before sending more.");
                        }

                        await this.remoteWindowHasCapacity.WaitAsync().ConfigureAwait(false);
                        if (this.IsRemotelyTerminated)
                        {
                            if (this.TraceSource!.Switch.ShouldTrace(TraceEventType.Verbose))
                            {
                                this.TraceSource.TraceEvent(TraceEventType.Verbose, 0, "Transmission on channel {0} \"{1}\" terminated the remote party terminated the channel.", this.QualifiedId, this.Name);
                            }

                            break;
                        }

                        ReadResult result;
                        try
                        {
                            result = await this.mxStreamIOReader!.ReadAsync().ConfigureAwait(false);
                        }
                        catch (InvalidOperationException ex)
                        {
                            // Someone completed the reader. The channel was probably disposed.
                            if (this.TraceSource!.Switch.ShouldTrace(TraceEventType.Verbose))
                            {
                                this.TraceSource.TraceEvent(TraceEventType.Verbose, 0, "Transmission terminated because the reader threw: {0}", ex);
                            }

                            break;
                        }

                        if (result.IsCanceled)
                        {
                            // We've been asked to cancel. Presumably the channel has been disposed.
                            if (this.TraceSource!.Switch.ShouldTrace(TraceEventType.Verbose))
                            {
                                this.TraceSource.TraceEvent(TraceEventType.Verbose, 0, "Transmission terminated because the read was canceled.");
                            }

                            break;
                        }

                        // We'll send whatever we've got, up to the maximum size of the frame or available window size.
                        // Anything in excess of that we'll pick up next time the loop runs.
                        long bytesToSend = Math.Min(result.Buffer.Length, FramePayloadMaxLength);
                        if (this.BackpressureSupportEnabled)
                        {
                            bytesToSend = Math.Min(this.RemoteWindowRemaining, bytesToSend);
                        }

                        var bufferToRelay = result.Buffer.Slice(0, bytesToSend);
                        this.OnTransmittingBytes(bufferToRelay.Length);
                        bool isCompleted = result.IsCompleted && result.Buffer.Length == bufferToRelay.Length;
                        if (this.TraceSource!.Switch.ShouldTrace(TraceEventType.Verbose))
                        {
                            this.TraceSource.TraceEvent(TraceEventType.Verbose, 0, "{0} of {1} bytes will be transmitted.", bufferToRelay.Length, result.Buffer.Length);
                        }

                        if (bufferToRelay.Length > 0)
                        {
                            FrameHeader header = new FrameHeader
                            {
                                Code = ControlCode.Content,
                                ChannelId = this.QualifiedId,
                            };

                            await this.MultiplexingStream.SendFrameAsync(header, bufferToRelay, CancellationToken.None).ConfigureAwait(false);
                        }

                        try
                        {
                            // Let the pipe know exactly how much we read, which might be less than we were given.
                            this.mxStreamIOReader.AdvanceTo(bufferToRelay.End);

                            // We mustn't accidentally access the memory that may have been recycled now that we called AdvanceTo.
                            bufferToRelay = default;
                            result.ScrubAfterAdvanceTo();
                        }
                        catch (InvalidOperationException ex)
                        {
                            // Someone completed the reader. The channel was probably disposed.
                            if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Verbose))
                            {
                                this.TraceSource.TraceEvent(TraceEventType.Verbose, 0, "Transmission terminated because the reader threw: {0}", ex);
                            }

                            break;
                        }

                        if (isCompleted)
                        {
                            if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
                            {
                                this.TraceSource.TraceEvent(TraceEventType.Information, 0, "Transmission terminated because the writer completed.");
                            }

                            break;
                        }
                    }

                    await this.mxStreamIOReader!.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await this.mxStreamIOReader!.CompleteAsync(ex).ConfigureAwait(false);
                    throw;
                }
                finally
                {
                    this.MultiplexingStream.OnChannelWritingCompleted(this);
                }
            }

            /// <summary>
            /// Invoked when we transmit data to the remote party
            /// so we can track how much data we're sending them so we don't overrun their receiving buffer.
            /// </summary>
            /// <param name="transmittedBytes">The number of bytes being transmitted.</param>
            private void OnTransmittingBytes(long transmittedBytes)
            {
                if (this.BackpressureSupportEnabled)
                {
                    Requires.Range(transmittedBytes >= 0, nameof(transmittedBytes), "A non-negative number is required.");
                    lock (this.SyncObject)
                    {
                        Requires.Range(this.remoteWindowFilled + transmittedBytes <= this.remoteWindowSize, nameof(transmittedBytes), "The value exceeds the space remaining in the window size.");
                        this.remoteWindowFilled += transmittedBytes;
                        if (this.remoteWindowFilled == this.remoteWindowSize)
                        {
                            this.remoteWindowHasCapacity.Reset();
                        }
                    }
                }
            }

            private void LocalContentExamined(long bytesExamined)
            {
                Requires.Range(bytesExamined >= 0, nameof(bytesExamined));
                if (bytesExamined == 0 || this.IsDisposed)
                {
                    return;
                }

                if (this.TraceSource!.Switch.ShouldTrace(TraceEventType.Verbose))
                {
                    this.TraceSource.TraceEvent(TraceEventType.Verbose, 0, "Acknowledging processing of {0} bytes.", bytesExamined);
                }

                this.MultiplexingStream.SendFrame(
                    new FrameHeader
                    {
                        Code = ControlCode.ContentProcessed,
                        ChannelId = this.QualifiedId,
                    },
                    this.MultiplexingStream.formatter.SerializeContentProcessed(bytesExamined),
                    CancellationToken.None);
            }

            private async Task AutoCloseOnPipesClosureAsync()
            {
                Assumes.NotNull(this.mxStreamIOReaderCompleted);
                await Task.WhenAll(this.mxStreamIOWriterCompleted.WaitAsync(), this.mxStreamIOReaderCompleted).ConfigureAwait(false);

                if (this.TraceSource!.Switch.ShouldTrace(TraceEventType.Information))
                {
                    this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEventId.ChannelAutoClosing, "Channel {0} \"{1}\" self-closing because both reader and writer are complete.", this.QualifiedId, this.Name);
                }

                this.Dispose();
            }

            private void Fault(Exception exception)
            {
                if (this.TraceSource?.Switch.ShouldTrace(TraceEventType.Critical) ?? false)
                {
                    this.TraceSource!.TraceEvent(TraceEventType.Critical, (int)TraceEventId.FatalError, "Channel Closing self due to exception: {0}", exception);
                }

                this.mxStreamIOReader?.Complete(exception);
                this.Dispose();
            }

            private void DisposeSelfOnFailure(Task task)
            {
                Requires.NotNull(task, nameof(task));

                if (task.IsCompleted)
                {
                    if (task.IsFaulted)
                    {
                        this.Fault(task.Exception!.InnerException ?? task.Exception);
                    }
                }
                else
                {
                    task.ContinueWith(
                        (t, s) => ((Channel)s!).Fault(t.Exception!.InnerException ?? t.Exception),
                        this,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default).Forget();
                }
            }

            [DataContract]
            internal class OfferParameters
            {
                /// <summary>
                /// Initializes a new instance of the <see cref="OfferParameters"/> class.
                /// </summary>
                /// <param name="name">The name of the channel.</param>
                /// <param name="remoteWindowSize">
                /// The maximum number of bytes that may be transmitted and not yet acknowledged as processed by the remote party.
                /// When based on <see cref="PipeOptions.PauseWriterThreshold"/>, this value should be -1 of that value in order
                /// to avoid the actual pause that would be fatal to the read loop of the multiplexing stream.
                /// </param>
                internal OfferParameters(string name, long? remoteWindowSize)
                {
                    this.Name = name ?? throw new ArgumentNullException(nameof(name));
                    this.RemoteWindowSize = remoteWindowSize;
                }

                /// <summary>
                /// Gets the name of the channel.
                /// </summary>
                [DataMember]
                internal string Name { get; }

                /// <summary>
                /// Gets the maximum number of bytes that may be transmitted and not yet acknowledged as processed by the remote party.
                /// </summary>
                [DataMember]
                internal long? RemoteWindowSize { get; }
            }

            [DataContract]
            internal class AcceptanceParameters
            {
                /// <summary>
                /// Initializes a new instance of the <see cref="AcceptanceParameters"/> class.
                /// </summary>
                /// <param name="remoteWindowSize">
                /// The maximum number of bytes that may be transmitted and not yet acknowledged as processed by the remote party.
                /// When based on <see cref="PipeOptions.PauseWriterThreshold"/>, this value should be -1 of that value in order
                /// to avoid the actual pause that would be fatal to the read loop of the multiplexing stream.
                /// </param>
                internal AcceptanceParameters(long? remoteWindowSize) => this.RemoteWindowSize = remoteWindowSize;

                /// <summary>
                /// Gets the maximum number of bytes that may be transmitted and not yet acknowledged as processed by the remote party.
                /// </summary>
                [DataMember]
                internal long? RemoteWindowSize { get; }
            }

            private class WindowPipeReader : PipeReader
            {
                private readonly Channel owner;
                private readonly PipeReader inner;
                private ReadResult lastReadResult;
                private long bytesProcessed;
                private SequencePosition lastExaminedPosition;

                internal WindowPipeReader(Channel owner, PipeReader inner)
                {
                    this.owner = owner;
                    this.inner = inner;
                }

                public override void AdvanceTo(SequencePosition consumed)
                {
                    long consumedBytes = this.Consumed(consumed, consumed);
                    this.inner.AdvanceTo(consumed);
                    this.owner.LocalContentExamined(consumedBytes);
                }

                public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
                {
                    long consumedBytes = this.Consumed(consumed, examined);
                    this.inner.AdvanceTo(consumed, examined);
                    this.owner.LocalContentExamined(consumedBytes);
                }

                public override void CancelPendingRead() => this.inner.CancelPendingRead();

                public override void Complete(Exception? exception = null) => this.inner.Complete(exception);

                public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
                {
                    return this.lastReadResult = await this.inner.ReadAsync(cancellationToken).ConfigureAwait(false);
                }

                public override bool TryRead(out ReadResult readResult)
                {
                    bool result = this.inner.TryRead(out readResult);
                    this.lastReadResult = readResult;
                    return result;
                }

                public override ValueTask CompleteAsync(Exception? exception = null) => this.inner.CompleteAsync(exception);

                public override Task CopyToAsync(PipeWriter destination, CancellationToken cancellationToken = default) => this.inner.CopyToAsync(destination, cancellationToken);

                public override Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default) => this.inner.CopyToAsync(destination, cancellationToken);

                [Obsolete]
                public override void OnWriterCompleted(Action<Exception?, object?> callback, object? state) => this.inner.OnWriterCompleted(callback, state);

                private long Consumed(SequencePosition consumed, SequencePosition examined)
                {
                    var lastExamined = this.lastExaminedPosition;
                    if (lastExamined.Equals(default))
                    {
                        lastExamined = this.lastReadResult.Buffer.Start;
                    }

                    // If the entirety of the buffer was examined for the first time, just use the buffer length as a perf optimization.
                    // Otherwise, slice the buffer from last examined to new examined to get the number of freshly examined bytes.
                    long bytesJustProcessed =
                        lastExamined.Equals(this.lastReadResult.Buffer.Start) && this.lastReadResult.Buffer.End.Equals(examined) ? this.lastReadResult.Buffer.Length :
                        this.lastReadResult.Buffer.Slice(lastExamined, examined).Length;

                    this.bytesProcessed += bytesJustProcessed;

                    // Only send the 'more bytes please' message if we've consumed at least a max frame's worth of data
                    // or if our reader indicates that more data is required before it will examine any more.
                    // Or in some cases of very small receiving windows, when the entire window is empty.
                    long result = 0;
                    if (this.bytesProcessed >= FramePayloadMaxLength || this.bytesProcessed == this.owner.localWindowSize)
                    {
                        result = this.bytesProcessed;
                        this.bytesProcessed = 0;
                    }

                    // Only store the examined position if it is ahead of the consumed position.
                    // Otherwise we'd store a position in an array that may be recycled.
                    this.lastExaminedPosition = consumed.Equals(examined) ? default : examined;

                    return result;
                }
            }
        }
    }
}
