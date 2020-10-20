using System;
using Xunit;

namespace Snappier.Tests
{
    public class SnappyTests
    {
        [Theory]
        [InlineData("alice29.txt")]
        [InlineData("asyoulik.txt")]
        [InlineData("fireworks.jpeg")]
        [InlineData("geo.protodata")]
        [InlineData("html")]
        [InlineData("html_x_4")]
        [InlineData("kppkn.gtb")]
        [InlineData("lcet10.txt")]
        [InlineData("paper-100k.pdf")]
        [InlineData("plrabn12.txt")]
        [InlineData("urls.10K")]
        public void CompressAndDecompress(string filename)
        {
            using var resource =
                typeof(SnappyStreamTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.{filename}");
            Assert.NotNull(resource);

            var input = new byte[resource.Length];
            resource.Read(input);

            var compressed = new byte[Snappy.GetMaxCompressedLength(input.Length)];
            var compressedLength = Snappy.Compress(input, compressed);

            var compressedSpan = compressed.AsSpan(0, compressedLength);

            var output = new byte[Snappy.GetUncompressedLength(compressedSpan)];
            var outputLength = Snappy.Decompress(compressedSpan, output);

            Assert.Equal(input.Length, outputLength);
            Assert.Equal(input, output);
        }
    }
}
