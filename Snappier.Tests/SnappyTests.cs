using System.Buffers;
using System.Text;

namespace Snappier.Tests;

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
        using Stream resource =
            typeof(SnappyTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.{filename}");
        Assert.NotNull(resource);

        byte[] input = new byte[resource.Length];
        int bytesRead = resource.Read(input, 0, input.Length);

        byte[] compressed = new byte[Snappy.GetMaxCompressedLength(bytesRead)];
        int compressedLength = Snappy.Compress(input.AsSpan(0, bytesRead), compressed);

        Span<byte> compressedSpan = compressed.AsSpan(0, compressedLength);

        byte[] output = new byte[Snappy.GetUncompressedLength(compressedSpan)];
        int outputLength = Snappy.Decompress(compressedSpan, output);

        Assert.Equal(input.Length, outputLength);
        Assert.Equal(input, output);
    }

    [Fact]
    public void CompressAndDecompressFile_LimitedOutputBuffer()
    {
        // Covers the branch where the output buffer is too small to hold the maximum compressed length
        // but is larger than the actual compressed length

        using Stream resource =
            typeof(SnappyTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.alice29.txt");
        Assert.NotNull(resource);

        byte[] input = new byte[65536];
        int bytesRead = resource.Read(input, 0, input.Length);

        byte[] compressed = new byte[Snappy.GetMaxCompressedLength(bytesRead) - 5];
        int compressedLength = Snappy.Compress(input.AsSpan(0, bytesRead), compressed);

        Span<byte> compressedSpan = compressed.AsSpan(0, compressedLength);

        byte[] output = new byte[Snappy.GetUncompressedLength(compressedSpan)];
        int outputLength = Snappy.Decompress(compressedSpan, output);

        Assert.Equal(input.Length, outputLength);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Compress_InsufficientOutputBuffer()
    {
        using Stream resource =
            typeof(SnappyTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.alice29.txt");
        Assert.NotNull(resource);

        byte[] input = new byte[65536];
        int bytesRead = resource.Read(input, 0, input.Length);

        byte[] compressed = new byte[1024];
        Assert.Throws<ArgumentException>(() => Snappy.Compress(input.AsSpan(0, bytesRead), compressed));
    }

    [Fact]
    public void TryCompressAndDecompress()
    {
        using Stream resource =
            typeof(SnappyTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.alice29.txt");
        Assert.NotNull(resource);

        byte[] input = new byte[65536];
        int bytesRead = resource.Read(input, 0, input.Length);

        byte[] compressed = new byte[Snappy.GetMaxCompressedLength(bytesRead)];
        bool result  = Snappy.TryCompress(input.AsSpan(0, bytesRead), compressed, out int compressedLength);
        Assert.True(result);

        Span<byte> compressedSpan = compressed.AsSpan(0, compressedLength);

        byte[] output = new byte[Snappy.GetUncompressedLength(compressedSpan)];
        result = Snappy.TryDecompress(compressedSpan, output, out int outputLength);
        Assert.True(result);

        Assert.Equal(input.Length, outputLength);
        Assert.Equal(input, output);
    }

    [Fact]
    public void TryCompress_InsufficientOutputBuffer()
    {
        using Stream resource =
            typeof(SnappyTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.alice29.txt");
        Assert.NotNull(resource);

        byte[] input = new byte[65536];
        int bytesRead = resource.Read(input, 0, input.Length);

        byte[] compressed = new byte[1024];
        bool result = Snappy.TryCompress(input.AsSpan(0, bytesRead), compressed, out _);

        Assert.False(result);
    }

#if NET6_0_OR_GREATER

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
    public void CompressAndDecompressFile_ViaBufferWriter(string filename)
    {
        using Stream resource =
            typeof(SnappyTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.{filename}");
        Assert.NotNull(resource);

        byte[] input = new byte[resource.Length];
        int bytesRead = resource.Read(input, 0, input.Length);

        var compressed = new ArrayBufferWriter<byte>();
        Snappy.Compress(new ReadOnlySequence<byte>(input).Slice(0, bytesRead), compressed);

        var output = new ArrayBufferWriter<byte>(); // new byte[Snappy.GetUncompressedLength(compressedSpan)];
        Snappy.Decompress(new ReadOnlySequence<byte>(compressed.WrittenMemory), output);

        Assert.Equal(input.Length, output.WrittenCount);
        Assert.True(input.AsSpan().SequenceEqual(output.WrittenSpan));
    }

    [Theory]
    [InlineData(16384)]
    [InlineData(32768)]
    [InlineData(65536)]
    public void CompressAndDecompressFile_ViaBufferWriter_SplitInput(int maxSegmentSize)
    {
        using Stream resource =
            typeof(SnappyTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.alice29.txt");
        Assert.NotNull(resource);

        byte[] input = new byte[resource.Length];
        int bytesRead = resource.Read(input, 0, input.Length);

        var compressed = new ArrayBufferWriter<byte>();
        Snappy.Compress(SequenceHelpers.CreateSequence(input.AsMemory(0, bytesRead), maxSegmentSize), compressed);

        var output = new ArrayBufferWriter<byte>(); // new byte[Snappy.GetUncompressedLength(compressedSpan)];
        Snappy.Decompress(SequenceHelpers.CreateSequence(compressed.WrittenMemory, maxSegmentSize), output);

        Assert.Equal(input.Length, output.WrittenCount);
        Assert.True(input.AsSpan().SequenceEqual(output.WrittenSpan));
    }

#endif

    public static TheoryData<string> CompressAndDecompressStringCases() =>
    [
        "",
        "a",
        "ab",
        "abc",
        "aaaaaaa" + new string('b', 16) + "aaaaaabc",
        "aaaaaaa" + new string('b', 256) + "aaaaaabc",
        "aaaaaaa" + new string('b', 2047) + "aaaaaabc",
        "aaaaaaa" + new string('b', 65536) + "aaaaaabc",
        "abcaaaaaaa" + new string('b', 65536) + "aaaaaabc"
    ];

    [Theory]
    [MemberData(nameof(CompressAndDecompressStringCases))]
    public void CompressAndDecompressString(string str)
    {
        byte[] input = Encoding.UTF8.GetBytes(str);

        byte[] compressed = Snappy.CompressToArray(input);
        byte[] output = Snappy.DecompressToArray(compressed);

        Assert.Equal(input.Length, output.Length);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Compress_OverlappingBuffers_InvalidOperationException()
    {
        byte[] input = new byte[1024];

        Assert.Throws<InvalidOperationException>(() => Snappy.Compress(input, input.AsSpan(input.Length - 1)));
    }

    [Fact]
    public void BadData_InsufficentOutputBuffer_ThrowsArgumentException()
    {
        byte[] input = new byte[100000];
        ArrayFill(input, (byte) 'A');

        byte[] compressed = new byte[Snappy.GetMaxCompressedLength(input.Length)];
        int compressedLength = Snappy.Compress(input, compressed);

        Assert.Throws<ArgumentException>(() =>
        {
            byte[] output = new byte[100];
            Snappy.Decompress(compressed.AsSpan(0, compressedLength), output);
        });
    }

    [Fact]
    public void TryDecompress_InsufficentOutputBuffer_ReturnsFalse()
    {
        byte[] input = new byte[100000];
        ArrayFill(input, (byte)'A');

        byte[] compressed = new byte[Snappy.GetMaxCompressedLength(input.Length)];
        int compressedLength = Snappy.Compress(input, compressed);

        byte[] output = new byte[100];
        bool result = Snappy.TryDecompress(compressed.AsSpan(0, compressedLength), output, out _);

        Assert.False(result);
    }

    [Fact]
    public void BadData_SimpleCorruption_ThrowsInvalidDataException()
    {
        byte[] input = Encoding.UTF8.GetBytes("making sure we don't crash with corrupted input");

        byte[] compressed = new byte[Snappy.GetMaxCompressedLength(input.Length)];
        int compressedLength = Snappy.Compress(input, compressed);
        Span<byte> compressedSpan = compressed.AsSpan(0, compressedLength);

        // corrupt the data a bit
        compressedSpan[1]--;
        compressedSpan[3]++;

        Assert.Throws<InvalidDataException>(() =>
        {
            int length = Snappy.GetUncompressedLength(compressed.AsSpan(0, compressedLength));
            Assert.InRange(length, 0, 1 << 20);

            byte[] output = new byte[length];
            Snappy.Decompress(compressed.AsSpan(0, compressedLength), output);
        });
    }

    [Fact]
    public void BadData_LongLength_ThrowsInvalidDataException()
    {
        byte[] input = new byte[1000];
        ArrayFill(input, (byte) 'A');

        byte[] compressed = new byte[Snappy.GetMaxCompressedLength(input.Length)];
        int compressedLength = Snappy.Compress(input, compressed);
        Span<byte> compressedSpan = compressed.AsSpan(0, compressedLength);

        // Set the length header to 16383
        compressedSpan[0] = 255;
        compressedSpan[1] = 127;

        Assert.Throws<InvalidDataException>(() =>
        {
            byte[] output = new byte[1000];
            Snappy.Decompress(compressed, output);
        });
    }

    // Uncompressed length 10, then a literal tag (0x44) declaring 18 bytes, but only 10 bytes of input follow.
    // https://github.com/brantburnett/Snappier/issues/152
    private static readonly byte[] TruncatedLiteralBlock =
        [0x0a, 0x44, 0x01, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x02];

    // Uncompressed length 10, then a literal tag (0x34) declaring 14 bytes, with all 14 bytes present.
    private static readonly byte[] LiteralLongerThanUncompressedLengthBlock =
        [0x0a, 0x34, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e];

    public static TheoryData<string> LiteralOverrunBlocks =>
        [nameof(TruncatedLiteralBlock), nameof(LiteralLongerThanUncompressedLengthBlock)];

    private static byte[] GetLiteralOverrunBlock(string name) => name switch
    {
        nameof(TruncatedLiteralBlock) => TruncatedLiteralBlock,
        nameof(LiteralLongerThanUncompressedLengthBlock) => LiteralLongerThanUncompressedLengthBlock,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(LiteralOverrunBlocks))]
    public void BadData_LiteralOverrun_Decompress_ThrowsInvalidDataException(string blockName)
    {
        byte[] compressed = GetLiteralOverrunBlock(blockName);

        Assert.Equal(10, Snappy.GetUncompressedLength(compressed));

        byte[] output = new byte[10];
        Assert.Throws<InvalidDataException>(() => Snappy.Decompress(compressed, output));
    }

    [Theory]
    [MemberData(nameof(LiteralOverrunBlocks))]
    public void BadData_LiteralOverrun_TryDecompress_ThrowsInvalidDataException(string blockName)
    {
        byte[] compressed = GetLiteralOverrunBlock(blockName);

        byte[] output = new byte[10];
        Assert.Throws<InvalidDataException>(() => Snappy.TryDecompress(compressed, output, out _));
    }

    [Theory]
    [MemberData(nameof(LiteralOverrunBlocks))]
    public void BadData_LiteralOverrun_DecompressToMemory_ThrowsInvalidDataException(string blockName)
    {
        byte[] compressed = GetLiteralOverrunBlock(blockName);

        Assert.Throws<InvalidDataException>(() => Snappy.DecompressToMemory(compressed).Dispose());
    }

    [Theory]
    [MemberData(nameof(LiteralOverrunBlocks))]
    public void BadData_LiteralOverrun_DecompressToArray_ThrowsInvalidDataException(string blockName)
    {
        byte[] compressed = GetLiteralOverrunBlock(blockName);

        Assert.Throws<InvalidDataException>(() => Snappy.DecompressToArray(compressed));
    }

    [Theory]
    [MemberData(nameof(LiteralOverrunBlocks))]
    public void BadData_LiteralOverrun_SplitSequence_ThrowsInvalidDataException(string blockName)
    {
        byte[] compressed = GetLiteralOverrunBlock(blockName);

        // Split into segments of every size so the tag and literal land in scratch and span segment boundaries
        for (int segmentSize = 1; segmentSize <= compressed.Length; segmentSize++)
        {
            ReadOnlySequence<byte> sequence = SequenceHelpers.CreateSequence(compressed, segmentSize);

            Assert.Throws<InvalidDataException>(() => Snappy.DecompressToMemory(sequence).Dispose());
            Assert.Throws<InvalidDataException>(() => Snappy.Decompress(sequence, new ArrayBufferWriter<byte>()));
        }
    }

    [Theory]
    [InlineData("baddata1.snappy")]
    [InlineData("baddata2.snappy")]
    [InlineData("baddata3.snappy")]
    public void BadData_FromFile_ThrowsInvalidDataException(string filename)
    {
        using Stream resource =
            typeof(SnappyTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.{filename}");
        Assert.NotNull(resource);

        byte[] input = new byte[resource.Length];
        int bytesRead = resource.Read(input, 0, input.Length);

        Assert.Throws<InvalidDataException>(() =>
        {
            int length = Snappy.GetUncompressedLength(input.AsSpan(0, bytesRead));
            Assert.InRange(length, 0, 1 << 20);

            byte[] output = new byte[length];
            Snappy.Decompress(input.AsSpan(0, bytesRead), output);
        });
    }

    [Theory]
    [InlineData("baddata1.snappy")]
    [InlineData("baddata2.snappy")]
    [InlineData("baddata3.snappy")]
    public void BadData_TryDecompress_ThrowsInvalidDataException(string filename)
    {
        using Stream resource =
            typeof(SnappyTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.{filename}");
        Assert.NotNull(resource);

        byte[] input = new byte[resource.Length];
        int bytesRead = resource.Read(input, 0, input.Length);

        Assert.Throws<InvalidDataException>(() =>
        {
            int length = Snappy.GetUncompressedLength(input.AsSpan(0, bytesRead));
            Assert.InRange(length, 0, 1 << 20);

            byte[] output = new byte[length];
            Snappy.TryDecompress(input.AsSpan(0, bytesRead), output, out _);
        });
    }

    [Fact]
    public void DecompressToMemory()
    {
        using Stream resource =
            typeof(SnappyTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.alice29.txt");
        Assert.NotNull(resource);

        byte[] input = new byte[resource.Length];
        int bytesRead = resource.Read(input, 0, input.Length);

        byte[] compressed = new byte[Snappy.GetMaxCompressedLength(bytesRead)];
        int compressedLength = Snappy.Compress(input.AsSpan(0, bytesRead), compressed);

        Span<byte> compressedSpan = compressed.AsSpan(0, compressedLength);

        using IMemoryOwner<byte> output = Snappy.DecompressToMemory(compressedSpan);

        Assert.Equal(bytesRead, output.Memory.Length);
        Assert.True(input.AsSpan(0, bytesRead).SequenceEqual(output.Memory.Span));
    }

    [Fact]
    public void DecompressToMemory_FromSequence()
    {
        using Stream resource =
            typeof(SnappyTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.alice29.txt");
        Assert.NotNull(resource);

        byte[] input = new byte[resource.Length];
        int bytesRead = resource.Read(input, 0, input.Length);

        byte[] compressed = new byte[Snappy.GetMaxCompressedLength(bytesRead)];
        int compressedLength = Snappy.Compress(input.AsSpan(0, bytesRead), compressed);

        ReadOnlySequence<byte> compressedSequence = SequenceHelpers.CreateSequence(compressed.AsMemory(0, compressedLength), 1024);

        using IMemoryOwner<byte> output = Snappy.DecompressToMemory(compressedSequence);

        Assert.Equal(bytesRead, output.Memory.Length);
        Assert.True(input.AsSpan(0, bytesRead).SequenceEqual(output.Memory.Span));
    }

#if NET6_0_OR_GREATER

    [Fact]
    public void DecompressToBufferWriter_FromSequence()
    {
        using Stream resource =
            typeof(SnappyTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.alice29.txt");
        Assert.NotNull(resource);

        byte[] input = new byte[resource.Length];
        int bytesRead = resource.Read(input, 0, input.Length);

        byte[] compressed = new byte[Snappy.GetMaxCompressedLength(bytesRead)];
        int compressedLength = Snappy.Compress(input.AsSpan(0, bytesRead), compressed);

        ReadOnlySequence<byte> compressedSequence = SequenceHelpers.CreateSequence(compressed.AsMemory(0, compressedLength), 1024);

        var writer = new ArrayBufferWriter<byte>();
        Snappy.Decompress(compressedSequence, writer);

        Assert.Equal(bytesRead, writer.WrittenCount);
        Assert.True(input.AsSpan(0, bytesRead).SequenceEqual(writer.WrittenSpan));
    }

#endif

    [Fact]
    public void RandomData()
    {
        var rng = new Random(301);

        for (int i = 0; i < 20000; i++)
        {
            int length = rng.Next(0, 4095);
            if (i < 100)
            {
                length = 65536 + rng.Next(0, 65535);
            }

            byte[] buffer = new byte[length];
            int size = 0;
            while (size < length)
            {
                int runLength = 1;
                if (rng.Next(0, 9) == 0)
                {
                    int skewedBits = rng.Next(0, 8);

                    runLength = rng.Next(0, (1 << skewedBits) - 1);
                }

                byte c = (byte) rng.Next(0, 255);

                if (i >= 100)
                {
                    int skewedBits = rng.Next(0, 3);

                    c = (byte)rng.Next(0, (1 << skewedBits) - 1);
                }

                ArrayFill(buffer, c, size, Math.Min(runLength, length - size));
                size += runLength;
            }

            using IMemoryOwner<byte> compressed = Snappy.CompressToMemory(buffer);

            using IMemoryOwner<byte> decompressed = Snappy.DecompressToMemory(compressed.Memory.Span);

            Assert.Equal(buffer.Length, decompressed.Memory.Length);
            Assert.Equal(buffer, decompressed.Memory.ToArray());
        }
    }

    private static void ArrayFill(byte[] array, byte value)
    {
#if NET48
        ArrayFill(array, value, 0, array.Length);
#else
        Array.Fill(array, value);
#endif
    }

    private static void ArrayFill(byte[] array, byte value, int startIndex, int count)
    {
#if NET48
        for (int i = startIndex; i < startIndex + count; i++)
        {
            array[i] = value;
        }
#else
        Array.Fill(array, value, startIndex, count);
#endif
    }
}
