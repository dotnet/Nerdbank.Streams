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
    private static readonly byte[] Data3Bytes = new byte[] { 0x1, 0x3, 0x2 };

    private static readonly byte[] Data5Bytes = new byte[] { 0x1, 0x3, 0x2, 0x5, 0x4 };

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
        byte[] sentBuffer = Data3Bytes;

        this.stream1.Write(sentBuffer, 0, sentBuffer.Length);
        byte[] buffer = new byte[sentBuffer.Length];
        int bytesRead = this.stream2.Read(buffer, 0, buffer.Length);
        Assert.Equal(sentBuffer.Length, bytesRead);
        Assert.Equal<byte>(sentBuffer, buffer);
    }

    [Fact]
    public void Read_InSmallerBlocks()
    {
        byte[] sentBuffer = Data5Bytes;

        this.stream1.Write(sentBuffer, 0, sentBuffer.Length);
        byte[] buffer = new byte[2];
        int bytesRead = this.stream2.Read(buffer, 0, buffer.Length);
        Assert.Equal(buffer.Length, bytesRead);
        Assert.Equal(sentBuffer.Take(2), buffer);

        bytesRead = this.stream2.Read(buffer, 0, buffer.Length);
        Assert.Equal(buffer.Length, bytesRead);
        Assert.Equal(sentBuffer.Skip(2).Take(2), buffer);

        bytesRead = this.stream2.Read(buffer, 0, buffer.Length);
        Assert.Equal(1, bytesRead);
        Assert.Equal(sentBuffer.Skip(4), buffer.Take(1));
    }

    [Fact]
    public void Write_TwiceThenRead()
    {
        this.stream1.Write(Data3Bytes, 0, Data3Bytes.Length);
        this.stream1.Write(Data5Bytes, 0, Data5Bytes.Length);
        byte[] receiveBuffer = new byte[Data3Bytes.Length + Data5Bytes.Length];
        int bytesRead = 0;
        do
        {
            // Per the MSDN documentation, Read can fill less than the provided buffer.
            int bytesJustRead = this.stream2.Read(receiveBuffer, bytesRead, receiveBuffer.Length - bytesRead);
            Assert.NotEqual(0, bytesJustRead);
            bytesRead += bytesJustRead;
        } while (bytesRead < receiveBuffer.Length);

        Assert.Equal(Data3Bytes, receiveBuffer.Take(Data3Bytes.Length));
        Assert.Equal(Data5Bytes, receiveBuffer.Skip(Data3Bytes.Length));
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
