// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class MultiplexingStreamBasicTests : TestBase
{
    public MultiplexingStreamBasicTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public async Task Ctor_ThrowsOnNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => MultiplexingStream.CreateAsync(null!, this.TimeoutToken));
    }

    [Fact]
    public async Task Stream_CanWriteFalse_Rejected()
    {
        var readonlyStreamMock = new Moq.Mock<Stream>();
        readonlyStreamMock.SetupGet(r => r.CanRead).Returns(true);
        await Assert.ThrowsAsync<ArgumentException>(() => MultiplexingStream.CreateAsync(readonlyStreamMock.Object, this.TimeoutToken)).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task Stream_CanReadFalse_Rejected()
    {
        var writeOnlyStreamMock = new Moq.Mock<Stream>();
        writeOnlyStreamMock.SetupGet(r => r.CanWrite).Returns(true);
        await Assert.ThrowsAsync<ArgumentException>(() => MultiplexingStream.CreateAsync(writeOnlyStreamMock.Object, this.TimeoutToken)).WithCancellation(this.TimeoutToken);
    }
}
