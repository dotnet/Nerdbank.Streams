// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using MessagePack;

    /// <content>
    /// Contains the <see cref="WriteError"/> nested type.
    /// </content>
    public partial class MultiplexingStream
    {
        /// <summary>
        /// A class containing information about a write error and which is sent to the
        /// remote alongside <see cref="MultiplexingStream.ControlCode.ContentWritingError"/>.
        /// </summary>
        public class WriteError
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="WriteError"/> class.
            /// </summary>
            /// <param name="message">The error message we want to send to the receiver.</param>
            public WriteError(string message)
            {
                this.ErrorMessage = message;
            }

            /// <summary>
            /// Gets the error message that we want to send to receiver.
            /// </summary>
            public string ErrorMessage { get; }
        }
    }
}
