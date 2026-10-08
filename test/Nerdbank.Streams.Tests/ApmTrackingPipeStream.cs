// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NETFRAMEWORK

using System.IO.Pipes;

internal sealed class ApmTrackingPipeStream : PipeStream
{
    private readonly MemoryStream innerStream;
    private int beginReadCallCount;
    private int beginWriteCallCount;
    private int readAsyncCallCount;
    private int writeAsyncCallCount;

    internal ApmTrackingPipeStream()
        : this(Array.Empty<byte>())
    {
    }

    internal ApmTrackingPipeStream(byte[] initialBuffer)
        : base(PipeDirection.InOut, 4096)
    {
        this.innerStream = new MemoryStream();
        this.innerStream.Write(initialBuffer, 0, initialBuffer.Length);
        this.innerStream.Position = 0;
    }

    public override bool CanRead => this.innerStream.CanRead;

    public override bool CanSeek => this.innerStream.CanSeek;

    public override bool CanWrite => this.innerStream.CanWrite;

    public override long Length => this.innerStream.Length;

    public override long Position
    {
        get => this.innerStream.Position;
        set => this.innerStream.Position = value;
    }

    internal int BeginReadCallCount => Volatile.Read(ref this.beginReadCallCount);

    internal int BeginWriteCallCount => Volatile.Read(ref this.beginWriteCallCount);

    internal int ReadAsyncCallCount => Volatile.Read(ref this.readAsyncCallCount);

    internal int WriteAsyncCallCount => Volatile.Read(ref this.writeAsyncCallCount);

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
    {
        Interlocked.Increment(ref this.beginReadCallCount);
        return this.innerStream.BeginRead(buffer, offset, count, callback, state);
    }

    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
    {
        Interlocked.Increment(ref this.beginWriteCallCount);
        return this.innerStream.BeginWrite(buffer, offset, count, callback, state);
    }

    public override int EndRead(IAsyncResult asyncResult) => this.innerStream.EndRead(asyncResult);

    public override void EndWrite(IAsyncResult asyncResult) => this.innerStream.EndWrite(asyncResult);

    public override void Flush() => this.innerStream.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => this.innerStream.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => this.innerStream.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref this.readAsyncCallCount);
        return base.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin) => this.innerStream.Seek(offset, origin);

    public override void SetLength(long value) => this.innerStream.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) => this.innerStream.Write(buffer, offset, count);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref this.writeAsyncCallCount);
        return base.WriteAsync(buffer, offset, count, cancellationToken);
    }

    internal byte[] ToArray() => this.innerStream.ToArray();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.innerStream.Dispose();
        }

        base.Dispose(disposing);
    }
}

#endif
