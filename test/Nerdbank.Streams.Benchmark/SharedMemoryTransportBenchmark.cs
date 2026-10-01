// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams.Benchmark;

using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

/// <summary>Identifies the transport under test.</summary>
public enum TransportKind
{
    /// <summary>A managed in-process duplex pipe.</summary>
    InMemory,

    /// <summary>An OS named pipe with both endpoints in the same process.</summary>
    NamedPipe,

    /// <summary>Shared memory with cross-process-capable signaling.</summary>
    SharedMemory,
}

/// <summary>Compares round-trip transport overhead for an in-process pipe, an OS pipe and IPC shared memory.</summary>
/// <remarks>Both endpoints are in one process, but IPC shared memory uses the production listen/connect and wake-up path.</remarks>
[MemoryDiagnoser]
public class SharedMemoryTransportBenchmark
{
    private IDuplexPipe? client;
    private IDuplexPipe? server;
    private NamedPipeServerStream? pipeServer;
    private NamedPipeClientStream? pipeClient;
    private SharedMemoryDuplexPipe? memoryServer;
    private SharedMemoryDuplexPipe? memoryClient;
    private Task? responder;
    private byte[] payload = null!;

    /// <summary>Gets or sets the transport being measured.</summary>
#if NETFRAMEWORK
    [Params(TransportKind.InMemory, TransportKind.NamedPipe)]
#else
    [Params(TransportKind.InMemory, TransportKind.NamedPipe, TransportKind.SharedMemory)]
#endif
    public TransportKind Transport { get; set; }

    /// <summary>Gets or sets the bytes sent per request.</summary>
    [Params(64, 65536)]
    public int MessageSize { get; set; }

    /// <summary>Creates endpoints and validates a round trip before measuring.</summary>
    /// <returns>A task that completes when both endpoints are ready.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        this.payload = new byte[this.MessageSize];
        for (int i = 0; i < this.payload.Length; i++)
        {
            this.payload[i] = (byte)(i * 31);
        }

        (this.client, this.server) = this.Transport switch
        {
            TransportKind.InMemory => FullDuplexStream.CreatePipePair(),
            TransportKind.NamedPipe => await this.CreateNamedPipePairAsync().ConfigureAwait(false),
            TransportKind.SharedMemory => await this.CreateSharedMemoryPairAsync().ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(this.Transport)),
        };

        this.responder = Task.Run(this.RespondAsync);
        if (await this.RoundTripAsync().ConfigureAwait(false) != 1)
        {
            throw new InvalidDataException("Round trip did not return the expected acknowledgement.");
        }
    }

    /// <summary>Sends a message, then waits for acknowledgement after the peer consumes it.</summary>
    /// <returns>The received acknowledgement.</returns>
    [Benchmark]
    public async Task<byte> RoundTripAsync()
    {
        this.payload.CopyTo(this.client!.Output.GetSpan(this.payload.Length));
        this.client.Output.Advance(this.payload.Length);
        await this.client.Output.FlushAsync().ConfigureAwait(false);
        ReadResult result = await this.client.Input.ReadAsync().ConfigureAwait(false);
        if (result.Buffer.IsEmpty && result.IsCompleted)
        {
            throw new EndOfStreamException();
        }

        byte acknowledgement = result.Buffer.First.Span[0];
        this.client.Input.AdvanceTo(result.Buffer.GetPosition(1));
        return acknowledgement;
    }

    /// <summary>Closes the endpoints and the peer's response loop.</summary>
    /// <returns>A task that completes after cleanup.</returns>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        this.client?.Output.Complete();
        this.server?.Input.Complete();
        this.server?.Output.Complete();
        this.client?.Input.Complete();
        this.memoryClient?.Dispose();
        this.memoryServer?.Dispose();
        this.pipeClient?.Dispose();
        this.pipeServer?.Dispose();
        if (this.responder is not null)
        {
            await this.responder.ConfigureAwait(false);
        }
    }

    private async Task RespondAsync()
    {
        while (true)
        {
            int received = 0;
            while (received < this.MessageSize)
            {
                ReadResult result = await this.server!.Input.ReadAsync().ConfigureAwait(false);
                if (result.Buffer.IsEmpty && result.IsCompleted)
                {
                    return;
                }

                received += checked((int)result.Buffer.Length);
                this.server.Input.AdvanceTo(result.Buffer.End);
            }

            this.server!.Output.GetSpan(1)[0] = 1;
            this.server.Output.Advance(1);
            await this.server.Output.FlushAsync().ConfigureAwait(false);
        }
    }

    private async Task<(IDuplexPipe Client, IDuplexPipe Server)> CreateNamedPipePairAsync()
    {
        string name = $"streams-benchmark-{Guid.NewGuid():N}";
        this.pipeServer = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
        this.pipeClient = new NamedPipeClientStream(".", name, PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
        Task accept = this.pipeServer.WaitForConnectionAsync();
        await this.pipeClient.ConnectAsync().ConfigureAwait(false);
        await accept.ConfigureAwait(false);
        return (this.pipeClient.UsePipe(), this.pipeServer.UsePipe());
    }

    private async Task<(IDuplexPipe Client, IDuplexPipe Server)> CreateSharedMemoryPairAsync()
    {
        string name = Guid.NewGuid().ToString("N");
        Task<SharedMemoryDuplexPipe> accept = SharedMemoryDuplexPipe.ListenAsync(name);
        this.memoryClient = await SharedMemoryDuplexPipe.ConnectAsync(name).ConfigureAwait(false);
        this.memoryServer = await accept.ConfigureAwait(false);
        return (this.memoryClient, this.memoryServer);
    }
}
