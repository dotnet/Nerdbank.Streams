// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Xunit;

internal class MockMemoryPool<T> : MemoryPool<T>
{
    public override int MaxBufferSize => throw new NotImplementedException();

    public int RentCallCount { get; private set; }
    public List<Memory<T>> Contents { get; } = new List<Memory<T>>();

    /// <summary>
    /// Gets or sets a multiplying factor for how much larger the minimum size of array returned
    /// should be relative to the actual requested size.
    /// </summary>
    public double MinArraySizeFactor { get; set; } = 1.0;

    internal int DefaultLength { get; set; } = 16;

    public override IMemoryOwner<T> Rent(int minBufferSize = -1)
    {
        this.RentCallCount++;

        Memory<T> result;
        if (minBufferSize <= 0)
        {
            result = this.Contents.FirstOrDefault();
        }
        else
        {
            minBufferSize = (int)(minBufferSize * this.MinArraySizeFactor);
            result = this.Contents.FirstOrDefault(a => a.Length >= minBufferSize);
        }

        if (result.Length == 0)
        {
            result = minBufferSize == 0 ? default : new T[minBufferSize == -1 ? this.DefaultLength : minBufferSize];
        }
        else
        {
            this.Contents.Remove(result);
        }

        return new Rental(this, result);
    }

    internal void AssertContents(params Memory<T>[] expectedArrays) => this.AssertContents((IEnumerable<Memory<T>>)expectedArrays);

    internal void AssertContents(IEnumerable<Memory<T>> expectedArrays)
    {
        Assert.Equal(expectedArrays, this.Contents);
    }

    /// <summary>
    /// Adds an array to the pool.
    /// </summary>
    /// <param name="length">The length of the array.</param>
    internal void Seed(int length)
    {
        this.Contents.Add(new T[length]);
    }

    protected override void Dispose(bool disposing)
    {
    }

    private void Return(Rental rental)
    {
        if (rental.Memory.Length > 0)
        {
            this.Contents.Add(rental.Memory);
        }
    }

    private class Rental : IMemoryOwner<T>
    {
        private readonly MockMemoryPool<T> owner;

        internal Rental(MockMemoryPool<T> owner, Memory<T> memory)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.Memory = memory;
        }

        public Memory<T> Memory { get; }

        public void Dispose()
        {
            this.owner.Return(this);
        }
    }
}
