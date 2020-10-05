using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Snappier.Tests
{
    public class Basic
    {
        private readonly ITestOutputHelper _outputHelper;

        public Basic(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        [Fact]
        public void Alice()
        {
            using var resource =
                typeof(Basic).Assembly.GetManifestResourceStream("Snappier.Tests.TestData.alice29.snappy");
            Assert.NotNull(resource);

            using var stream = new SnappyStream(resource, CompressionMode.Decompress, true);

            using var streamReader = new StreamReader(stream, Encoding.UTF8);
            var decompressedText = streamReader.ReadToEnd();

            _outputHelper.WriteLine(decompressedText);

            using var sourceResource = typeof(Basic).Assembly.GetManifestResourceStream("Snappier.Tests.TestData.alice29.txt");
            Assert.NotNull(sourceResource);

            using var streamReader2 = new StreamReader(sourceResource, Encoding.UTF8);
            var sourceText = streamReader2.ReadToEnd();

            Assert.Equal(sourceText, decompressedText);
        }

        [Fact]
        public void HtmlX4()
        {
            using var resource =
                typeof(Basic).Assembly.GetManifestResourceStream("Snappier.Tests.TestData.html_x_4.snappy");
            Assert.NotNull(resource);

            using var stream = new SnappyStream(resource, CompressionMode.Decompress, true);

            using var streamReader = new StreamReader(stream, Encoding.UTF8);
            var decompressedText = streamReader.ReadToEnd();

            _outputHelper.WriteLine(decompressedText);

            using var sourceResource = typeof(Basic).Assembly.GetManifestResourceStream("Snappier.Tests.TestData.html_x_4");
            Assert.NotNull(sourceResource);

            using var streamReader2 = new StreamReader(sourceResource, Encoding.UTF8);
            var sourceText = streamReader2.ReadToEnd();

            Assert.Equal(sourceText, decompressedText);
        }

        [Fact]
        public void Alice100()
        {
            var outputStream = new MemoryStream();

            for (var i = 0; i < 100; i++)
            {
                using var resource =
                    typeof(Basic).Assembly.GetManifestResourceStream("Snappier.Tests.TestData.alice29.snappy");
                Assert.NotNull(resource);

                using var stream = new SnappyStream(resource, CompressionMode.Decompress, true);

                outputStream.Position = 0;
                stream.CopyTo(outputStream);
            }
        }

        [Fact]
        public void Alice_PInvoke()
        {
            using var resource =
                typeof(Basic).Assembly.GetManifestResourceStream("Snappier.Tests.TestData.alice29.snappy");
            Assert.NotNull(resource);

            using var stream = new Snappy.SnappyStream(resource, CompressionMode.Decompress, true);

            using var streamReader = new StreamReader(stream, Encoding.UTF8);
            var decompressedText = streamReader.ReadToEnd();

            using var sourceResource = typeof(Basic).Assembly.GetManifestResourceStream("Snappier.Tests.TestData.alice29.txt");
            Assert.NotNull(sourceResource);

            using var streamReader2 = new StreamReader(sourceResource, Encoding.UTF8);
            var sourceText = streamReader2.ReadToEnd();

            Assert.Equal(sourceText, decompressedText);
        }
    }
}
