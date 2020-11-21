using System;
using System.Linq;
using Snappier.Internal;
using Xunit;

namespace Snappier.Tests.Internal
{
    public class SnappyDecompressorTests
    {
        #region DecompressAllTags

        [Fact]
        public void DecompressAllTags_ShortInputBufferWhichCopiesToScratch_DoesNotReadPastEndOfScratch()
        {
            // Arrange

            var decompressor = new SnappyDecompressor();
            decompressor.SetExpectedLengthForTest(1024);

            decompressor.WriteToBufferForTest(Enumerable.Range(0, 255).Select(p => (byte) p).ToArray());

            // if in error, decompressor will read the 222, 0, 0 as the next tag and throw a copy offset exception
            decompressor.LoadScratchForTest(new byte[] { 222, 222, 222, 222, 0, 0 }, 0);

            // Act

            decompressor.DecompressAllTags(new byte[] { 150, 255, 0 });

        }

        #endregion

        #region ExtractData

        [Fact]
        public void ExtractData_NoLength_InvalidOperationException()
        {
            // Arrange

            using var decompressor = new SnappyDecompressor();

            // Act/Assert

            var ex = Assert.Throws<InvalidOperationException>(() => decompressor.ExtractData());

            Assert.Equal("No data present.", ex.Message);
        }

        [Fact]
        public void ExtractData_NotFullDecompressed_InvalidOperationException()
        {
            // Arrange

            using var decompressor = new SnappyDecompressor();

            using var compressed = Snappy.CompressToMemory(new byte[] {1, 2, 3, 4});

            // Only length is forwarded
            decompressor.Decompress(compressed.Memory.Span.Slice(0, 1));

            // Act/Assert

            var ex = Assert.Throws<InvalidOperationException>(() => decompressor.ExtractData());
            Assert.Equal("Block is not fully decompressed.", ex.Message);
        }

        [Fact]
        public void ExtractData_ZeroLength_EmptyMemory()
        {
            // Arrange

            using var decompressor = new SnappyDecompressor();

            using var compressed = Snappy.CompressToMemory(Array.Empty<byte>());

            decompressor.Decompress(compressed.Memory.Span);

            // Act

            using var result = decompressor.ExtractData();

            // Assert

            Assert.Equal(0, result.Memory.Length);
        }

        [Fact]
        public void ExtractData_SomeData_Memory()
        {
            // Arrange

            using var decompressor = new SnappyDecompressor();

            using var compressed = Snappy.CompressToMemory(new byte[] {1, 2, 3, 4});

            decompressor.Decompress(compressed.Memory.Span);

            // Act

            using var result = decompressor.ExtractData();

            // Assert

            Assert.Equal(4, result.Memory.Length);
        }

        [Fact]
        public void ExtractData_SomeData_DoesResetForReuse()
        {
            // Arrange

            using var decompressor = new SnappyDecompressor();

            using var compressed = Snappy.CompressToMemory(new byte[] {1, 2, 3, 4});
            using var compressed2 = Snappy.CompressToMemory(new byte[] {4, 3, 2, 1});

            decompressor.Decompress(compressed.Memory.Span);

            // Act

            using var result = decompressor.ExtractData();

            decompressor.Decompress(compressed2.Memory.Span);

            using var result2 = decompressor.ExtractData();

            // Assert

            Assert.Equal(4, result2.Memory.Length);
            Assert.Equal(new byte[] {4, 3, 2, 1}, result2.Memory.ToArray() );
        }

        #endregion
    }
}
