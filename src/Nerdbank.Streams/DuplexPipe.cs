// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.IO.Pipelines;

    /// <summary>
    /// A basic implementation of <see cref="IDuplexPipe"/>.
    /// </summary>
    public class DuplexPipe : IDuplexPipe
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DuplexPipe"/> class.
        /// </summary>
        /// <param name="input">The reader. Must not be null.</param>
        /// <param name="output">The writer. Must not be null.</param>
        public DuplexPipe(PipeReader input, PipeWriter output)
        {
            this.Input = input ?? throw new ArgumentNullException(nameof(input));
            this.Output = output ?? throw new ArgumentNullException(nameof(output));
        }

        /// <inheritdoc />
        public PipeReader Input { get; }

        /// <inheritdoc />
        public PipeWriter Output { get; }
    }
}
