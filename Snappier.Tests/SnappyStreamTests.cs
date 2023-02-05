using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Snappier.Tests
{
    public class SnappyStreamTests
    {
        private readonly ITestOutputHelper _outputHelper;

        public SnappyStreamTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

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

            using var output = new MemoryStream();

            using (var compressor = new SnappyStream(output, CompressionMode.Compress, true))
            {
                resource.CopyTo(compressor);
            }

            output.Position = 0;

            using var decompressor = new SnappyStream(output, CompressionMode.Decompress, true);

            using var streamReader = new StreamReader(decompressor, Encoding.UTF8);
            var decompressedText = streamReader.ReadToEnd();

            _outputHelper.WriteLine(decompressedText);

            using var sourceResource = typeof(SnappyStreamTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.{filename}");
            Assert.NotNull(sourceResource);

            using var streamReader2 = new StreamReader(sourceResource, Encoding.UTF8);
            var sourceText = streamReader2.ReadToEnd();

            Assert.Equal(sourceText, decompressedText);
        }

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
        public async Task CompressAndDecompressAsync(string filename)
        {
            using var resource =
                typeof(SnappyStreamTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.{filename}");
            Assert.NotNull(resource);

            using var output = new MemoryStream();

            using (var compressor = new SnappyStream(output, CompressionMode.Compress, true))
            {
                await resource.CopyToAsync(compressor);
            }

            output.Position = 0;

            using var decompressor = new SnappyStream(output, CompressionMode.Decompress, true);

            using var streamReader = new StreamReader(decompressor, Encoding.UTF8);
            var decompressedText = await streamReader.ReadToEndAsync();

            _outputHelper.WriteLine(decompressedText);

            using var sourceResource = typeof(SnappyStreamTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.{filename}");
            Assert.NotNull(sourceResource);

            using var streamReader2 = new StreamReader(sourceResource, Encoding.UTF8);
            var sourceText = await streamReader2.ReadToEndAsync();

            Assert.Equal(sourceText, decompressedText);
        }

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
        // Test writing lots of small chunks to catch errors where reading needs to break mid-chunk.
        public void CompressAndDecompressChunkStressTest(string filename)
        {
            var resource = typeof(SnappyStreamTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.{filename}");
            using var resourceMem = new MemoryStream();
            resource.CopyTo(resourceMem);
            var originalBytes = resourceMem.ToArray();

            var rand = new Random(123);

            using var compresed = new MemoryStream();
            using (var inputStream = new MemoryStream(originalBytes))
            using (var compressor = new SnappyStream(compresed, CompressionMode.Compress, true))
            {
                // Write lots of small randomly sized chunks to increase change of hitting error conditions.
                byte[] buffer = new byte[100];
                var requestedSize = rand.Next(1, buffer.Length);
                int n;
                while ((n = inputStream.Read(buffer, 0, requestedSize)) != 0)
                {
                    compressor.Write(buffer, 0, n);
                    // Flush after every write so we get lots of small chunks in the compressed output.
                    compressor.Flush();
                }
            }
            compresed.Position = 0;

            using var decompressed = new MemoryStream();
            using (var decompressor = new SnappyStream(compresed, CompressionMode.Decompress, true))
            {
                decompressor.CopyTo(decompressed);
            }

            Assert.Equal(originalBytes.Length, decompressed.Length);
            Assert.Equal(originalBytes, decompressed.ToArray());
        }
    }
}
