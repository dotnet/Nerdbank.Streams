// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.IO.Pipelines;
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
    public void Ctor_RejectsNulls()
    {
        var pipe = new Pipe();
        Assert.Throws<ArgumentNullException>(() => new DuplexPipe(null, null));
        Assert.Throws<ArgumentNullException>(() => new DuplexPipe(null, pipe.Writer));
        Assert.Throws<ArgumentNullException>(() => new DuplexPipe(pipe.Reader, null));
    }
}
