// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO.Pipelines;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class MultiplexingStreamChannelOptionsTests : TestBase
{
    public MultiplexingStreamChannelOptionsTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void Defaults()
    {
        var options = new MultiplexingStream.ChannelOptions();
        Assert.Null(options.TraceSource);
        Assert.Null(options.ExistingPipe);
    }

    [Fact]
    public void TraceSource()
    {
        var src = new TraceSource("name");
        var options = new MultiplexingStream.ChannelOptions
        {
            TraceSource = src,
        };
        Assert.Same(src, options.TraceSource);
        options.TraceSource = null;
        Assert.Null(options.TraceSource);
    }

    [Fact]
    public void ExistingPipe_Mock()
    {
        var pipe = new Pipe();
        var duplexPipe = new MockDuplexPipe { Input = pipe.Reader, Output = pipe.Writer };
        var options = new MultiplexingStream.ChannelOptions
        {
            ExistingPipe = duplexPipe,
        };

        // We provided an "untrusted" instance of IDuplexPipe, so it would be copied into a trusted type.
        // Only assert that the contents are the same.
        Assert.Same(duplexPipe.Input, options.ExistingPipe.Input);
        Assert.Same(duplexPipe.Output, options.ExistingPipe.Output);

        options.ExistingPipe = null;
        Assert.Null(options.ExistingPipe);
    }

    [Fact]
    public void ExistingPipe_DuplexPipe()
    {
        var pipe = new Pipe();
        var duplexPipe = new DuplexPipe(pipe.Reader, pipe.Writer);
        var options = new MultiplexingStream.ChannelOptions
        {
            ExistingPipe = duplexPipe,
        };

        // We provided an instance of the concrete type DuplexPipe, so we expect that instance was persisted.
        Assert.Same(duplexPipe, options.ExistingPipe);

        options.ExistingPipe = null;
        Assert.Null(options.ExistingPipe);
    }

    [Fact]
    public void ExistingPipe_AcceptsSimplex()
    {
        var options = new MultiplexingStream.ChannelOptions();

        var pipe = new Pipe();
        Assert.Throws<ArgumentException>(() => options.ExistingPipe = new MockDuplexPipe());
        options.ExistingPipe = new MockDuplexPipe { Input = pipe.Reader };
        options.ExistingPipe = new MockDuplexPipe { Output = pipe.Writer };
    }

    [Fact]
    public void ReaderPipeOptions()
    {
        PipeOptions expected = new PipeOptions();
        var options = new MultiplexingStream.ChannelOptions
        {
            InputPipeOptions = expected,
        };
        Assert.Same(expected, options.InputPipeOptions);
        options.InputPipeOptions = null;
        Assert.Null(options.InputPipeOptions);
    }

    private class MockDuplexPipe : IDuplexPipe
    {
        public PipeReader? Input { get; set; }

        public PipeWriter? Output { get; set; }
    }
}
