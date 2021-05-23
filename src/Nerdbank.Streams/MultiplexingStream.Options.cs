// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Linq;
    using Microsoft;

    /// <content>
    /// Contains the <see cref="Options"/> nested type.
    /// </content>
    public partial class MultiplexingStream
    {
        /// <summary>
        /// Describes the options that a <see cref="MultiplexingStream"/> may be created with.
        /// </summary>
        public class Options
        {
            /// <summary>
            /// The default window size for a new channel,
            /// which also serves as the minimum window size for any channel.
            /// </summary>
            /// <remarks>
            /// Using an integer multiple of <see cref="FramePayloadMaxLength"/> ensures that the client can send full frames
            /// instead of ending with a partial frame when the remote window limit is reached.
            /// </remarks>
            private static readonly long RecommendedDefaultChannelReceivingWindowSize = 5 * FramePayloadMaxLength;

            /// <summary>
            /// Backing field for the <see cref="TraceSource"/> property.
            /// </summary>
            private TraceSource traceSource = new TraceSource(nameof(MultiplexingStream), SourceLevels.Critical);

            /// <summary>
            /// Backing field for the <see cref="ProtocolMajorVersion"/> property.
            /// </summary>
            private int protocolMajorVersion = 1;

            /// <summary>
            /// Backing field for the <see cref="DefaultChannelReceivingWindowSize"/> property.
            /// </summary>
            private long defaultChannelReceivingWindowSize = RecommendedDefaultChannelReceivingWindowSize;

            /// <summary>
            /// Backing field for the <see cref="DefaultChannelTraceSourceFactory"/> property.
            /// </summary>
            private Func<int, string, TraceSource?>? defaultChannelTraceSourceFactory;

            /// <summary>
            /// Backing field for the <see cref="DefaultChannelTraceSourceFactoryWithQualifier"/> property.
            /// </summary>
            private Func<QualifiedChannelId, string, TraceSource?>? defaultChannelTraceSourceFactoryWithQualifier;

            /// <summary>
            /// Backing field for the <see cref="StartSuspended"/> property.
            /// </summary>
            private bool startSuspended;

            /// <summary>
            /// Initializes a new instance of the <see cref="Options"/> class.
            /// </summary>
            public Options()
            {
                this.SeededChannels = new List<ChannelOptions>();
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="Options"/> class
            /// and copies the values from another instance into this one.
            /// </summary>
            /// <param name="copyFrom">The instance to copy values from.</param>
            public Options(Options copyFrom)
            {
                Requires.NotNull(copyFrom, nameof(copyFrom));

                this.defaultChannelReceivingWindowSize = copyFrom.defaultChannelReceivingWindowSize;
                this.traceSource = copyFrom.traceSource;
                this.protocolMajorVersion = copyFrom.protocolMajorVersion;
                this.defaultChannelTraceSourceFactory = copyFrom.defaultChannelTraceSourceFactory;
                this.defaultChannelTraceSourceFactoryWithQualifier = copyFrom.defaultChannelTraceSourceFactoryWithQualifier;
                this.startSuspended = copyFrom.startSuspended;
                this.SeededChannels = copyFrom.SeededChannels.ToList();
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="Options"/> class
            /// and copies the values from another instance into this one.
            /// </summary>
            /// <param name="copyFrom">The instance to copy values from.</param>
            /// <param name="frozen"><c>true</c> to freeze this copy.</param>
            private Options(Options copyFrom, bool frozen)
                : this(copyFrom)
            {
                this.IsFrozen = frozen;
                if (frozen)
                {
                    this.SeededChannels = new ReadOnlyCollection<ChannelOptions>(this.SeededChannels);
                }
            }

            /// <summary>
            /// Gets or sets the logger used by this instance.
            /// </summary>
            /// <value>Never null.</value>
            public TraceSource TraceSource
            {
                get => this.traceSource;
                set
                {
                    Requires.NotNull(value, nameof(value));
                    this.ThrowIfFrozen();
                    this.traceSource = value;
                }
            }

            /// <summary>
            /// Gets or sets the number of received bytes that may be buffered locally per channel (transmitted from the remote party but not yet processed).
            /// </summary>
            /// <value>
            /// Must be a positive value.
            /// </value>
            /// <exception cref="ArgumentOutOfRangeException">Thrown if set to a non-positive value.</exception>
            /// <remarks>
            /// This value is ignored when <see cref="ProtocolMajorVersion"/> is set to 1.
            /// </remarks>
            public long DefaultChannelReceivingWindowSize
            {
                get => this.defaultChannelReceivingWindowSize;
                set
                {
                    Requires.Range(value > 0, nameof(value));
                    this.ThrowIfFrozen();
                    this.defaultChannelReceivingWindowSize = value;
                }
            }

            /// <summary>
            /// Gets or sets the protocol version to be used.
            /// </summary>
            /// <value>The default is 1.</value>
            /// <remarks>
            /// 1 is the original and default version.
            /// 2 is a protocol breaking change and adds backpressure support.
            /// 3 is a protocol breaking change that removes the initial handshake so no round-trip to establish the connection is necessary.
            /// </remarks>
            public int ProtocolMajorVersion
            {
                get => this.protocolMajorVersion;
                set
                {
                    Requires.Range(value > 0, nameof(value));
                    this.ThrowIfFrozen();
                    this.protocolMajorVersion = value;
                }
            }

            /// <summary>
            /// Gets or sets a factory for <see cref="TraceSource"/> instances to attach to a newly opened <see cref="Channel"/>
            /// when its <see cref="ChannelOptions.TraceSource"/> is <c>null</c>.
            /// </summary>
            /// <remarks>
            /// <para>The delegate receives a channel ID and name, and may return a <see cref="TraceSource"/> or <c>null</c>.</para>
            /// <para>This delegate will not be invoked if <see cref="DefaultChannelTraceSourceFactoryWithQualifier"/> has been set to a non-null value.</para>
            /// </remarks>
            [Obsolete("Use " + nameof(DefaultChannelTraceSourceFactoryWithQualifier) + " instead.")]
            public Func<int, string, TraceSource?>? DefaultChannelTraceSourceFactory
            {
                get => this.defaultChannelTraceSourceFactory;
                set
                {
                    this.ThrowIfFrozen();
                    this.defaultChannelTraceSourceFactory = value;
                }
            }

            /// <summary>
            /// Gets or sets a factory for <see cref="TraceSource"/> instances to attach to a newly opened <see cref="Channel"/>
            /// when its <see cref="ChannelOptions.TraceSource"/> is <c>null</c>.
            /// </summary>
            /// <remarks>
            /// <para>The delegate receives a channel ID and name, and may return a <see cref="TraceSource"/> or <c>null</c>.</para>
            /// <para>This delegate supersedes the obsolete <see cref="DefaultChannelTraceSourceFactory"/> as this one provides detail about whether the channel was offered locally or remotely.</para>
            /// </remarks>
            public Func<QualifiedChannelId, string, TraceSource?>? DefaultChannelTraceSourceFactoryWithQualifier
            {
                get => this.defaultChannelTraceSourceFactoryWithQualifier;
                set
                {
                    this.ThrowIfFrozen();
                    this.defaultChannelTraceSourceFactoryWithQualifier = value;
                }
            }

            /// <summary>
            /// Gets or sets a value indicating whether the <see cref="MultiplexingStream"/> should <em>not</em> start
            /// reading from the stream immediately in order to provide time for the creator to add event handlers
            /// such as <see cref="ChannelOffered"/>.
            /// </summary>
            /// <value>The default value is <see langword="false"/>.</value>
            /// <remarks>
            /// When set to <see langword="true" />, the owner must use <see cref="StartListening"/>
            /// after attaching event handlers to actually being reading from the stream.
            /// </remarks>
            public bool StartSuspended
            {
                get => this.startSuspended;
                set
                {
                    this.ThrowIfFrozen();
                    this.startSuspended = value;
                }
            }

            /// <summary>
            /// Gets a list of options for channels that are to be "seeded" into a new <see cref="MultiplexingStream"/>.
            /// </summary>
            /// <remarks>
            /// Seeded channels avoid the need for a round-trip for an offer/accept packet exchange.
            /// Seeded channels are accessed within the <see cref="MultiplexingStream"/> instance by calling <see cref="AcceptChannel(ulong, ChannelOptions?)"/>
            /// with the 0-based index into this list used as the channel ID.
            /// They are only supported when <see cref="ProtocolMajorVersion"/> is at least 3.
            /// </remarks>
            public IList<ChannelOptions> SeededChannels { get; private set; }

            /// <summary>
            /// Gets a value indicating whether this instance is frozen.
            /// </summary>
            public bool IsFrozen { get; private set; }

            /// <summary>
            /// Returns a frozen instance of this object.
            /// </summary>
            /// <returns>This instance if already frozen, otherwise a frozen copy.</returns>
            public Options GetFrozenCopy() => this.IsFrozen ? this : new Options(this, frozen: true);

            private void ThrowIfFrozen() => Verify.Operation(!this.IsFrozen, Strings.Frozen);
        }
    }
}
