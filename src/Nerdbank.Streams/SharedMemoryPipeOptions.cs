// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams;

/// <summary>
/// Options that control the behavior and resource allocation of a <see cref="SharedMemoryDuplexPipe"/>.
/// </summary>
public sealed class SharedMemoryPipeOptions
{
    private int capacity = 1024 * 1024;

    /// <summary>
    /// Gets or sets the size, in bytes, of the shared memory buffer in each direction.
    /// This limits how many bytes the writer can have sent that the reader has not yet consumed.
    /// Must be a power of two of at least 2. Default is 1 MB.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This does <em>not</em> limit message size. Most messages are serialized directly into shared memory without being copied.
    /// When a message is larger than the free space available, it is instead buffered in private memory
    /// and copied into shared memory in pieces as the reader frees up space, so the reader may receive it incrementally.
    /// </para>
    /// <para>
    /// When the writer gets ahead of the reader and the buffer fills, whether with one large message or many small ones,
    /// <see cref="System.IO.Pipelines.PipeWriter.FlushAsync(CancellationToken)"/> waits asynchronously (without spinning)
    /// until the reader consumes enough data to make room. No data is dropped.
    /// </para>
    /// <para>
    /// Each connection reserves about twice this many bytes of shared memory (one buffer per direction), shared by both processes.
    /// This reserves address space and, on Windows, counts toward the system commit limit, but the OS provides physical memory
    /// for a page only when it is first written. Whenever the reader has caught up, the writer starts again at the beginning of the buffer,
    /// so a connection only ever touches as much memory as it has had in flight at once.
    /// A generous capacity is therefore inexpensive unless it is actually filled.
    /// The memory is ordinary pageable memory: under memory pressure the OS may move it to the paging file or swap.
    /// </para>
    /// <para>
    /// Choose a size that comfortably fits your largest typical messages: larger values let more messages skip the extra copy and reduce
    /// how often the writer must wait for the reader.
    /// </para>
    /// </remarks>
    public int Capacity
    {
        get => this.capacity;
        set
        {
            if (value < 2 || (value & (value - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Capacity must be a power of two and at least 2 bytes.");
            }

            this.capacity = value;
        }
    }

    /// <summary>
    /// Gets or sets the signaling mechanism used to notify the peer of new data or released space.
    /// Default is <see cref="SharedMemorySignaling.Auto"/>.
    /// </summary>
    public SharedMemorySignaling Signaling { get; set; } = SharedMemorySignaling.Auto;

    /// <summary>
    /// Gets or sets the directory in which to create the file that backs the shared memory, on platforms other than Windows.
    /// When <see langword="null"/>, defaults to <c>/dev/shm</c> if available, or the system temporary directory.
    /// </summary>
    /// <remarks>
    /// This is ignored on Windows, where the shared memory is backed by the paging file and no file is created.
    /// Elsewhere the file is readable and writable only by the current user and is deleted as soon as the client opens it.
    /// </remarks>
    public string? BaseDirectory { get; set; }
}
