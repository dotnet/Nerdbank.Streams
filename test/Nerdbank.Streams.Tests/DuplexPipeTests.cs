// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Nerdbank.Streams;
using Xunit;

public class DuplexPipeTests
{
    [Fact]
    public void Ctor()
    {
        var pipe = new Pipe();
        var duplexPipe = new DuplexPipe(pipe.Reader, pipe.Writer);
        Assert.Same(pipe.Reader, duplexPipe.Input);
        Assert.Same(pipe.Writer, duplexPipe.Output);
    }

    [Fact]
    public void Ctor_ReplacesNullsWithCompleted()
    {
        var duplex = new DuplexPipe(null, null);
        Assert.NotNull(duplex.Input);
        Assert.NotNull(duplex.Output);
    }

    [Fact]
    public async Task ReaderOnly()
    {
        var pipe = new Pipe();
        var duplex = new DuplexPipe(pipe.Reader);

        // Our assert strategy is to verify our no-op writer behaves the same way as a completed writer.
        pipe.Writer.Complete();
        Assert.Throws<InvalidOperationException>(() => pipe.Writer.GetMemory());
        Assert.Throws<InvalidOperationException>(() => duplex.Output.GetMemory());

        Assert.Throws<InvalidOperationException>(() => pipe.Writer.GetSpan());
        Assert.Throws<InvalidOperationException>(() => duplex.Output.GetSpan());

        // System.IO.Pipelines stopped throwing when Advance(0) is called after completion,
        // But we still feel it's useful to throw since it's a read-only pipe.
        pipe.Writer.Advance(0);
        Assert.Throws<InvalidOperationException>(() => duplex.Output.Advance(0));

        FlushResult flushResult = await pipe.Writer.FlushAsync();
        Assert.False(flushResult.IsCompleted);
        flushResult = await duplex.Output.FlushAsync();
        Assert.False(flushResult.IsCompleted);

        pipe.Writer.CancelPendingFlush();
        duplex.Output.CancelPendingFlush();

#pragma warning disable CS0618 // Type or member is obsolete
        pipe.Writer.OnReaderCompleted((ex, s) => { }, null);
        duplex.Output.OnReaderCompleted((ex, s) => { }, null);
#pragma warning restore CS0618 // Type or member is obsolete

        pipe.Writer.Complete();
        duplex.Output.Complete();
    }

    [Fact]
    public async Task WriterOnly()
    {
        var pipe = new Pipe();
        var duplex = new DuplexPipe(pipe.Writer);

        // Our assert strategy is to verify our no-op writer behaves the same way as a completed writer.
        pipe.Reader.Complete();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipe.Reader.ReadAsync().AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => duplex.Input.ReadAsync().AsTask());

        Assert.Throws<InvalidOperationException>(() => pipe.Reader.TryRead(out ReadResult result));
        Assert.Throws<InvalidOperationException>(() => duplex.Input.TryRead(out ReadResult result));

        Assert.Throws<InvalidOperationException>(() => pipe.Reader.AdvanceTo(default));
        Assert.Throws<InvalidOperationException>(() => duplex.Input.AdvanceTo(default));

        pipe.Reader.CancelPendingRead();
        duplex.Input.CancelPendingRead();

#pragma warning disable CS0618 // Type or member is obsolete
        pipe.Reader.OnWriterCompleted((ex, s) => { }, null);
        duplex.Input.OnWriterCompleted((ex, s) => { }, null);
#pragma warning restore CS0618 // Type or member is obsolete

        pipe.Reader.Complete();
        duplex.Input.Complete();
    }
}
