// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class MultiplexingProtocolExceptionTests : TestBase
{
    public MultiplexingProtocolExceptionTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void Ctor_Default_ProducesNonEmptyMessage()
    {
        var ex = new MultiplexingProtocolException();
        Assert.False(string.IsNullOrEmpty(ex.Message));
    }

    [Fact]
    public void Ctor_Message()
    {
        string expected = "foo";
        var ex = new MultiplexingProtocolException(expected);
        Assert.Equal(expected, ex.Message);
    }

    [Fact]
    public void Ctor_MessageInner()
    {
        string expectedMessage = "foo";
        Exception expectedInner = new InvalidOperationException();
        var ex = new MultiplexingProtocolException(expectedMessage, expectedInner);
        Assert.Equal(expectedMessage, ex.Message);
        Assert.Same(expectedInner, ex.InnerException);
    }
}
