// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.IO.Pipelines;
    using System.Threading;

    /// <summary>
    /// An exception thrown from <see cref="PipeReader.ReadAsync(CancellationToken)"/>
    /// when called on the <see cref="MultiplexingStream.Channel.Input"/> property from a
    /// <see cref="MultiplexingStream.Channel"/> whose remote counterpart completed
    /// their <see cref="MultiplexingStream.Channel.Output"/> with a fault
    /// using <see cref="PipeWriter.CompleteAsync(System.Exception?)"/>.
    /// </summary>
    [Serializable]
    public class RemoteChannelException : Exception
    {
        /// <summary>Initializes a new instance of the <see cref="RemoteChannelException"/> class.</summary>
        public RemoteChannelException()
        {
        }

        /// <summary>Initializes a new instance of the <see cref="RemoteChannelException"/> class.</summary>
        /// <inheritdoc cref="Exception(string)"/>
        public RemoteChannelException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="RemoteChannelException"/> class.</summary>
        /// <inheritdoc cref="Exception(string, Exception)"/>
        public RemoteChannelException(string message, Exception inner)
            : base(message, inner)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="RemoteChannelException"/> class.</summary>
        /// <inheritdoc cref="Exception(System.Runtime.Serialization.SerializationInfo, System.Runtime.Serialization.StreamingContext)"/>
        protected RemoteChannelException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context)
            : base(info, context)
        {
        }
    }
}
