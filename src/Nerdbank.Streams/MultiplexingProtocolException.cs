// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;

    /// <summary>
    /// An exception that is thrown when an error occurs on the remote side of a multiplexed connection.
    /// </summary>
#if NETFRAMEWORK || NETSTANDARD2_0
    [System.Serializable]
#endif
    public class MultiplexingProtocolException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiplexingProtocolException"/> class.
        /// </summary>
        public MultiplexingProtocolException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiplexingProtocolException"/> class.
        /// </summary>
        /// <param name="message">The message for the exception.</param>
        public MultiplexingProtocolException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiplexingProtocolException"/> class.
        /// </summary>
        /// <param name="message">The message for the exception.</param>
        /// <param name="inner">The inner exception.</param>
        public MultiplexingProtocolException(string message, Exception inner)
            : base(message, inner)
        {
        }

#if NETFRAMEWORK || NETSTANDARD2_0
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiplexingProtocolException"/> class
        /// for use in deserialization.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The serialization context.</param>
        protected MultiplexingProtocolException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }
}
