// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System.Diagnostics;
    using Microsoft;

    /// <summary>
    /// Describes the options that a <see cref="MultiplexingStream"/> may be created with.
    /// </summary>
    public class MultiplexingStreamOptions
    {
#if TRACESOURCE
        /// <summary>
        /// Backing field for the <see cref="TraceSource"/> property.
        /// </summary>
        private TraceSource traceSource = new TraceSource(nameof(MultiplexingStream), SourceLevels.Critical);
#endif

        /////// <summary>
        /////// Gets or sets the maximum number of channel offers from the remote party that are allowed before the
        /////// connection is terminated for abuse.
        /////// </summary>
        /////// <value>The default value is 100.</value>
        ////public int MaximumAllowedChannelOffers { get; set; } = 100;

#if TRACESOURCE
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
                this.traceSource = value;
            }
        }
#endif
    }
}
