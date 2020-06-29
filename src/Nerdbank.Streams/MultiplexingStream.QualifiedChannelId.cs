// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Diagnostics;

    /// <content>
    /// Contains the <see cref="QualifiedChannelId"/> nested type.
    /// </content>
    public partial class MultiplexingStream
    {
        /// <summary>
        /// An enumeration of the possible sources of a channel.
        /// </summary>
        /// <remarks>
        /// The ordinal values are chosen so as to make flipping the perspective as easy as negating the value,
        /// while leaving the <see cref="Seeded"/> value unchanged.
        /// </remarks>
        public enum ChannelSource : sbyte
        {
            /// <summary>
            /// The channel was offered by this <see cref="MultiplexingStream"/> instance to the other party.
            /// </summary>
            Local = 1,

            /// <summary>
            /// The channel was offered to this <see cref="MultiplexingStream"/> instance by the other party.
            /// </summary>
            Remote = -1,

            /// <summary>
            /// The channel was seeded during construction via the <see cref="Options.SeededChannels"/> collection.
            /// This channel is to be <see cref="AcceptChannel(ulong, ChannelOptions?)">accepted</see> by both parties.
            /// </summary>
            Seeded = 0,
        }

        /// <summary>
        /// The channel ID along with which party offered it.
        /// </summary>
        [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
        public struct QualifiedChannelId : IEquatable<QualifiedChannelId>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="QualifiedChannelId"/> struct.
            /// </summary>
            /// <param name="id">The channel id.</param>
            /// <param name="source">A value indicating where the channel originated.</param>
            public QualifiedChannelId(ulong id, ChannelSource source)
            {
                this.Id = id;
                this.Source = source;
            }

            /// <summary>
            /// Gets the channel ID.
            /// </summary>
            public ulong Id { get; }

            /// <summary>
            /// Gets a value indicating where the channel originated.
            /// </summary>
            public ChannelSource Source { get; }

            internal string DebuggerDisplay => this.ToString();

            /// <inheritdoc/>
            public bool Equals(QualifiedChannelId other) => this.Id == other.Id && this.Source == other.Source;

            /// <inheritdoc/>
            public override bool Equals(object? obj) => obj is QualifiedChannelId other && this.Equals(other);

            /// <inheritdoc/>
            public override int GetHashCode() => unchecked((int)this.Id) | ((int)this.Source << 29);

            /// <inheritdoc/>
            public override string ToString() => $"{this.Id} ({this.Source})";
        }
    }
}
