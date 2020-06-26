// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using Nerdbank.Streams;
using Xunit;

#pragma warning disable CS0618 // Type or member is obsolete

public class MultiplexingStreamOptionsTests
{
    private MultiplexingStream.Options options = new MultiplexingStream.Options();

    [Fact]
    public void DefaultChannelReceivingWindowSize()
    {
        Assert.True(this.options.DefaultChannelReceivingWindowSize > 0);
        this.options.DefaultChannelReceivingWindowSize = 5;
        Assert.Equal(5, this.options.DefaultChannelReceivingWindowSize);
        Assert.Throws<ArgumentOutOfRangeException>(() => this.options.DefaultChannelReceivingWindowSize = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => this.options.DefaultChannelReceivingWindowSize = -1);
    }

    [Fact]
    public void ProtocolMajorVersion()
    {
        Assert.Equal(1, this.options.ProtocolMajorVersion);
        this.options.ProtocolMajorVersion = 100;
        Assert.Equal(100, this.options.ProtocolMajorVersion);

        Assert.Throws<ArgumentOutOfRangeException>(() => this.options.ProtocolMajorVersion = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => this.options.ProtocolMajorVersion = -1);
    }

    [Fact]
    public void TraceSource()
    {
        Assert.NotNull(this.options.TraceSource);
        Assert.Throws<ArgumentNullException>(() => this.options.TraceSource = null!);
        Assert.NotNull(this.options.TraceSource);

        var traceSource = new TraceSource("test");
        this.options.TraceSource = traceSource;
        Assert.Same(traceSource, this.options.TraceSource);
    }

    [Fact]
    public void DefaultChannelTraceSourceFactory()
    {
        Assert.Null(this.options.DefaultChannelTraceSourceFactory);
        this.options.DefaultChannelTraceSourceFactory = (id, name) => null;
        Assert.NotNull(this.options.DefaultChannelTraceSourceFactory);
    }

    [Fact]
    public void DefaultChannelTraceSourceFactoryWithQualifier()
    {
        Assert.Null(this.options.DefaultChannelTraceSourceFactoryWithQualifier);
        this.options.DefaultChannelTraceSourceFactoryWithQualifier = (id, name) => null;
        Assert.NotNull(this.options.DefaultChannelTraceSourceFactoryWithQualifier);
    }

    [Fact]
    public void IsFrozen()
    {
        Assert.False(this.options.IsFrozen);
        var frozen = this.options.GetFrozenCopy();
        Assert.NotSame(this.options, frozen);
        Assert.True(frozen.IsFrozen);
        Assert.False(this.options.IsFrozen);
    }

    [Fact]
    public void CopyConstructor()
    {
        Assert.Throws<ArgumentNullException>(() => new MultiplexingStream.Options(null!));

        var original = new MultiplexingStream.Options
        {
            DefaultChannelReceivingWindowSize = 7185,
            DefaultChannelTraceSourceFactory = (id, name) => null,
            DefaultChannelTraceSourceFactoryWithQualifier = (id, name) => null,
            ProtocolMajorVersion = 1024,
            TraceSource = new TraceSource("test"),
        };

        var copy = new MultiplexingStream.Options(original);
        Assert.Equal(original.DefaultChannelReceivingWindowSize, copy.DefaultChannelReceivingWindowSize);
        Assert.Equal(original.DefaultChannelTraceSourceFactory, copy.DefaultChannelTraceSourceFactory);
        Assert.Equal(original.DefaultChannelTraceSourceFactoryWithQualifier, copy.DefaultChannelTraceSourceFactoryWithQualifier);
        Assert.Equal(original.ProtocolMajorVersion, copy.ProtocolMajorVersion);
        Assert.Equal(original.TraceSource, copy.TraceSource);
    }

    [Fact]
    public void Frozen_ThrowsOnChanges()
    {
        var frozen = this.options.GetFrozenCopy();
        Assert.Throws<InvalidOperationException>(() => frozen.DefaultChannelReceivingWindowSize = 1024);
        Assert.Throws<InvalidOperationException>(() => frozen.DefaultChannelTraceSourceFactory = (id, name) => null);
        Assert.Throws<InvalidOperationException>(() => frozen.DefaultChannelTraceSourceFactoryWithQualifier = (id, name) => null);
        Assert.Throws<InvalidOperationException>(() => frozen.ProtocolMajorVersion = 5);
        Assert.Throws<InvalidOperationException>(() => frozen.TraceSource = new TraceSource("test"));
    }

    [Fact]
    public void CopyOfFrozenIsNotFrozen()
    {
        var thawedOptions = new MultiplexingStream.Options(this.options.GetFrozenCopy());
        Assert.False(thawedOptions.IsFrozen);
        thawedOptions.ProtocolMajorVersion = 500;
    }
}
