// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

#if !TRACESOURCE

namespace Nerdbank.Streams
{
    /// <summary>
    /// A stand-in for null tracing on target frameworks that don't support it.
    /// </summary>
    internal class TraceSource
    {
        private TraceSource()
        {
        }
    }
}

#endif
