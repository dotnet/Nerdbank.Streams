// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams;

#if NET
using System.Runtime.Versioning;
#endif

/// <summary>
/// Specifies the signaling mechanism used to wake waiting peers in a shared-memory transport.
/// </summary>
public enum SharedMemorySignaling
{
    /// <summary>
    /// Automatically selects the best supported mechanism for the current operating system
    /// (<see cref="NamedEvent"/> on Windows, <see cref="Doorbell"/> elsewhere).
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Uses Windows named auto-reset events (<see cref="System.Threading.EventWaitHandle"/>).
    /// </summary>
#if NET
    [SupportedOSPlatform("windows")]
#endif
    NamedEvent = 1,

    /// <summary>
    /// Uses a 1-byte doorbell exchange over a duplex named pipe or Unix domain socket.
    /// </summary>
    Doorbell = 2,
}
