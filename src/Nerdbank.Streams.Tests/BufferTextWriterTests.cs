// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class BufferTextWriterTests : TestBase
{
    private static readonly Encoding DefaultEncoding = Encoding.UTF8;
    private static readonly Encoding DefaultEncodingNoPreamble = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private BufferTextWriter bufferTextWriter = new BufferTextWriter();
    private Sequence<byte> sequence = new Sequence<byte>();

    public BufferTextWriterTests(ITestOutputHelper logger)
        : base(logger)
    {
        Assert.Null(this.bufferTextWriter.Encoding);
        this.bufferTextWriter.Initialize(this.sequence, DefaultEncoding);
    }

    [Fact]
    public void Ctor_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new BufferTextWriter(null, DefaultEncoding));
        Assert.Throws<ArgumentNullException>(() => new BufferTextWriter(this.sequence, null));
    }

    [Fact]
    public void Ctor()
    {
        this.bufferTextWriter = new BufferTextWriter(this.sequence, DefaultEncoding);
        Assert.Same(DefaultEncoding, this.bufferTextWriter.Encoding);
        this.bufferTextWriter.Write('a');
        this.AssertWritten("a");
    }

    [Fact]
    public void Initialize_ValidatesArgs()
    {
        Assert.Throws<ArgumentNullException>(() => this.bufferTextWriter.Initialize(new Sequence<byte>(), null));
        Assert.Throws<ArgumentNullException>(() => this.bufferTextWriter.Initialize(null, Encoding.UTF8));
    }

    [Fact]
    public void Initialize_ThrowsIfUnflushed()
    {
        this.bufferTextWriter.Write("hi");
        Assert.Throws<InvalidOperationException>(() => this.bufferTextWriter.Initialize(new Sequence<byte>(), DefaultEncoding));
    }

    [Fact]
    public void Initialize_SameEncoder()
    {
        this.bufferTextWriter.Initialize(new Sequence<byte>(), this.bufferTextWriter.Encoding);
    }

    [Fact]
    public void Encoding_AfterInitialize()
    {
        this.bufferTextWriter.Initialize(new Sequence<byte>(), Encoding.Unicode);
        Assert.Same(Encoding.Unicode, this.bufferTextWriter.Encoding);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Anything_Uninitialized(bool afterReset)
    {
        BufferTextWriter writer;
        if (afterReset)
        {
            writer = this.bufferTextWriter;
            writer.Reset();
        }
        else
        {
            writer = new BufferTextWriter();
        }

        Assert.Throws<InvalidOperationException>(() => writer.Flush());
        Assert.IsType<InvalidOperationException>(writer.FlushAsync().Exception?.InnerException);
        Assert.Throws<InvalidOperationException>(() => writer.Write(true));
        Assert.Throws<InvalidOperationException>(() => writer.Write('a'));
        Assert.Throws<InvalidOperationException>(() => writer.Write("a"));
    }

    [Fact]
    public void Write_Bool()
    {
        this.bufferTextWriter.Write(true);
        this.AssertWritten("True");
    }

    [Fact]
    public void Write_Char()
    {
        this.bufferTextWriter.Write('a');
        this.AssertWritten("a");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(300)]
    [InlineData(30000)]
    public void Write_String(int length)
    {
        string written = new string('a', length);
        this.bufferTextWriter.Write(written);
        this.AssertWritten(written);
    }

    [Fact]
    public void Write_String_Null()
    {
        this.bufferTextWriter.Write((string)null);
        this.AssertWritten(string.Empty);
    }

    [Fact]
    public void Write_CharThenLongString()
    {
        string longString = new string('b', 10000);
        this.bufferTextWriter.Write('a');
        this.bufferTextWriter.Write(longString);
        this.AssertWritten('a' + longString);
    }

    [Fact]
    public void FlushAsync_CompletesSynchronously()
    {
        this.bufferTextWriter.Initialize(this.sequence, DefaultEncodingNoPreamble);
        this.bufferTextWriter.Write('b');
        Assert.Equal(TaskStatus.RanToCompletion, this.bufferTextWriter.FlushAsync().Status);
        Assert.Equal(DefaultEncodingNoPreamble.GetBytes("b"), this.sequence.AsReadOnlySequence.ToArray());
    }

    private void AssertWritten(string expected, Encoding encoding = null)
    {
        encoding = encoding ?? DefaultEncoding;
        this.bufferTextWriter.Flush();

        if (expected.Length == 0)
        {
            Assert.Equal(0, this.sequence.Length);
            return;
        }

        byte[] writtenBytes = this.sequence.AsReadOnlySequence.ToArray();

        // Assert the preamble was written, if any.
        var expectedPreamble = encoding.GetPreamble();
        Assert.Equal(expectedPreamble, writtenBytes.Take(expectedPreamble.Length));

        // Skip the preamble when comparing the string.
        Assert.Equal(expected, encoding.GetString(writtenBytes, expectedPreamble.Length, writtenBytes.Length - expectedPreamble.Length));
    }
}
