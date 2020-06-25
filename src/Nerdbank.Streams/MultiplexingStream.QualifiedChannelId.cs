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
        /// The channel ID along with which party offered it.
        /// </summary>
        [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
        public struct QualifiedChannelId : IEquatable<QualifiedChannelId>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="QualifiedChannelId"/> struct.
            /// </summary>
            /// <param name="id">The channel id.</param>
            /// <param name="offeredLocally">a value indicating whether <see cref="Id"/> is referring to a channel that was originally offered by the local party.</param>
            public QualifiedChannelId(long id, bool offeredLocally)
            {
                this.Id = id;
                this.OfferedLocally = offeredLocally;
            }

            /// <summary>
            /// Gets the channel ID.
            /// </summary>
            public long Id { get; }

            /// <summary>
            /// Gets a value indicating whether <see cref="Id"/> is referring to a channel that was originally offered by the local party.
            /// </summary>
            public bool OfferedLocally { get; }

            internal string DebuggerDisplay => $"{this.Id} ({(this.OfferedLocally ? "local" : "remote")})";

            /// <inheritdoc/>
            public bool Equals(QualifiedChannelId other) => this.Id == other.Id && this.OfferedLocally == other.OfferedLocally;

            /// <inheritdoc/>
            public override bool Equals(object? obj) => obj is QualifiedChannelId other && this.Equals(other);

            /// <inheritdoc/>
            public override int GetHashCode() => unchecked((int)this.Id) * (this.OfferedLocally ? 1 : -1);

            /// <inheritdoc/>
            public override string ToString() => $"{this.Id} ({(this.OfferedLocally ? "local" : "remote")})";
        }
    }
}
