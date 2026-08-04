// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using Nerdbank.Streams;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

[Obsolete("Tests functionality that .NET now exposes directly through PipeReader.Create(Stream)")]
public class StreamUseStrictPipeReaderTests : StreamPipeReaderTestBase
{
    public StreamUseStrictPipeReaderTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public async Task StreamFails()
    {
        var expectedException = new InvalidOperationException();
        Stream unreadableStream = Substitute.For<Stream>();
        unreadableStream.CanRead.Returns(true);

        // Set up for either ReadAsync method to be called. We expect it will be Memory<T> on .NET Core 2.1 and byte[] on all the others.
#if SPAN_BUILTIN
        unreadableStream.ReadAsync(default, CancellationToken.None).ThrowsAsyncForAnyArgs(expectedException);
#else
        unreadableStream.ReadAsync(null, 0, 0, CancellationToken.None).ThrowsAsyncForAnyArgs(expectedException);
#endif

        PipeReader? reader = this.CreatePipeReader(unreadableStream);
        InvalidOperationException? actualException = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(this.TimeoutToken).AsTask());
        Assert.Same(expectedException, actualException);
    }

    [Fact]
    public void Read()
    {
        MemoryStream ms = new(new byte[] { 1, 2, 3 });
        var reader = (StreamPipeReader)this.CreatePipeReader(ms);
        ReadResult result = reader.Read();
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Buffer.ToArray());
        Assert.False(result.IsCompleted);
        reader.AdvanceTo(result.Buffer.GetPosition(1));
        result = reader.Read();
        Assert.Equal(new byte[] { 2, 3 }, result.Buffer.ToArray());
        Assert.False(result.IsCompleted);
        reader.AdvanceTo(result.Buffer.End);
        result = reader.Read();
        Assert.Equal(0, result.Buffer.Length);
        Assert.True(result.IsCompleted);
    }

    [Fact]
    public async Task ReadAsync_NonCancellableCaller_ReusesReaderCancellationToken()
    {
        var stream = new RecordingReadStream(blockReads: false);
        var reader = new StreamPipeReader(stream, bufferSize: 1, leaveOpen: true);

        ReadResult result = await reader.ReadAsync(CancellationToken.None);
        reader.AdvanceTo(result.Buffer.End);
        result = await reader.ReadAsync(CancellationToken.None);
        reader.AdvanceTo(result.Buffer.End);

        Assert.Equal(2, stream.ReadCancellationTokens.Count);
        Assert.True(stream.ReadCancellationTokens[0].CanBeCanceled);
        Assert.Equal(stream.ReadCancellationTokens[0], stream.ReadCancellationTokens[1]);
        reader.Complete();
    }

    [Fact]
    public async Task ReadAsync_NonCancellableCaller_ObservesCancelPendingRead()
    {
        var stream = new RecordingReadStream(blockReads: true);
        var reader = new StreamPipeReader(stream, bufferSize: 1, leaveOpen: true);

        ValueTask<ReadResult> readTask = reader.ReadAsync(CancellationToken.None);

        CancellationToken readCancellationToken = Assert.Single(stream.ReadCancellationTokens);
        Assert.True(readCancellationToken.CanBeCanceled);
        reader.CancelPendingRead();
        ReadResult result = await readTask;
        Assert.True(result.IsCanceled);
        reader.Complete();
    }

    [Fact]
    public async Task ReadAsync_CancellableCaller_LinksCancellationToken()
    {
        var stream = new RecordingReadStream(blockReads: true);
        var reader = new StreamPipeReader(stream, bufferSize: 1, leaveOpen: true);
        using var cts = new CancellationTokenSource();

        ValueTask<ReadResult> readTask = reader.ReadAsync(cts.Token);

        CancellationToken readCancellationToken = Assert.Single(stream.ReadCancellationTokens);
        Assert.True(readCancellationToken.CanBeCanceled);
        Assert.NotEqual(cts.Token, readCancellationToken);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask.AsTask());
        reader.Complete();
    }

    protected override PipeReader CreatePipeReader(Stream stream, int sizeHint = 0) => stream.UseStrictPipeReader(sizeHint);

    private sealed class RecordingReadStream : Stream
    {
        private readonly bool blockReads;

        internal RecordingReadStream(bool blockReads)
        {
            this.blockReads = blockReads;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        internal List<CancellationToken> ReadCancellationTokens { get; } = new();

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            this.ReadCancellationTokens.Add(cancellationToken);
            if (this.blockReads)
            {
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }

            buffer[offset] = 1;
            return 1;
        }

#if SPAN_BUILTIN

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            this.ReadCancellationTokens.Add(cancellationToken);
            if (this.blockReads)
            {
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }

            buffer.Span[0] = 1;
            return 1;
        }

#endif

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
