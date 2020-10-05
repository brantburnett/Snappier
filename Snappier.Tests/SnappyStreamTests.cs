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

        [Fact]
        public void CompressAlice()
        {
            using var resource =
                typeof(Basic).Assembly.GetManifestResourceStream("Snappier.Tests.TestData.alice29.txt");
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

            using var sourceResource = typeof(Basic).Assembly.GetManifestResourceStream("Snappier.Tests.TestData.alice29.txt");
            Assert.NotNull(sourceResource);

            using var streamReader2 = new StreamReader(sourceResource, Encoding.UTF8);
            var sourceText = streamReader2.ReadToEnd();

            Assert.Equal(sourceText, decompressedText);
        }

        [Fact]
        public async Task CompressAliceAsync()
        {
            using var resource =
                typeof(Basic).Assembly.GetManifestResourceStream("Snappier.Tests.TestData.alice29.txt");
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

            using var sourceResource = typeof(Basic).Assembly.GetManifestResourceStream("Snappier.Tests.TestData.alice29.txt");
            Assert.NotNull(sourceResource);

            using var streamReader2 = new StreamReader(sourceResource, Encoding.UTF8);
            var sourceText = await streamReader2.ReadToEndAsync();

            Assert.Equal(sourceText, decompressedText);
        }
    }
}
