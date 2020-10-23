using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        public void CompressAndDecompressFile(string filename)
        {
            using var resource =
                typeof(SnappyTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.{filename}");
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

        public static IEnumerable<object[]> CompressAndDecompressStringCases()
        {
            yield return new object[] {""};
            yield return new object[] {"a"};
            yield return new object[] {"ab"};
            yield return new object[] {"abc"};

            yield return new object[] {"aaaaaaa" + new string('b', 16) + "aaaaaabc"};
            yield return new object[] {"aaaaaaa" + new string('b', 256) + "aaaaaabc"};
            yield return new object[] {"aaaaaaa" + new string('b', 2047) + "aaaaaabc"};
            yield return new object[] {"aaaaaaa" + new string('b', 65536) + "aaaaaabc"};
            yield return new object[] {"abcaaaaaaa" + new string('b', 65536) + "aaaaaabc"};
        }

        [Theory]
        [MemberData(nameof(CompressAndDecompressStringCases))]
        public void CompressAndDecompressString(string str)
        {
            var input = Encoding.UTF8.GetBytes(str);

            var compressed = Snappy.CompressToArray(input);
            var output = Snappy.DecompressToArray(compressed);

            Assert.Equal(input.Length, output.Length);
            Assert.Equal(input, output);
        }

        [Fact]
        public void BadData_InsufficentOutputBuffer_ThrowsArgumentException()
        {
            var input = new byte[100000];
            Array.Fill(input, (byte) 'A');

            var compressed = new byte[Snappy.GetMaxCompressedLength(input.Length)];
            var compressedLength = Snappy.Compress(input, compressed);

            Assert.Throws<ArgumentException>(() =>
            {
                var output = new byte[100];
                Snappy.Decompress(compressed.AsSpan(0, compressedLength), output);
            });
        }

        [Fact]
        public void BadData_SimpleCorruption_ThrowsInvalidDataException()
        {
            var input = Encoding.UTF8.GetBytes("making sure we don't crash with corrupted input");

            var compressed = new byte[Snappy.GetMaxCompressedLength(input.Length)];
            var compressedLength = Snappy.Compress(input, compressed);
            var compressedSpan = compressed.AsSpan(0, compressedLength);

            // corrupt the data a bit
            compressedSpan[1]--;
            compressedSpan[3]++;

            Assert.Throws<InvalidDataException>(() =>
            {
                var length = Snappy.GetUncompressedLength(compressed.AsSpan(0, compressedLength));
                Assert.InRange(length, 0, 1 << 20);

                var output = new byte[length];
                Snappy.Decompress(compressed.AsSpan(0, compressedLength), output);
            });
        }

        [Fact]
        public void BadData_LongLength_ThrowsInvalidDataException()
        {
            var input = new byte[1000];
            Array.Fill(input, (byte) 'A');

            var compressed = new byte[Snappy.GetMaxCompressedLength(input.Length)];
            var compressedLength = Snappy.Compress(input, compressed);
            var compressedSpan = compressed.AsSpan(0, compressedLength);

            // Set the length header to 16383
            compressedSpan[0] = 255;
            compressedSpan[1] = 127;

            Assert.Throws<InvalidDataException>(() =>
            {
                var output = new byte[1000];
                Snappy.Decompress(compressed, output);
            });
        }

        [Theory]
        [InlineData("baddata1.snappy")]
        [InlineData("baddata2.snappy")]
        [InlineData("baddata3.snappy")]
        public void BadData_FromFile_ThrowsInvalidDataException(string filename)
        {
            using var resource =
                typeof(SnappyTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.{filename}");
            Assert.NotNull(resource);

            var input = new byte[resource.Length];
            resource.Read(input);

            Assert.Throws<InvalidDataException>(() =>
            {
                var length = Snappy.GetUncompressedLength(input);
                Assert.InRange(length, 0, 1 << 20);

                var output = new byte[length];
                Snappy.Decompress(input, output);
            });
        }
    }
}
