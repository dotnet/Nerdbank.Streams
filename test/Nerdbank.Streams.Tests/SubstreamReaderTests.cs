using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Xunit;

namespace Nerdbank.Streams.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref="SubstreamReader"/> class.
    /// </summary>
    public class SubstreamReaderTests
    {
        /// <summary>
        /// A helper stream class with CanRead overridden to false.
        /// </summary>
        private class NonReadableStream : MemoryStream
        {
            public override bool CanRead => false;
        }

        /// <summary>
        /// Creates a MemoryStream containing the given header length and data.
        /// </summary>
        /// <param name="length">The substream length to encode in header.</param>
        /// <param name="data">The substream data bytes.</param>
        /// <returns>A MemoryStream containing header (4-byte little endian) followed by data.</returns>
        private static MemoryStream CreateSubstreamMemoryStream(int length, byte[] data = null)
        {
            byte[] header = BitConverter.GetBytes(length);
            byte[] streamData = header;
            if (data != null)
            {
                using (var ms = new MemoryStream())
                {
                    ms.Write(header, 0, header.Length);
                    ms.Write(data, 0, data.Length);
                    streamData = ms.ToArray();
                }
            }
            return new MemoryStream(streamData);
        }

        /// <summary>
        /// Tests that the constructor throws an ArgumentNullException when the underlying stream is null.
        /// </summary>
//         [Fact] [Error] (56-60)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void Constructor_NullUnderlyingStream_ThrowsArgumentNullException()
//         {
//             // Arrange
//             Stream nullStream = null;
// 
//             // Act & Assert
//             Assert.Throws<ArgumentNullException>(() => new SubstreamReader(nullStream));
//         }

        /// <summary>
        /// Tests that the constructor throws an ArgumentException when the underlying stream is not readable.
        /// </summary>
//         [Fact] [Error] (69-56)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void Constructor_NonReadableStream_ThrowsArgumentException()
//         {
//             // Arrange
//             var nonReadable = new NonReadableStream();
// 
//             // Act & Assert
//             Assert.Throws<ArgumentException>(() => new SubstreamReader(nonReadable));
//         }

        /// <summary>
        /// Tests that the CanRead property always returns true.
        /// </summary>
//         [Fact] [Error] (80-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void CanRead_ReturnsTrue()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0);
//             var reader = new SubstreamReader(ms);
// 
//             // Act
//             bool canRead = reader.CanRead;
// 
//             // Assert
//             Assert.True(canRead);
//         }

        /// <summary>
        /// Tests that the CanSeek property always returns false.
        /// </summary>
//         [Fact] [Error] (97-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void CanSeek_ReturnsFalse()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0);
//             var reader = new SubstreamReader(ms);
// 
//             // Act
//             bool canSeek = reader.CanSeek;
// 
//             // Assert
//             Assert.False(canSeek);
//         }

        /// <summary>
        /// Tests that the CanWrite property always returns false.
        /// </summary>
//         [Fact] [Error] (114-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void CanWrite_ReturnsFalse()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0);
//             var reader = new SubstreamReader(ms);
// 
//             // Act
//             bool canWrite = reader.CanWrite;
// 
//             // Assert
//             Assert.False(canWrite);
//         }

        /// <summary>
        /// Tests that the CanTimeout property returns the underlying stream's CanTimeout value.
        /// </summary>
//         [Fact] [Error] (132-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void CanTimeout_ReturnsUnderlyingStreamCanTimeout()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0);
//             // MemoryStream's CanTimeout is false.
//             var reader = new SubstreamReader(ms);
// 
//             // Act
//             bool canTimeout = reader.CanTimeout;
// 
//             // Assert
//             Assert.Equal(ms.CanTimeout, canTimeout);
//         }

        /// <summary>
        /// Tests that accessing the Length property always throws a NotSupportedException.
        /// </summary>
//         [Fact] [Error] (149-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void Length_AlwaysThrowsNotSupportedException()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0);
//             var reader = new SubstreamReader(ms);
// 
//             // Act & Assert
//             Assert.Throws<NotSupportedException>(() => _ = reader.Length);
//         }

        /// <summary>
        /// Tests that getting the Position property always throws a NotSupportedException.
        /// </summary>
//         [Fact] [Error] (163-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void PositionGet_AlwaysThrowsNotSupportedException()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0);
//             var reader = new SubstreamReader(ms);
// 
//             // Act & Assert
//             Assert.Throws<NotSupportedException>(() => _ = reader.Position);
//         }

        /// <summary>
        /// Tests that setting the Position property always throws a NotSupportedException.
        /// </summary>
//         [Fact] [Error] (177-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void PositionSet_AlwaysThrowsNotSupportedException()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0);
//             var reader = new SubstreamReader(ms);
// 
//             // Act & Assert
//             Assert.Throws<NotSupportedException>(() => reader.Position = 10);
//         }

        /// <summary>
        /// Tests that the Flush method always throws a NotSupportedException.
        /// </summary>
//         [Fact] [Error] (191-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void Flush_AlwaysThrowsNotSupportedException()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0);
//             var reader = new SubstreamReader(ms);
// 
//             // Act & Assert
//             Assert.Throws<NotSupportedException>(() => reader.Flush());
//         }

        /// <summary>
        /// Tests that the FlushAsync method always throws a NotSupportedException.
        /// </summary>
//         [Fact] [Error] (205-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public async Task FlushAsync_AlwaysThrowsNotSupportedException()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0);
//             var reader = new SubstreamReader(ms);
// 
//             // Act & Assert
//             await Assert.ThrowsAsync<NotSupportedException>(() => reader.FlushAsync(CancellationToken.None));
//         }

        /// <summary>
        /// Tests that the Seek method always throws a NotSupportedException.
        /// </summary>
//         [Fact] [Error] (219-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void Seek_AlwaysThrowsNotSupportedException()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0);
//             var reader = new SubstreamReader(ms);
// 
//             // Act & Assert
//             Assert.Throws<NotSupportedException>(() => reader.Seek(0, SeekOrigin.Begin));
//         }

        /// <summary>
        /// Tests that the SetLength method always throws a NotSupportedException.
        /// </summary>
//         [Fact] [Error] (233-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void SetLength_AlwaysThrowsNotSupportedException()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0);
//             var reader = new SubstreamReader(ms);
// 
//             // Act & Assert
//             Assert.Throws<NotSupportedException>(() => reader.SetLength(100));
//         }

        /// <summary>
        /// Tests that the Write method always throws a NotSupportedException.
        /// </summary>
//         [Fact] [Error] (247-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void Write_AlwaysThrowsNotSupportedException()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0);
//             var reader = new SubstreamReader(ms);
//             byte[] buffer = Encoding.UTF8.GetBytes("test");
// 
//             // Act & Assert
//             Assert.Throws<NotSupportedException>(() => reader.Write(buffer, 0, buffer.Length));
//         }

        /// <summary>
        /// Tests that the WriteAsync method always throws a NotSupportedException.
        /// </summary>
//         [Fact] [Error] (262-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public async Task WriteAsync_AlwaysThrowsNotSupportedException()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0);
//             var reader = new SubstreamReader(ms);
//             byte[] buffer = Encoding.UTF8.GetBytes("test");
// 
//             // Act & Assert
//             await Assert.ThrowsAsync<NotSupportedException>(() => reader.WriteAsync(buffer, 0, buffer.Length, CancellationToken.None));
//         }

        /// <summary>
        /// Tests that the Read method returns zero when the substream header indicates an EOF (length of zero).
        /// </summary>
//         [Fact] [Error] (277-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void Read_SubstreamEof_ReturnsZero()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0); // header indicates 0 length => EOF.
//             var reader = new SubstreamReader(ms);
//             byte[] buffer = new byte[10];
// 
//             // Act
//             int bytesRead = reader.Read(buffer, 0, buffer.Length);
// 
//             // Assert
//             Assert.Equal(0, bytesRead);
//         }

        /// <summary>
        /// Tests that the Read method correctly reads data when a valid substream header and data are provided.
        /// </summary>
//         [Fact] [Error] (297-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void Read_NormalRead_ReturnsCorrectBytes()
//         {
//             // Arrange
//             string expectedText = "Hello";
//             byte[] data = Encoding.UTF8.GetBytes(expectedText);
//             using var ms = CreateSubstreamMemoryStream(data.Length, data);
//             var reader = new SubstreamReader(ms);
//             byte[] buffer = new byte[10];
// 
//             // Act
//             int bytesRead = reader.Read(buffer, 0, buffer.Length);
//             // A subsequent read should hit EOF.
//             int secondRead = reader.Read(buffer, 0, buffer.Length);
// 
//             // Assert
//             Assert.Equal(data.Length, bytesRead);
//             Assert.Equal(0, secondRead);
//             string actualText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
//             Assert.Equal(expectedText, actualText);
//         }

        /// <summary>
        /// Tests that the Read method throws an EndOfStreamException when the header is incomplete.
        /// </summary>
//         [Fact] [Error] (322-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void Read_HeaderIncomplete_ThrowsEndOfStreamException()
//         {
//             // Arrange
//             // Create a MemoryStream with fewer than 4 bytes (e.g., 2 bytes).
//             byte[] incompleteHeader = new byte[] { 0x01, 0x02 };
//             using var ms = new MemoryStream(incompleteHeader);
//             var reader = new SubstreamReader(ms);
//             byte[] buffer = new byte[10];
// 
//             // Act & Assert
//             Assert.Throws<EndOfStreamException>(() => reader.Read(buffer, 0, buffer.Length));
//         }

        /// <summary>
        /// Tests that the ReadAsync method returns zero when the substream header indicates an EOF (length of zero).
        /// </summary>
//         [Fact] [Error] (337-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public async Task ReadAsync_SubstreamEof_ReturnsZero()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0); // header indicates 0 length => EOF.
//             var reader = new SubstreamReader(ms);
//             byte[] buffer = new byte[10];
// 
//             // Act
//             int bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);
// 
//             // Assert
//             Assert.Equal(0, bytesRead);
//         }

        /// <summary>
        /// Tests that the ReadAsync method correctly reads data when a valid substream header and data are provided.
        /// </summary>
//         [Fact] [Error] (357-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public async Task ReadAsync_NormalRead_ReturnsCorrectBytes()
//         {
//             // Arrange
//             string expectedText = "World";
//             byte[] data = Encoding.UTF8.GetBytes(expectedText);
//             using var ms = CreateSubstreamMemoryStream(data.Length, data);
//             var reader = new SubstreamReader(ms);
//             byte[] buffer = new byte[10];
// 
//             // Act
//             int bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);
//             int secondRead = await reader.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);
// 
//             // Assert
//             Assert.Equal(data.Length, bytesRead);
//             Assert.Equal(0, secondRead);
//             string actualText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
//             Assert.Equal(expectedText, actualText);
//         }

        /// <summary>
        /// Tests that the ReadAsync method throws an EndOfStreamException when the header is incomplete.
        /// </summary>
//         [Fact] [Error] (380-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public async Task ReadAsync_HeaderIncomplete_ThrowsEndOfStreamException()
//         {
//             // Arrange
//             byte[] incompleteHeader = new byte[] { 0x01, 0x02 };
//             using var ms = new MemoryStream(incompleteHeader);
//             var reader = new SubstreamReader(ms);
//             byte[] buffer = new byte[10];
// 
//             // Act & Assert
//             await Assert.ThrowsAsync<EndOfStreamException>(() => reader.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None));
//         }

        /// <summary>
        /// Tests that calling Dispose sets the IsDisposed property to true.
        /// </summary>
//         [Fact] [Error] (395-30)CS0122 'SubstreamReader' is inaccessible due to its protection level [Error] (401-32)CS0122 'SubstreamReader.IsDisposed' is inaccessible due to its protection level
//         public void Dispose_SetsIsDisposedTrue()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0);
//             var reader = new SubstreamReader(ms);
// 
//             // Act
//             reader.Dispose();
// 
//             // Assert
//             Assert.True(reader.IsDisposed);
//         }

        /// <summary>
        /// Tests that methods throw an ObjectDisposedException after the SubstreamReader has been disposed.
        /// </summary>
//         [Fact] [Error] (412-30)CS0122 'SubstreamReader' is inaccessible due to its protection level
//         public void MethodsAfterDispose_ThrowObjectDisposedException()
//         {
//             // Arrange
//             using var ms = CreateSubstreamMemoryStream(0);
//             var reader = new SubstreamReader(ms);
//             reader.Dispose();
// 
//             // Act & Assert
//             Assert.Throws<ObjectDisposedException>(() => { var _ = reader.Length; });
//             Assert.Throws<ObjectDisposedException>(() => reader.Flush());
//             Assert.Throws<ObjectDisposedException>(() => reader.Seek(0, SeekOrigin.Begin));
//         }
    }
}
