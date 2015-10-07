using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nerdbank.FullDuplexStream;
using Xunit;

public class FullDuplexStreamTests
{
    private static readonly byte[] Data1 = new byte[3] { 0x1, 0x3, 0x2 };

    private readonly Stream stream1;

    private readonly Stream stream2;

    public FullDuplexStreamTests()
    {
        var tuple = FullDuplexStream.CreateStreams();
        Assert.NotNull(tuple);
        Assert.NotNull(tuple.Item1);
        Assert.NotNull(tuple.Item2);

        this.stream1 = tuple.Item1;
        this.stream2 = tuple.Item2;
    }

    [Fact]
    public void Write_InvalidArgs()
    {
        Assert.Throws<ArgumentNullException>(() => this.stream1.Write(null, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream1.Write(new byte[0], -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream1.Write(new byte[0], 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream1.Write(new byte[0], 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream1.Write(new byte[0], 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.stream1.Write(new byte[1], 0, 2));
    }

    [Fact]
    public void Write_CanBeReadOnOtherStream()
    {
        byte[] sentBuffer = Data1;

        this.stream1.Write(sentBuffer, 0, sentBuffer.Length);
        byte[] buffer = new byte[sentBuffer.Length];
        int bytesRead = this.stream2.Read(buffer, 0, buffer.Length);
        Assert.Equal(sentBuffer.Length, bytesRead);
        Assert.Equal<byte>(sentBuffer, buffer);
    }

    [Fact]
    public void Write_EmptyBuffer()
    {
        this.stream1.Write(new byte[0], 0, 0);
    }

    [Fact]
    public void Write_EmptyTrailingEdgeOfBuffer()
    {
        this.stream1.Write(new byte[1], 1, 0);
    }

    [Fact]
    public void Write_TrailingEdgeOfBuffer()
    {
        this.stream1.Write(new byte[2], 1, 1);
    }
}
