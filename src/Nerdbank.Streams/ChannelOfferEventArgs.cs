// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Threading;

    /// <summary>
    /// Describes an offer for a channel.
    /// </summary>
    public class ChannelOfferEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the unique ID of the channel.
        /// </summary>
        public int ChannelId { get; }

        /// <summary>
        /// Gets the name of the channel.
        /// </summary>
        public string ChannelName { get; }

        /// <summary>
        /// Gets a value indicating whether the channel was accepted by a pending <see cref="MultiplexingStream.AcceptChannelAsync(string, MultiplexingStream.ChannelOptions, CancellationToken)"/>.
        /// </summary>
        public bool IsAccepted { get; }
    }
}
