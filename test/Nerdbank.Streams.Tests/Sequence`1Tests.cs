using System;
using System.Buffers;
using Nerdbank.Streams;
using NSubstitute;
using Xunit;

namespace Nerdbank.Streams.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="Sequence{T}"/> class.
    /// </summary>
    public class SequenceTests
    {
        private readonly ArrayPool<byte> arrayPool;
        private readonly MemoryPool<byte> memoryPool;

        public SequenceTests()
        {
            arrayPool = ArrayPool<byte>.Shared;
            memoryPool = MemoryPool<byte>.Shared;
        }

        /// <summary>
        /// Tests that the default constructor creates an empty sequence.
        /// </summary>
        [Fact]
        public void Constructor_Default_CreatesEmptySequence()
        {
            // Arrange
            var sequence = new Sequence<byte>();

            // Act
            ReadOnlySequence<byte> roSequence = sequence.AsReadOnlySequence;

            // Assert
            Assert.Equal(0, roSequence.Length);
        }

        /// <summary>
        /// Tests that the constructor with a MemoryPool creates an empty sequence.
        /// </summary>
        [Fact]
        public void Constructor_WithMemoryPool_CreatesEmptySequence()
        {
            // Arrange
            var sequence = new Sequence<byte>(memoryPool);

            // Act
            ReadOnlySequence<byte> roSequence = sequence.AsReadOnlySequence;

            // Assert
            Assert.Equal(0, roSequence.Length);
        }

        /// <summary>
        /// Tests that the constructor with an ArrayPool creates an empty sequence.
        /// </summary>
        [Fact]
        public void Constructor_WithArrayPool_CreatesEmptySequence()
        {
            // Arrange
            var sequence = new Sequence<byte>(arrayPool);

            // Act
            ReadOnlySequence<byte> roSequence = sequence.AsReadOnlySequence;

            // Assert
            Assert.Equal(0, roSequence.Length);
        }

        /// <summary>
        /// Tests that GetMemory returns a memory buffer with at least the requested size.
        /// </summary>
        [Fact]
        public void GetMemory_WithPositiveSizeHint_ReturnsMemoryWithSufficientCapacity()
        {
            // Arrange
            var sequence = new Sequence<byte>(arrayPool);
            int requestedSize = 10;

            // Act
            Memory<byte> memory = sequence.GetMemory(requestedSize);

            // Assert
            Assert.True(memory.Length >= requestedSize, $"Memory length {memory.Length} is less than requested {requestedSize}.");
        }

        /// <summary>
        /// Tests that GetSpan returns a span with at least the requested size.
        /// </summary>
        [Fact]
        public void GetSpan_WithPositiveSizeHint_ReturnsSpanWithSufficientCapacity()
        {
            // Arrange
            var sequence = new Sequence<byte>(arrayPool);
            int requestedSize = 15;

            // Act
            Span<byte> span = sequence.GetSpan(requestedSize);

            // Assert
            Assert.True(span.Length >= requestedSize, $"Span length {span.Length} is less than requested {requestedSize}.");
        }

        /// <summary>
        /// Tests that calling Advance without acquiring memory throws an InvalidOperationException.
        /// </summary>
        [Fact]
        public void Advance_WithoutAcquiringMemory_ThrowsException()
        {
            // Arrange
            var sequence = new Sequence<byte>(arrayPool);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => sequence.Advance(5));
            Assert.Contains("Cannot advance before acquiring memory", ex.Message);
        }

        /// <summary>
        /// Tests that Advance correctly commits bytes after acquiring memory.
        /// </summary>
        [Fact]
        public void Advance_AfterGetMemory_CommitsDataAndIncreasesLength()
        {
            // Arrange
            var sequence = new Sequence<byte>(arrayPool);
            int requestedSize = 20;
            Memory<byte> memory = sequence.GetMemory(requestedSize);
            int bytesToWrite = 10;
            // Simulate writing data.
            memory.Span.Slice(0, bytesToWrite).Fill(0x1);

            // Act
            sequence.Advance(bytesToWrite);
            ReadOnlySequence<byte> roSequence = sequence.AsReadOnlySequence;

            // Assert
            Assert.Equal(bytesToWrite, roSequence.Length);
        }

        /// <summary>
        /// Tests that Append appends non-empty memory to the sequence.
        /// </summary>
        [Fact]
        public void Append_NonEmptyMemory_AppendsData()
        {
            // Arrange
            var sequence = new Sequence<byte>(arrayPool);
            byte[] data = new byte[] { 1, 2, 3, 4 };
            ReadOnlyMemory<byte> readOnlyMemory = new ReadOnlyMemory<byte>(data);

            // Act
            sequence.Append(readOnlyMemory);
            ReadOnlySequence<byte> roSequence = sequence.AsReadOnlySequence;

            // Assert
            Assert.Equal(data.Length, roSequence.Length);
        }

        /// <summary>
        /// Tests that Append with an empty memory does not alter the sequence.
        /// </summary>
        [Fact]
        public void Append_EmptyMemory_DoesNotChangeSequence()
        {
            // Arrange
            var sequence = new Sequence<byte>(arrayPool);
            byte[] data = Array.Empty<byte>();
            ReadOnlyMemory<byte> readOnlyMemory = new ReadOnlyMemory<byte>(data);

            // Act
            sequence.Append(readOnlyMemory);
            ReadOnlySequence<byte> roSequence = sequence.AsReadOnlySequence;

            // Assert
            Assert.Equal(0, roSequence.Length);
        }

        /// <summary>
        /// Tests that Reset clears the sequence and resets its length.
        /// </summary>
        [Fact]
        public void Reset_AfterAppendingData_ClearsSequence()
        {
            // Arrange
            var sequence = new Sequence<byte>(arrayPool);
            byte[] data = new byte[] { 5, 6, 7 };
            sequence.Append(new ReadOnlyMemory<byte>(data));
            Assert.NotEqual(0, sequence.AsReadOnlySequence.Length);

            // Act
            sequence.Reset();
            ReadOnlySequence<byte> roSequence = sequence.AsReadOnlySequence;

            // Assert
            Assert.Equal(0, roSequence.Length);
        }

        /// <summary>
        /// Tests that Dispose clears the sequence by internally calling Reset.
        /// </summary>
        [Fact]
        public void Dispose_ClearsSequence()
        {
            // Arrange
            var sequence = new Sequence<byte>(arrayPool);
            byte[] data = new byte[] { 9, 8, 7 };
            sequence.Append(new ReadOnlyMemory<byte>(data));
            Assert.NotEqual(0, sequence.AsReadOnlySequence.Length);

            // Act
            sequence.Dispose();
            ReadOnlySequence<byte> roSequence = sequence.AsReadOnlySequence;

            // Assert
            Assert.Equal(0, roSequence.Length);
        }

        /// <summary>
        /// Tests that the implicit operator returns an empty ReadOnlySequence when the sequence is null.
        /// </summary>
        [Fact]
        public void ImplicitOperator_NullSequence_ReturnsEmptyReadOnlySequence()
        {
            // Arrange
            Sequence<byte>? sequence = null;

            // Act
            ReadOnlySequence<byte> roSequence = sequence;

            // Assert
            Assert.Equal(0, roSequence.Length);
        }

        /// <summary>
        /// Tests that AdvanceTo with a default SequencePosition does nothing.
        /// </summary>
        [Fact]
        public void AdvanceTo_DefaultSequencePosition_DoesNothing()
        {
            // Arrange
            var sequence = new Sequence<byte>(arrayPool);
            Memory<byte> memory = sequence.GetMemory(30);
            int bytesToWrite = 15;
            memory.Span.Slice(0, bytesToWrite).Fill(0xFF);
            sequence.Advance(bytesToWrite);
            ReadOnlySequence<byte> originalSequence = sequence.AsReadOnlySequence;

            // Act
            sequence.AdvanceTo(default);
            ReadOnlySequence<byte> roSequence = sequence.AsReadOnlySequence;

            // Assert
            Assert.Equal(originalSequence.Length, roSequence.Length);
        }

        /// <summary>
        /// Tests that AdvanceTo with a valid SequencePosition removes the data before that position.
        /// </summary>
        [Fact]
        public void AdvanceTo_ValidSequencePosition_RemovesDataBeforePosition()
        {
            // Arrange
            var sequence = new Sequence<byte>(arrayPool);
            Memory<byte> memory = sequence.GetMemory(50);
            int totalBytes = 20;
            // Fill memory with sequential data.
            for (int i = 0; i < totalBytes; i++)
            {
                memory.Span[i] = (byte)(i + 1);
            }
            sequence.Advance(totalBytes);
            ReadOnlySequence<byte> originalSequence = sequence.AsReadOnlySequence;
            long originalLength = originalSequence.Length;

            // Create a SequencePosition 5 bytes into the first segment.
            SequencePosition pos = originalSequence.GetPosition(5);

            // Act
            sequence.AdvanceTo(pos);
            ReadOnlySequence<byte> roSequence = sequence.AsReadOnlySequence;

            // Assert: new length should be originalLength - 5.
            Assert.Equal(originalLength - 5, roSequence.Length);

            // Verify that the first element is now the 6th element from original data.
            SequenceReader<byte> reader = new SequenceReader<byte>(roSequence);
            bool success = reader.TryRead(out byte firstValue);
            Assert.True(success, "Failed to read from the sequence after advancing.");
            Assert.Equal(6, firstValue);
        }

        /// <summary>
        /// Tests that auto-increasing of MinimumSpanLength occurs as the sequence grows.
        /// </summary>
        [Fact]
        public void AutoIncreaseMinimumSpanLength_WhenSequenceGrows_IncreasesMinimumSpanLength()
        {
            // Arrange
            var sequence = new Sequence<byte>(arrayPool)
            {
                AutoIncreaseMinimumSpanLength = true,
                MinimumSpanLength = 0
            };

            int iterations = 10;
            int bytesPerIteration = 20;

            // Act: Advance and accumulate data to grow the sequence.
            for (int i = 0; i < iterations; i++)
            {
                Memory<byte> memory = sequence.GetMemory(bytesPerIteration);
                sequence.Advance(bytesPerIteration);
            }
            long totalLength = sequence.AsReadOnlySequence.Length;
            int expectedMinimum = (int)Math.Min(32 * 1024, totalLength / 2);

            // Assert: MinimumSpanLength should be increased to at least expectedMinimum.
            Assert.True(sequence.MinimumSpanLength >= expectedMinimum, 
                $"Expected MinimumSpanLength to be at least {expectedMinimum} but was {sequence.MinimumSpanLength}.");
        }
    }
}
