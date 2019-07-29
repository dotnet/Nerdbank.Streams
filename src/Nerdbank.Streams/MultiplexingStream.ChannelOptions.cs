// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

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
            private IDuplexPipe existingPipe;

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
            public TraceSource TraceSource { get; set; }

            /// <summary>
            /// Gets or sets an existing <see cref="IDuplexPipe"/> instance used to exchange data with the channel.
            /// </summary>
            /// <value>The default is <c>null</c>.</value>
            /// <remarks>
            /// This property supports high throughput scenarios where channel data ultimately goes to a <see cref="PipeWriter"/> and <see cref="PipeReader"/> that already exist.
            /// This removes the need for a memory copy of all bytes transferred over the channel by directing the <see cref="MultiplexingStream"/> to read and write directly an existing reader/writer pair.
            /// When set, the <see cref="Channel.Input"/> and <see cref="Channel.Output"/> properties will throw <see cref="NotSupportedException"/>
            /// since their values are implementation details of the existing pipe set here.
            /// </remarks>
            /// <exception cref="ArgumentException">Thrown if set to an <see cref="IDuplexPipe"/> that returns <c>null</c> for either of its properties.</exception>
            public IDuplexPipe ExistingPipe
            {
                get => this.existingPipe;
                set
                {
                    // If the value is actually an instance of IDuplexPipe, "snap" the input and output into our own type
                    // to ensure the values don't change in the future.
                    // If it's already a DuplexType instance, we know it's immutable and can trust it.
                    this.existingPipe = value == null || value is DuplexPipe ? value : new DuplexPipe(value.Input, value.Output);
                }
            }

            /// <summary>
            /// Gets or sets the options for the <see cref="Pipe"/> created to relay local reading from this channel.
            /// May be null. Will be ignored if <see cref="ExistingPipe"/> is not <c>null</c>.
            /// </summary>
            public PipeOptions InputPipeOptions { get; set; }
        }
    }
}
