// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Diagnostics;
    using System.IO.Pipelines;

    /// <content>
    /// Contains the <see cref="ChannelOptions"/> nested type.
    /// </content>
    public partial class MultiplexingStream
    {
        /// <summary>
        /// Describes local treatment of a channel.
        /// </summary>
        public class ChannelOptions
        {
            /// <summary>
            /// Backing field for the <see cref="ExistingPipe"/> property.
            /// </summary>
            private IDuplexPipe? existingPipe;

            /// <summary>
            /// Initializes a new instance of the <see cref="ChannelOptions" /> class.
            /// </summary>
            public ChannelOptions()
            {
            }

            /// <summary>
            /// Gets or sets the mechanism used for tracing activity related to this channel.
            /// </summary>
            /// <value>The trace source. May be null.</value>
            public TraceSource? TraceSource { get; set; }

            /// <summary>
            /// Gets or sets an existing <see cref="IDuplexPipe"/> instance used to exchange data with the channel.
            /// </summary>
            /// <value>The default is <see langword="null"/>.</value>
            /// <remarks>
            /// <para>
            /// This property supports high throughput scenarios where channel data ultimately goes to a <see cref="PipeWriter"/> and <see cref="PipeReader"/> that already exist.
            /// This may remove the need for a memory copy of all bytes transferred over the channel by directing the <see cref="MultiplexingStream"/> to read and write directly an existing reader/writer pair.
            /// When set, the <see cref="Channel.Input"/> and <see cref="Channel.Output"/> properties will throw <see cref="NotSupportedException"/>
            /// since their values are implementation details of the existing pipe set here.
            /// </para>
            /// <para>
            /// The <see cref="PipeWriter"/> specified in <see cref="IDuplexPipe.Output"/> *must* be created with <see cref="PipeOptions.PauseWriterThreshold"/> that *exceeds*
            /// the value of <see cref="ChannelReceivingWindowSize"/> and <see cref="Options.DefaultChannelReceivingWindowSize"/>.
            /// </para>
            /// </remarks>
            /// <exception cref="ArgumentException">Thrown if set to an <see cref="IDuplexPipe"/> that returns <see langword="null"/> for either of its properties.</exception>
            public IDuplexPipe? ExistingPipe
            {
                get => this.existingPipe;
                set
                {
                    if (value != null && value.Input == null && value.Output == null)
                    {
                        throw new ArgumentException("At least a reader or writer must be specified.");
                    }

                    // If the value is actually an instance of IDuplexPipe, "snap" the input and output into our own type
                    // to ensure the values don't change in the future.
                    // If it's already a DuplexType instance, we know it's immutable and can trust it.
                    this.existingPipe = value == null || value is DuplexPipe ? value : new DuplexPipe(value.Input, value.Output);
                }
            }

            /// <summary>
            /// Gets or sets the options for the <see cref="Pipe"/> created to relay local reading from this channel.
            /// </summary>
            [Obsolete("This value is ignored.")]
            public PipeOptions? InputPipeOptions { get; set; }

            /// <summary>
            /// Gets or sets the number of received bytes that may be buffered locally per channel (transmitted from the remote party but not yet processed).
            /// </summary>
            /// <remarks>
            /// This value should be at least the value of <see cref="Options.DefaultChannelReceivingWindowSize"/> when the <see cref="MultiplexingStream"/> was created.
            /// When the value is null or less than <see cref="Options.DefaultChannelReceivingWindowSize"/>, the value from <see cref="Options.DefaultChannelReceivingWindowSize"/> is used.
            /// </remarks>
            public long? ChannelReceivingWindowSize { get; set; }
        }
    }
}
