using MessagePack;
using Nerdbank.Streams;
using System;
using System.Buffers;
using Xunit;

namespace MessagePack.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref = "SequencePool"/> class.
    /// </summary>
    public class SequencePoolTests
    {
        private const int MinimumSpanLengthConstant = 32 * 1024;
        /// <summary>
        /// Tests that Rent returns a non-null Rental with a non-null Sequence value and the expected MinimumSpanLength.
        /// </summary>
//         [Fact] [Error] (24-31)CS1061 'SequencePool' does not contain a definition for 'Rent' and no accessible extension method 'Rent' accepting a first argument of type 'SequencePool' could be found (are you missing a using directive or an assembly reference?)
//         public void Rent_WhenCalled_ReturnsValidRentalWithExpectedMinimumSpanLength()
//         {
//             // Arrange
//             var pool = new SequencePool(10);
//             // Act
//             var rental = pool.Rent();
//             // Assert
//             Assert.NotNull(rental);
//             Assert.NotNull(rental.Value);
//             Assert.Equal(MinimumSpanLengthConstant, rental.Value.MinimumSpanLength);
//         }

        /// <summary>
        /// Tests that upon disposing a Rental, the Sequence is returned to the pool and later reused.
        /// </summary>
//         [Fact] [Error] (40-32)CS1061 'SequencePool' does not contain a definition for 'Rent' and no accessible extension method 'Rent' accepting a first argument of type 'SequencePool' could be found (are you missing a using directive or an assembly reference?) [Error] (43-32)CS1061 'SequencePool' does not contain a definition for 'Rent' and no accessible extension method 'Rent' accepting a first argument of type 'SequencePool' could be found (are you missing a using directive or an assembly reference?)
//         public void Dispose_AfterRent_ReturnsSequenceToPoolForReuse()
//         {
//             // Arrange
//             var pool = new SequencePool(1);
//             // Act
//             var rental1 = pool.Rent();
//             Sequence<byte> firstInstance = rental1.Value;
//             rental1.Dispose();
//             var rental2 = pool.Rent();
//             Sequence<byte> secondInstance = rental2.Value;
//             // Assert
//             Assert.Same(firstInstance, secondInstance);
//         }

        /// <summary>
        /// Tests that disposing a Rental twice does not result in duplicate entries in the pool.
        /// </summary>
//         [Fact] [Error] (58-31)CS1061 'SequencePool' does not contain a definition for 'Rent' and no accessible extension method 'Rent' accepting a first argument of type 'SequencePool' could be found (are you missing a using directive or an assembly reference?) [Error] (63-36)CS1061 'SequencePool' does not contain a definition for 'Rent' and no accessible extension method 'Rent' accepting a first argument of type 'SequencePool' could be found (are you missing a using directive or an assembly reference?) [Error] (68-34)CS1061 'SequencePool' does not contain a definition for 'Rent' and no accessible extension method 'Rent' accepting a first argument of type 'SequencePool' could be found (are you missing a using directive or an assembly reference?)
//         public void Dispose_CalledTwice_DoesNotDuplicateSequenceInPool()
//         {
//             // Arrange
//             var pool = new SequencePool(1);
//             // Act
//             var rental = pool.Rent();
//             Sequence<byte> instance = rental.Value;
//             rental.Dispose();
//             // Second disposal call should not add the sequence again because pool is at capacity.
//             rental.Dispose();
//             var rentalAfter = pool.Rent();
//             Sequence<byte> reusedInstance = rentalAfter.Value;
//             // Assert
//             Assert.Same(instance, reusedInstance);
//             // Renting again should create a new instance.
//             var rentalNew = pool.Rent();
//             Assert.NotSame(reusedInstance, rentalNew.Value);
//         }

        /// <summary>
        /// Tests that the MinimumSpanLength on a Sequence is reset to the preferred constant after disposal even if modified.
        /// </summary>
//         [Fact] [Error] (80-31)CS1061 'SequencePool' does not contain a definition for 'Rent' and no accessible extension method 'Rent' accepting a first argument of type 'SequencePool' could be found (are you missing a using directive or an assembly reference?) [Error] (84-36)CS1061 'SequencePool' does not contain a definition for 'Rent' and no accessible extension method 'Rent' accepting a first argument of type 'SequencePool' could be found (are you missing a using directive or an assembly reference?)
//         public void Dispose_AfterModifyingMinimumSpanLength_ResetsToPreferredValue()
//         {
//             // Arrange
//             var pool = new SequencePool(1);
//             var rental = pool.Rent();
//             // Act
//             rental.Value.MinimumSpanLength = 1024;
//             rental.Dispose();
//             var rentedAgain = pool.Rent();
//             // Assert
//             Assert.Equal(MinimumSpanLengthConstant, rentedAgain.Value.MinimumSpanLength);
//         }

        /// <summary>
        /// Tests that Rent creates a new Sequence when the pool is empty.
        /// </summary>
//         [Fact] [Error] (98-32)CS1061 'SequencePool' does not contain a definition for 'Rent' and no accessible extension method 'Rent' accepting a first argument of type 'SequencePool' could be found (are you missing a using directive or an assembly reference?) [Error] (100-32)CS1061 'SequencePool' does not contain a definition for 'Rent' and no accessible extension method 'Rent' accepting a first argument of type 'SequencePool' could be found (are you missing a using directive or an assembly reference?)
//         public void Rent_WhenPoolIsEmpty_ReturnsNewSequenceInstance()
//         {
//             // Arrange
//             var pool = new SequencePool(1);
//             // Act
//             var rental1 = pool.Rent();
//             // Do not dispose rental1, so pool is empty.
//             var rental2 = pool.Rent();
//             // Assert
//             Assert.NotSame(rental1.Value, rental2.Value);
//         }
    }
}