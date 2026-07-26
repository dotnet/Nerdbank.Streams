// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System;
    using System.Buffers;
    using System.IO;
    using System.IO.Pipelines;
    using System.Threading;
    using System.Threading.Tasks;
    using MessagePack;
    using Microsoft;
    using Microsoft.VisualStudio.Threading;

    /// <content>
    /// Contains the <see cref="Formatter" /> nested type.
    /// </content>
    public partial class MultiplexingStream
    {
        internal abstract class Formatter : System.IAsyncDisposable
        {
            protected Formatter(PipeWriter writer)
            {
                this.PipeWriter = writer;
            }

            /// <summary>
            /// Gets or sets the result of the handshake regarding whether this endpoint is the "odd" one.
            /// </summary>
            /// <remarks>
            /// Only applicable and set in v1 and v2 of the protocol.
            /// </remarks>
            protected bool? IsOddEndpoint { get; set; }

            protected PipeWriter PipeWriter { get; }

            protected bool IsDisposed { get; private set; }

            public virtual ValueTask DisposeAsync()
            {
                if (this.IsDisposed)
                {
                    return default;
                }

                this.IsDisposed = true;
                return this.PipeWriter.CompleteAsync();
            }

            /// <summary>
            /// Writes the initial handshake.
            /// </summary>
            /// <returns>An object that is passed to <see cref="ReadHandshakeAsync(object, Options, CancellationToken)"/> as the first parameter.</returns>
            internal abstract object? WriteHandshake();

            /// <summary>
            /// Reads the initial handshake.
            /// </summary>
            /// <param name="writeHandshakeResult">The result of the <see cref="WriteHandshake"/> call.</param>
            /// <param name="options">Configuration settings that should be shared with the remote party.</param>
            /// <param name="cancellationToken">A cancellation token.</param>
            /// <returns>The result of the handshake.</returns>
            internal abstract Task<(bool? IsOdd, Version ProtocolVersion)> ReadHandshakeAsync(object? writeHandshakeResult, Options options, CancellationToken cancellationToken);

            internal abstract void WriteFrame(FrameHeader header, ReadOnlySequence<byte> payload);

            internal abstract Task<(FrameHeader Header, ReadOnlySequence<byte> Payload)?> ReadFrameAsync(CancellationToken cancellationToken);

            internal ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken)
            {
                return this.PipeWriter.FlushAsync(cancellationToken);
            }

            internal abstract ReadOnlySequence<byte> Serialize(Channel.OfferParameters offerParameters);

            internal abstract Channel.OfferParameters DeserializeOfferParameters(ReadOnlySequence<byte> payload);

            internal abstract ReadOnlySequence<byte> Serialize(Channel.AcceptanceParameters acceptanceParameters);

            internal abstract Channel.AcceptanceParameters DeserializeAcceptanceParameters(ReadOnlySequence<byte> payload);

            internal abstract long DeserializeContentProcessed(ReadOnlySequence<byte> payload);

            internal abstract ReadOnlySequence<byte> SerializeContentProcessed(long bytesProcessed);

            protected static bool IsOdd(ReadOnlySpan<byte> localRandomBuffer, ReadOnlySpan<byte> remoteRandomBuffer)
            {
                bool? isOdd = null;
                for (int i = 0; i < localRandomBuffer.Length; i++)
                {
                    byte sent = localRandomBuffer[i];
                    byte recv = remoteRandomBuffer[i];
                    if (sent > recv)
                    {
                        isOdd = true;
                        break;
                    }
                    else if (sent < recv)
                    {
                        isOdd = false;
                        break;
                    }
                }

                if (!isOdd.HasValue)
                {
                    throw new MultiplexingProtocolException("Unable to determine even/odd party.");
                }

                return isOdd.Value;
            }

            protected FrameHeader CreateFrameHeader(ControlCode code, ulong? channelId, ChannelSource? channelSource)
            {
                QualifiedChannelId? qualifiedId = null;
                if (channelId.HasValue)
                {
                    if (!channelSource.HasValue)
                    {
                        Assumes.True(this.IsOddEndpoint.HasValue);
                        bool channelIsOdd = channelId.Value % 2 == 1;

                        // Remember that this is from the remote sender's point of view.
                        channelSource = channelIsOdd == this.IsOddEndpoint.Value ? ChannelSource.Remote : ChannelSource.Local;
                    }

                    qualifiedId = new QualifiedChannelId(channelId.Value, channelSource.Value);
                }

                return new FrameHeader
                {
                    Code = code,
                    ChannelId = qualifiedId,
                };
            }
        }

        internal class V1Formatter : Formatter
        {
            /// <summary>
            /// The magic number to send at the start of communication when using v1 of the protocol.
            /// </summary>
            private static readonly byte[] ProtocolMagicNumber = new byte[] { 0x2f, 0xdf, 0x1d, 0x50 };
            private static readonly Version ProtocolVersion = new Version(1, 0);
            private readonly Stream readingStream;
            private readonly AsyncSemaphore readingSemaphore = new AsyncSemaphore(1);
            private readonly Memory<byte> headerBuffer = new byte[HeaderLength];
            private readonly Memory<byte> payloadBuffer = new byte[FramePayloadMaxLength];

            internal V1Formatter(PipeWriter writer, Stream readingStream)
                : base(writer)
            {
                this.readingStream = readingStream;
            }

            private static int HeaderLength => sizeof(ControlCode) + sizeof(int) + sizeof(short);

            public override async ValueTask DisposeAsync()
            {
                if (this.IsDisposed)
                {
                    return;
                }

                // Take care to never dispose the Stream while we're reading it,
                // since that can lead to memory corruption and other random exceptions.
                using (await this.readingSemaphore.EnterAsync().ConfigureAwait(false))
                {
                    this.readingStream.Dispose();
                }

                await base.DisposeAsync().ConfigureAwait(false);
            }

            internal override object WriteHandshake()
            {
                byte[]? randomSendBuffer = Guid.NewGuid().ToByteArray();
                Span<byte> span = this.PipeWriter.GetSpan(ProtocolMagicNumber.Length + randomSendBuffer.Length);

                ProtocolMagicNumber.CopyTo(span);
                randomSendBuffer.CopyTo(span.Slice(ProtocolMagicNumber.Length));

                this.PipeWriter.Advance(ProtocolMagicNumber.Length + randomSendBuffer.Length);
                return randomSendBuffer;
            }

            internal override async Task<(bool? IsOdd, Version ProtocolVersion)> ReadHandshakeAsync(object? writeHandshakeResult, Options options, CancellationToken cancellationToken)
            {
                byte[]? randomSendBuffer = writeHandshakeResult as byte[] ?? throw new ArgumentException("This should be the result of a prior call to " + nameof(this.WriteHandshake), nameof(writeHandshakeResult));

                byte[]? handshakeBytes = new byte[ProtocolMagicNumber.Length + 16];
                using (await this.readingSemaphore.EnterAsync(cancellationToken).ConfigureAwait(false))
                {
                    await this.readingStream.ReadBlockOrThrowAsync(handshakeBytes, cancellationToken).ConfigureAwait(false);
                }

                // Verify that the magic number matches.
                for (int i = 0; i < ProtocolMagicNumber.Length; i++)
                {
                    if (handshakeBytes[i] != ProtocolMagicNumber[i])
                    {
                        throw new MultiplexingProtocolException("Handshake magic number mismatch.");
                    }
                }

                this.IsOddEndpoint = IsOdd(randomSendBuffer, handshakeBytes.AsSpan(ProtocolMagicNumber.Length));
                return (this.IsOddEndpoint, ProtocolVersion);
            }

            internal override void WriteFrame(FrameHeader header, ReadOnlySequence<byte> payload)
            {
                Verify.NotDisposed(!this.IsDisposed, this);

                Span<byte> span = this.PipeWriter.GetSpan(checked(HeaderLength + (int)payload.Length));
                span[0] = (byte)header.Code;
                Utilities.Write(span.Slice(1, 4), checked((int)(header.ChannelId?.Id ?? 0)));
                Utilities.Write(span.Slice(5, 2), (ushort)payload.Length);

                span = span.Slice(HeaderLength);
                foreach (ReadOnlyMemory<byte> segment in payload)
                {
                    segment.Span.CopyTo(span);
                    span = span.Slice(segment.Length);
                }

                this.PipeWriter.Advance(HeaderLength + (int)payload.Length);
            }

            internal override async Task<(FrameHeader, ReadOnlySequence<byte>)?> ReadFrameAsync(CancellationToken cancellationToken)
            {
                using (await this.readingSemaphore.EnterAsync(cancellationToken).ConfigureAwait(false))
                {
                    Verify.NotDisposed(!this.IsDisposed, this);
                    if (!await ReadToFillAsync(this.readingStream, this.headerBuffer, throwOnEmpty: false, cancellationToken).ConfigureAwait(false))
                    {
                        return null;
                    }

                    FrameHeader header = this.CreateFrameHeader(
                        (ControlCode)this.headerBuffer.Span[0],
                        checked((ulong)Utilities.ReadInt(this.headerBuffer.Span.Slice(1, 4))),
                        null);

                    int framePayloadLength = Utilities.ReadInt(this.headerBuffer.Span.Slice(5, 2));
                    Memory<byte> payloadBuffer = this.payloadBuffer.Slice(0, framePayloadLength);
                    await ReadToFillAsync(this.readingStream, payloadBuffer, throwOnEmpty: true, cancellationToken).ConfigureAwait(false);
                    return (header, new ReadOnlySequence<byte>(payloadBuffer));
                }
            }

            internal override long DeserializeContentProcessed(ReadOnlySequence<byte> payload)
            {
                return Utilities.ReadInt(payload.IsSingleSegment ? payload.First.Span : payload.ToArray());
            }

            internal override unsafe ReadOnlySequence<byte> Serialize(Channel.OfferParameters offerParameters)
            {
                var sequence = new Sequence<byte>();
                Span<byte> buffer = sequence.GetSpan(ControlFrameEncoding.GetMaxByteCount(offerParameters.Name.Length));
                fixed (byte* pBuffer = buffer)
                {
                    fixed (char* pName = offerParameters.Name)
                    {
                        int byteLength = ControlFrameEncoding.GetBytes(pName, offerParameters.Name.Length, pBuffer, buffer.Length);
                        sequence.Advance(byteLength);
                    }
                }

                return sequence;
            }

            internal override unsafe Channel.OfferParameters DeserializeOfferParameters(ReadOnlySequence<byte> payload)
            {
                ReadOnlySpan<byte> nameSlice = payload.IsSingleSegment ? payload.First.Span : payload.ToArray();
                fixed (byte* pName = nameSlice)
                {
                    return new Channel.OfferParameters(
                        pName != null ? ControlFrameEncoding.GetString(pName, nameSlice.Length) : string.Empty,
                        null);
                }
            }

            internal override ReadOnlySequence<byte> Serialize(Channel.AcceptanceParameters acceptanceParameters)
            {
                return default;
            }

            internal override Channel.AcceptanceParameters DeserializeAcceptanceParameters(ReadOnlySequence<byte> payload)
            {
                return new Channel.AcceptanceParameters(null);
            }

            internal override ReadOnlySequence<byte> SerializeContentProcessed(long bytesProcessed)
            {
                throw new NotSupportedException();
            }
        }

        internal class V2Formatter : Formatter
        {
            private static readonly Version ProtocolVersion = new Version(2, 0);
            private readonly MessagePackStreamReader reader;
            private readonly AsyncSemaphore readingSemaphore = new AsyncSemaphore(1);

            internal V2Formatter(PipeWriter writer, Stream readingStream)
                : base(writer)
            {
                this.reader = new MessagePackStreamReader(readingStream, leaveOpen: false);
            }

            public override async ValueTask DisposeAsync()
            {
                if (this.IsDisposed)
                {
                    return;
                }

                // Take care to never dispose the MessagePackStreamReader while we're reading it,
                // since that can lead to memory corruption and other random exceptions.
                using (await this.readingSemaphore.EnterAsync().ConfigureAwait(false))
                {
                    this.reader.Dispose();
                }

                await base.DisposeAsync().ConfigureAwait(false);
            }

            internal override object? WriteHandshake()
            {
                var writer = new MessagePackWriter(this.PipeWriter);

                // Announce how many elements we'll be writing out.
                writer.WriteArrayHeader(2);

                // Send the protocol version (counting as one element in the outer array).
                writer.WriteArrayHeader(2);
                writer.Write(ProtocolVersion.Major);
                writer.Write(ProtocolVersion.Minor);

                // Send a random number to establish even/odd assignments.
                byte[]? randomSendBuffer = Guid.NewGuid().ToByteArray();
                writer.Write(randomSendBuffer);

                writer.Flush();

                return randomSendBuffer;
            }

            internal override async Task<(bool? IsOdd, Version ProtocolVersion)> ReadHandshakeAsync(object? writeHandshakeResult, Options options, CancellationToken cancellationToken)
            {
                using (await this.readingSemaphore.EnterAsync(cancellationToken).ConfigureAwait(false))
                {
                    byte[]? randomSendBuffer = writeHandshakeResult as byte[] ?? throw new ArgumentException("This should be the result of a prior call to " + nameof(this.WriteHandshake), nameof(writeHandshakeResult));

                    ReadOnlySequence<byte>? msgpackSequence = await this.reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    if (msgpackSequence is null)
                    {
                        throw new EndOfStreamException();
                    }

                    (bool IsOdd, Version ProtocolVersion) result = DeserializeHandshake(randomSendBuffer, msgpackSequence.Value, options);
                    this.IsOddEndpoint = result.IsOdd;
                    return result;
                }
            }

            internal override void WriteFrame(FrameHeader header, ReadOnlySequence<byte> payload)
            {
                Verify.NotDisposed(!this.IsDisposed, this);

                var writer = new MessagePackWriter(this.PipeWriter);

                int elementCount = !payload.IsEmpty ? 3 : header.ChannelId.HasValue ? 2 : 1;
                writer.WriteArrayHeader(elementCount);

                writer.Write((int)header.Code);
                if (elementCount > 1)
                {
                    if (header.ChannelId.HasValue)
                    {
                        writer.Write(header.ChannelId.Value.Id);
                    }
                    else
                    {
                        writer.WriteNil();
                    }

                    if (elementCount > 2)
                    {
                        writer.Write(payload);
                    }
                }

                writer.Flush();
            }

            internal override async Task<(FrameHeader Header, ReadOnlySequence<byte> Payload)?> ReadFrameAsync(CancellationToken cancellationToken)
            {
                using (await this.readingSemaphore.EnterAsync(cancellationToken).ConfigureAwait(false))
                {
                    Verify.NotDisposed(!this.IsDisposed, this);
                    ReadOnlySequence<byte>? frameSequence = await this.reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    if (frameSequence is null)
                    {
                        return null;
                    }

                    return this.DeserializeFrame(frameSequence.Value);
                }
            }

            /// <summary>
            /// Creates a payload for a <see cref="ControlCode.ChannelTerminated"/> frame.
            /// </summary>
            /// <param name="exception">The exception to send to the remote side if there is one.</param>
            /// <returns>The payload to send when a channel gets terminated.</returns>
            internal ReadOnlySequence<byte> SerializeException(Exception? exception)
            {
                if (exception == null)
                {
                    return ReadOnlySequence<byte>.Empty;
                }

                var sequence = new Sequence<byte>();
                var writer = new MessagePackWriter(sequence);

                writer.WriteArrayHeader(1);

                // Get the exception to send to the remote side
                writer.Write($"{exception.GetType().Name}: {exception.Message}");
                writer.Flush();

                return sequence;
            }

            /// <summary>
            /// Gets the error message in the payload if there is one.
            /// </summary>
            /// <param name="payload">The payload that could contain an error message.</param>
            /// <returns>The error message in this payload if there is one, null otherwise.</returns>
            internal Exception? DeserializeException(ReadOnlySequence<byte> payload)
            {
                // An empty payload means the remote side closed the channel without an exception.
                if (payload.IsEmpty)
                {
                    return null;
                }

                var reader = new MessagePackReader(payload);
                int numElements = reader.ReadArrayHeader();

                // We received an empty payload.
                if (numElements == 0)
                {
                    return null;
                }

                // Get the exception message and return it as an exception.
                string remoteErrorMsg = reader.ReadString();
                return new MultiplexingProtocolException($"Received error from remote side: {remoteErrorMsg}");
            }

            internal override ReadOnlySequence<byte> SerializeContentProcessed(long bytesProcessed)
            {
                var sequence = new Sequence<byte>();
                var writer = new MessagePackWriter(sequence);
                writer.WriteArrayHeader(1);
                writer.Write(bytesProcessed);
                writer.Flush();
                return sequence;
            }

            internal override long DeserializeContentProcessed(ReadOnlySequence<byte> payload)
            {
                var reader = new MessagePackReader(payload);
                reader.ReadArrayHeader();
                return reader.ReadInt64();
            }

            internal override ReadOnlySequence<byte> Serialize(Channel.OfferParameters offerParameters)
            {
                var sequence = new Sequence<byte>();
                var writer = new MessagePackWriter(sequence);

                writer.WriteArrayHeader(offerParameters.RemoteWindowSize is null ? 1 : 2);
                writer.Write(offerParameters.Name);
                if (offerParameters.RemoteWindowSize is long remoteWindowSize)
                {
                    writer.Write(remoteWindowSize);
                }

                writer.Flush();
                return sequence;
            }

            internal override Channel.OfferParameters DeserializeOfferParameters(ReadOnlySequence<byte> payload)
            {
                var reader = new MessagePackReader(payload);
                int elementsCount = reader.ReadArrayHeader();
                if (elementsCount == 0)
                {
                    throw new MultiplexingProtocolException("Insufficient elements in offer parameter payload.");
                }

                string name = reader.ReadString();
                long? remoteWindowSize = null;
                if (elementsCount > 1)
                {
                    remoteWindowSize = reader.ReadInt64();
                }

                return new Channel.OfferParameters(name, remoteWindowSize);
            }

            internal override ReadOnlySequence<byte> Serialize(Channel.AcceptanceParameters acceptanceParameters)
            {
                var sequence = new Sequence<byte>();
                var writer = new MessagePackWriter(sequence);

                if (acceptanceParameters.RemoteWindowSize is long remoteWindowSize)
                {
                    writer.WriteArrayHeader(1);
                    writer.Write(remoteWindowSize);
                }
                else
                {
                    writer.WriteArrayHeader(0);
                }

                writer.Flush();
                return sequence;
            }

            internal override Channel.AcceptanceParameters DeserializeAcceptanceParameters(ReadOnlySequence<byte> payload)
            {
                var reader = new MessagePackReader(payload);

                int elementsCount = reader.ReadArrayHeader();
                long? remoteWindowSize = null;
                if (elementsCount > 0)
                {
                    remoteWindowSize = reader.ReadInt64();
                }

                return new Channel.AcceptanceParameters(remoteWindowSize);
            }

            protected virtual (FrameHeader Header, ReadOnlySequence<byte> Payload) DeserializeFrame(ReadOnlySequence<byte> frameSequence)
            {
                var reader = new MessagePackReader(frameSequence);
                int headerElementCount = reader.ReadArrayHeader();
                if (headerElementCount < 1)
                {
                    throw new MultiplexingProtocolException("Not enough elements in frame header.");
                }

                FrameHeader header;
                var code = (ControlCode)reader.ReadInt32();
                ulong? channelId = null;
                if (headerElementCount > 1)
                {
                    if (reader.IsNil)
                    {
                        reader.ReadNil();
                    }
                    else
                    {
                        channelId = reader.ReadUInt64();
                    }

                    if (headerElementCount > 2)
                    {
                        ReadOnlySequence<byte> payload = reader.ReadBytes() ?? default;
                        header = this.CreateFrameHeader(code, channelId, null);
                        return (header, payload);
                    }
                }

                header = this.CreateFrameHeader(code, channelId, null);
                return (header, default);
            }

            private static void Discard(ref MessagePackReader reader, int elementsToDiscard)
            {
                for (int i = 0; i < elementsToDiscard; i++)
                {
                    reader.Skip();
                }
            }

            private static (bool IsOdd, Version ProtocolVersion) DeserializeHandshake(ReadOnlySpan<byte> localRandomNumber, ReadOnlySequence<byte> handshakeSequence, Options options)
            {
                var reader = new MessagePackReader(handshakeSequence);

                int elementCount = reader.ReadArrayHeader();
                if (elementCount < 2)
                {
                    throw new MultiplexingProtocolException("Unexpected handshake.");
                }

                int versionElementCount = reader.ReadArrayHeader();
                if (versionElementCount < 2)
                {
                    throw new MultiplexingProtocolException("Too few elements in handshake.");
                }

                int versionMajor = reader.ReadInt32();
                int versionMinor = reader.ReadInt32();
                var remoteVersion = new Version(versionMajor, versionMinor);
                Discard(ref reader, versionElementCount - 2);

                if (remoteVersion.Major != ProtocolVersion.Major)
                {
                    throw new MultiplexingProtocolException($"Incompatible version. Local version: {ProtocolVersion}. Remote version: {remoteVersion}.");
                }

                byte[]? remoteRandomNumber = reader.ReadBytes()?.ToArray();
                if (remoteRandomNumber is null)
                {
                    throw new MultiplexingProtocolException("Missing random number.");
                }

                bool isOdd = IsOdd(localRandomNumber, remoteRandomNumber);
                Discard(ref reader, elementCount - 2);

                return (isOdd, remoteVersion);
            }
        }

        internal class V3Formatter : V2Formatter
        {
            private static readonly Version ProtocolVersion = new Version(3, 0);
            private static readonly Task<(bool?, Version)> ReadHandshakeResult = Task.FromResult<(bool?, Version)>((null, ProtocolVersion));

            internal V3Formatter(PipeWriter writer, Stream readingStream)
                : base(writer, readingStream)
            {
            }

            internal override object? WriteHandshake() => null;

            internal override Task<(bool? IsOdd, Version ProtocolVersion)> ReadHandshakeAsync(object? writeHandshakeResult, Options options, CancellationToken cancellationToken)
            {
                return ReadHandshakeResult;
            }

            internal override void WriteFrame(FrameHeader header, ReadOnlySequence<byte> payload)
            {
                Verify.NotDisposed(!this.IsDisposed, this);

                var writer = new MessagePackWriter(this.PipeWriter);

                int elementCount = !payload.IsEmpty ? 4 : header.ChannelId.HasValue ? 3 : 1;
                writer.WriteArrayHeader(elementCount);

                writer.Write((int)header.Code);
                if (elementCount > 1)
                {
                    if (header.ChannelId is { } channelId)
                    {
                        writer.Write(channelId.Id);
                        writer.Write((sbyte)channelId.Source);
                    }
                    else
                    {
                        throw new NotSupportedException("A frame may not contain payload without a channel ID.");
                    }

                    if (!payload.IsEmpty)
                    {
                        writer.Write(payload);
                    }
                }

                writer.Flush();
            }

            protected override (FrameHeader Header, ReadOnlySequence<byte> Payload) DeserializeFrame(ReadOnlySequence<byte> frameSequence)
            {
                var reader = new MessagePackReader(frameSequence);
                int headerElementCount = reader.ReadArrayHeader();
                var header = default(FrameHeader);
                if (headerElementCount < 1)
                {
                    throw new MultiplexingProtocolException("Not enough elements in frame header.");
                }

                header.Code = (ControlCode)reader.ReadInt32();
                if (headerElementCount > 1)
                {
                    if (!reader.TryReadNil())
                    {
                        if (headerElementCount < 3)
                        {
                            throw new MultiplexingProtocolException("Not enough elements in frame header.");
                        }

                        header.ChannelId = new QualifiedChannelId(
                            reader.ReadUInt64(),
                            (ChannelSource)reader.ReadSByte());
                    }

                    if (headerElementCount > 3)
                    {
                        ReadOnlySequence<byte> payload = reader.ReadBytes() ?? default;
                        return (header, payload);
                    }
                }

                return (header, default);
            }
        }
    }
}
