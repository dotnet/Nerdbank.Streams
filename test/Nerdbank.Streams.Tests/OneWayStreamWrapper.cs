// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft;

internal class OneWayStreamWrapper : Stream
{
    private readonly Stream innerStream;
    private readonly bool canRead;
    private readonly bool canWrite;

    internal OneWayStreamWrapper(Stream innerStream, bool canRead = false, bool canWrite = false)
    {
        if (canRead == canWrite)
        {
            throw new ArgumentException("Exactly one operation (read or write) must be true.");
        }

        Requires.Argument(innerStream.CanRead || !canRead, nameof(canRead), "Underlying stream is not readable.");
        Requires.Argument(innerStream.CanWrite || !canWrite, nameof(canWrite), "Underlying stream is not writeable.");

        this.innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        this.canRead = canRead;
        this.canWrite = canWrite;
    }

    public override bool CanRead => this.canRead && this.innerStream.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => this.canWrite && this.innerStream.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush()
    {
        if (this.CanWrite)
        {
            this.innerStream.Flush();
        }
        else
        {
            throw new NotSupportedException();
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (this.CanRead)
        {
            return this.innerStream.Read(buffer, offset, count);
        }
        else
        {
            throw new NotSupportedException();
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (this.CanRead)
        {
            return this.innerStream.ReadAsync(buffer, offset, count, cancellationToken);
        }
        else
        {
            throw new NotSupportedException();
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (this.CanWrite)
        {
            this.innerStream.Write(buffer, offset, count);
        }
        else
        {
            throw new NotSupportedException();
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (this.CanWrite)
        {
            return this.innerStream.WriteAsync(buffer, offset, count, cancellationToken);
        }
        else
        {
            throw new NotSupportedException();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.innerStream.Dispose();
        }
    }
}
