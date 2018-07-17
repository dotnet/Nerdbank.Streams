// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Pipelines;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft;
    using Microsoft.VisualStudio.Threading;

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
            public ChannelOptions();

            /// <summary>
            /// Gets a value indicating whether this instance is read only.
            /// </summary>
            /// <value><c>true</c> if this instance should not be changed; <c>false</c> otherwise.</value>
            public bool IsReadOnly { get; }

            /// <summary>
            /// Gets or sets the priority to give this channel. Larger positive values merit higher priority.
            /// </summary>
            /// <exception cref="InvalidOperationException">Thrown if changed when <see cref="IsReadOnly" /> is true.</exception>
            public int ChannelPriority { get; set; }

            /// <summary>
            /// Gets or sets the mechanism used for tracing activity related to this channel.
            /// </summary>
            /// <value>The trace source. May be null.</value>
            /// <exception cref="InvalidOperationException">Thrown if changed when <see cref="IsReadOnly" /> is true.</exception>
            public TraceSource TraceSource { get; set; }
        }
    }
}
