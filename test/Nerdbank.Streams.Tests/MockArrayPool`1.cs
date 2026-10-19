// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Microsoft;
using Xunit;

internal class MockArrayPool<T> : ArrayPool<T>
{
    internal const int DefaultLength = 16;

    public List<T[]> Contents { get; } = new List<T[]>();

    /// <summary>
    /// Gets or sets a multiplying factor for how much larger the minimum size of array returned
    /// should be relative to the actual requested size.
    /// </summary>
    public double MinArraySizeFactor { get; set; } = 1.0;

    public override T[] Rent(int minimumLength)
    {
        Requires.Range(minimumLength >= 0, nameof(minimumLength));

        if (minimumLength == 0)
        {
            return Array.Empty<T>();
        }

        minimumLength = (int)(minimumLength * this.MinArraySizeFactor);
        T[]? result = this.Contents.FirstOrDefault(a => a.Length >= minimumLength);

        if (result == null)
        {
            result = new T[minimumLength == -1 ? DefaultLength : minimumLength];
        }
        else
        {
            this.Contents.Remove(result);
        }

        return result;
    }

    public override void Return(T[] array, bool clearArray = false)
    {
        if (clearArray)
        {
            Array.Clear(array, 0, array.Length);
        }

        this.Contents.Add(array);
    }

    internal void AssertContents(params T[][] expectedArrays) => this.AssertContents((IEnumerable<T[]>)expectedArrays);

    internal void AssertContents(IEnumerable<T[]> expectedArrays)
    {
        Assert.Equal(expectedArrays, this.Contents);
    }
}
