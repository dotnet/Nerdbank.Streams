// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class SequenceTextReaderTests : TestBase
{
    private const string CharactersToRead = "ABCDEFG\r\nabcdefg\n1234567890";
    private static readonly Encoding DefaultEncoding = Encoding.UTF8;
    private readonly SequenceTextReader sequenceTextReader = new SequenceTextReader();
    private readonly TextReader baselineReader = new StringReader(CharactersToRead);

    public SequenceTextReaderTests(ITestOutputHelper logger)
        : base(logger)
    {
        var ros = new ReadOnlySequence<byte>(DefaultEncoding.GetBytes(CharactersToRead));
        this.sequenceTextReader.Initialize(ros, DefaultEncoding);
    }

    [Fact]
    public void Ctor_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new SequenceTextReader(default, null!));
    }

    [Fact]
    public void Ctor()
    {
        var reader = new SequenceTextReader(default, DefaultEncoding);
        Assert.Equal(-1, reader.Read());

        var ros = new ReadOnlySequence<byte>(DefaultEncoding.GetBytes(CharactersToRead));
        reader = new SequenceTextReader(ros, DefaultEncoding);
        Assert.Equal('A', reader.Read());
    }

    [Fact]
    public void Initialize_ValidatesArgs()
    {
        Assert.Throws<ArgumentNullException>(() => this.sequenceTextReader.Initialize(default, null!));
    }

    [Fact]
    public void Read_SkipsPreamble()
    {
        byte[] preamble = Encoding.UTF8.GetPreamble();
        byte[] bodyBytes = Encoding.UTF8.GetBytes("a");
        byte[] preambleAndBody = preamble.Concat(bodyBytes).ToArray();
        this.sequenceTextReader.Initialize(new ReadOnlySequence<byte>(preambleAndBody), Encoding.UTF8);
        Assert.Equal('a', this.sequenceTextReader.Read());
        Assert.Equal(-1, this.sequenceTextReader.Read());
    }

    [Fact]
    public void Read_SkipsPreamble_NoBody()
    {
        byte[] preamble = Encoding.UTF8.GetPreamble();
        this.sequenceTextReader.Initialize(new ReadOnlySequence<byte>(preamble), Encoding.UTF8);
        Assert.Equal(-1, this.sequenceTextReader.Read());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Anything_Uninitialized(bool afterReset)
    {
        SequenceTextReader reader;
        if (afterReset)
        {
            reader = this.sequenceTextReader;
            reader.Reset();
        }
        else
        {
            reader = new SequenceTextReader();
        }

        Assert.Equal(-1, reader.Peek());
        Assert.Equal(-1, reader.Read());
        Assert.Null(reader.ReadLine());
        Assert.Equal(string.Empty, reader.ReadToEnd());
    }

    [Fact]
    public void Read()
    {
        int actual, expected;
        do
        {
            actual = this.sequenceTextReader.Read();
            expected = this.baselineReader.Read();
            Assert.Equal(expected, actual);
        }
        while (actual != -1);
    }

    [Fact]
    public void PeekAndRead()
    {
        int actual, expected;
        do
        {
            actual = this.sequenceTextReader.Peek();
            expected = this.baselineReader.Peek();
            Assert.Equal(expected, actual);

            actual = this.sequenceTextReader.Read();
            expected = this.baselineReader.Read();
            Assert.Equal(expected, actual);
        }
        while (actual != -1);
    }

    [Fact]
    public void ReadToEnd()
    {
        Assert.Equal(this.baselineReader.ReadToEnd(), this.sequenceTextReader.ReadToEnd());
    }

    [Fact]
    public void ReadLine()
    {
        string? actual, expected;
        do
        {
            actual = this.sequenceTextReader.ReadLine();
            expected = this.baselineReader.ReadLine();
            Assert.Equal(expected, actual);
        }
        while (actual != null);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    public void ReadBlock(int bufferOffset, int bufferSlack)
    {
        const int BlockLength = 5;
        char[] actualBlock = new char[BlockLength], expectedBlock = new char[BlockLength];
        int actualResult, expectedResult;
        do
        {
            actualResult = this.sequenceTextReader.ReadBlock(actualBlock, bufferOffset, BlockLength - bufferOffset - bufferSlack);
            expectedResult = this.baselineReader.ReadBlock(expectedBlock, bufferOffset, BlockLength - bufferOffset - bufferSlack);
            Assert.Equal(expectedResult, actualResult);
        }
        while (actualResult > 0);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    public void Read_Buffer(int bufferOffset, int bufferSlack)
    {
        const int BlockLength = 5;
        char[] actualBlock = new char[BlockLength], expectedBlock = new char[BlockLength];
        int actualResult, expectedResult;
        do
        {
            actualResult = this.sequenceTextReader.Read(actualBlock, bufferOffset, BlockLength - bufferOffset - bufferSlack);
            expectedResult = this.baselineReader.Read(expectedBlock, bufferOffset, BlockLength - bufferOffset - bufferSlack);
            Assert.Equal(expectedResult, actualResult);
        }
        while (actualResult > 0);
    }

    [Fact]
    public void Read_Buffer_Empty()
    {
        Assert.Equal(0, this.sequenceTextReader.Read(new char[0], 0, 0));
        Assert.Equal(0, this.sequenceTextReader.Read(new char[1], 1, 0));
    }

    [Fact]
    public void Read_Buffer_InvalidInputs()
    {
        Assert.Throws<ArgumentNullException>(() => this.sequenceTextReader.Read(null!, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.sequenceTextReader.Read(new char[2], 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => this.sequenceTextReader.Read(new char[2], -1, 2));
        Assert.Throws<ArgumentException>(() => this.sequenceTextReader.Read(new char[2], 0, 3));
        Assert.Throws<ArgumentException>(() => this.sequenceTextReader.Read(new char[2], 2, 1));
        Assert.Throws<ArgumentException>(() => this.sequenceTextReader.Read(new char[2], 3, 1));
        Assert.Throws<ArgumentException>(() => this.sequenceTextReader.Read(new char[2], 3, 0));
    }

    [Fact]
    public void Read_VeryLarge()
    {
        var smallBytes = DefaultEncoding.GetBytes(CharactersToRead);
        var bytes = new byte[30000];
        for (int i = 0; i < bytes.Length; i += smallBytes.Length)
        {
            Buffer.BlockCopy(smallBytes, 0, bytes, i, Math.Min(bytes.Length - i, smallBytes.Length));
        }

        this.sequenceTextReader.Initialize(new ReadOnlySequence<byte>(bytes), DefaultEncoding);

        const int BlockLength = 49;
        char[] actualBlock = new char[BlockLength];
        char[] expectedBlock = DefaultEncoding.GetChars(bytes);
        int totalCharsRead = 0;
        int charsRead;
        do
        {
            charsRead = this.sequenceTextReader.Read(actualBlock, 0, BlockLength);
            Assert.Equal(expectedBlock.Skip(totalCharsRead).Take(charsRead), actualBlock.Take(charsRead));
            totalCharsRead += charsRead;
        }
        while (charsRead > 0);
    }
}
