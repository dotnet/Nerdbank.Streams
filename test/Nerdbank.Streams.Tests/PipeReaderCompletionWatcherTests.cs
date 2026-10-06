// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Nerdbank.Streams;
using Xunit;

public class PipeReaderCompletionWatcherTests : TestBase
{
    private readonly PipeReader reader = new Pipe().Reader;
    private readonly PipeReader monitored;
    private readonly object state = new object();
    private readonly TaskCompletionSource<Exception?> completionException = new TaskCompletionSource<Exception?>();

    public PipeReaderCompletionWatcherTests()
        : base(TestOutputHelper.Instance)
    {
        this.monitored = this.reader.OnCompleted(this.OnCompleted, this.state);
    }

    [Test]
    public void OnCompleted_NullReader()
    {
        PipeReader? reader = null;
        Assert.Throws<ArgumentNullException>(() => reader!.OnCompleted((e, s) => { }));
    }

    [Test]
    public async Task NullState()
    {
        var tcs = new TaskCompletionSource<Exception?>();
        PipeReader? monitored = this.reader.OnCompleted(
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

    [Test]
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
