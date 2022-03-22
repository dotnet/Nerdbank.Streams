// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams.Tests;

using System.Buffers;
using Xunit;

public class BufferWriterExtensionsTests
{
    [Fact]
    public void Write_Default()
    {
        using Sequence<int> seq = new();
        seq.Write(default);
        Assert.Equal(0, seq.Length);
    }

    [Fact]
    public void Write_OneBlock()
    {
        int[] array = new int[] { 1, 2, 3 };
        using Sequence<int> template = new();
        template.Write(array);

        using Sequence<int> seq = new();
        seq.Write(template);
        Assert.Equal(array, seq.AsReadOnlySequence.ToArray());
    }

    [Fact]
    public void Write_MultipleBlocks()
    {
        int[] array = new int[] { 1, 2, 3 };
        using Sequence<int> template = new();
        template.Write(array);
        template.Append(array);

        using Sequence<int> seq = new();
        seq.Write(template);
        Assert.Equal(array.Concat(array), seq.AsReadOnlySequence.ToArray());
    }
}
