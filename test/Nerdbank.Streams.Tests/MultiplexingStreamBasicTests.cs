// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using NSubstitute;
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
        Stream readonlyStreamMock = Substitute.For<Stream>();
        readonlyStreamMock.CanRead.Returns(true);
        await Assert.ThrowsAsync<ArgumentException>(() => MultiplexingStream.CreateAsync(readonlyStreamMock, this.TimeoutToken)).WithCancellation(this.TimeoutToken);
    }

    [Fact]
    public async Task Stream_CanReadFalse_Rejected()
    {
        Stream writeOnlyStreamMock = Substitute.For<Stream>();
        writeOnlyStreamMock.CanWrite.Returns(true);
        await Assert.ThrowsAsync<ArgumentException>(() => MultiplexingStream.CreateAsync(writeOnlyStreamMock, this.TimeoutToken)).WithCancellation(this.TimeoutToken);
    }
}
