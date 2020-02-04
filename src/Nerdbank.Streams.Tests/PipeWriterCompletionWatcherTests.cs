// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class PipeWriterCompletionWatcherTests : TestBase
{
    private readonly PipeWriter writer = new Pipe().Writer;
    private readonly PipeWriter monitored;
    private readonly object state = new object();
    private readonly TaskCompletionSource<Exception?> completionException = new TaskCompletionSource<Exception?>();

    public PipeWriterCompletionWatcherTests(ITestOutputHelper logger)
        : base(logger)
    {
        this.monitored = this.writer.OnCompleted(this.OnCompleted, this.state);
    }

    [Fact]
    public void OnCompleted_NullWriter()
    {
        PipeWriter? writer = null;
        Assert.Throws<ArgumentNullException>(() => writer!.OnCompleted((e, s) => { }));
    }

    [Fact]
    public async Task NullState()
    {
        var tcs = new TaskCompletionSource<Exception?>();
        var monitored = this.writer.OnCompleted(
            (e, s) =>
            {
                tcs.SetResult(e);
                Assert.Null(s);
            },
            null);
        var expectedException = new InvalidOperationException();
        monitored.Complete(expectedException);
        Assert.Same(expectedException, await tcs.Task);
    }

    [Fact]
    public async Task Complete_Twice()
    {
        this.monitored.Complete();
        Assert.Null(await this.completionException.Task);
        this.monitored.Complete(new InvalidOperationException());
    }

    private void OnCompleted(Exception? ex, object? state)
    {
        this.completionException.SetResult(ex);
        Assert.Same(this.state, state);
    }
}
