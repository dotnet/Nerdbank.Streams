// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Diagnostics;

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
        }
    }
}
