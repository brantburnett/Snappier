using System.IO.Compression;
using System.Text;

namespace Snappier.Tests;

public class SnappyStreamTests(ITestOutputHelper outputHelper)
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
        using Stream resource =
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
        string decompressedText = streamReader.ReadToEnd();

        outputHelper.WriteLine(decompressedText);

        using Stream sourceResource = typeof(SnappyStreamTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.{filename}");
        Assert.NotNull(sourceResource);

        using var streamReader2 = new StreamReader(sourceResource, Encoding.UTF8);
        string sourceText = streamReader2.ReadToEnd();

        Assert.Equal(sourceText, decompressedText);
    }

    [Fact]
    public void CompressAndDecompress_SingleByte()
    {
        using Stream resource =
            typeof(SnappyStreamTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.alice29.txt");
        Assert.NotNull(resource);

        byte[] inBuffer = new byte[128];
        int readBytes = resource.Read(inBuffer, 0, inBuffer.Length);

        using var output = new MemoryStream();

        using (var compressor = new SnappyStream(output, CompressionMode.Compress, true))
        {
            for (int i = 0; i < readBytes; i++)
            {
                compressor.WriteByte(inBuffer[i]);
            }
        }

        output.Position = 0;

        using var decompressor = new SnappyStream(output, CompressionMode.Decompress, true);

        byte[] outBuffer = new byte[128];
        for (int i = 0; i < readBytes; i++)
        {
            outBuffer[i] = (byte)decompressor.ReadByte();
        }

        Assert.Equal(inBuffer, outBuffer);
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
        using Stream resource =
            typeof(SnappyStreamTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.{filename}");
        Assert.NotNull(resource);

        using var output = new MemoryStream();

#if NET6_0_OR_GREATER
        await using (var compressor = new SnappyStream(output, CompressionMode.Compress, true))
        {
            await resource.CopyToAsync(compressor, TestContext.Current.CancellationToken);
        }
#else
        using (var compressor = new SnappyStream(output, CompressionMode.Compress, true))
        {
            await resource.CopyToAsync(compressor);
        }
#endif

        output.Position = 0;

#if NET6_0_OR_GREATER
        await
#endif
        using var decompressor = new SnappyStream(output, CompressionMode.Decompress, true);

        using var streamReader = new StreamReader(decompressor, Encoding.UTF8);
#if NET6_0_OR_GREATER
        string decompressedText = await streamReader.ReadToEndAsync(TestContext.Current.CancellationToken);
#else
        string decompressedText = await streamReader.ReadToEndAsync();
#endif

        outputHelper.WriteLine(decompressedText);

        using Stream sourceResource = typeof(SnappyStreamTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.{filename}");
        Assert.NotNull(sourceResource);

        using var streamReader2 = new StreamReader(sourceResource, Encoding.UTF8);
#if NET6_0_OR_GREATER
        string sourceText = await streamReader2.ReadToEndAsync(TestContext.Current.CancellationToken);
#else
        string sourceText = await streamReader2.ReadToEndAsync();
#endif

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
        Stream resource = typeof(SnappyStreamTests).Assembly.GetManifestResourceStream($"Snappier.Tests.TestData.{filename}");
        using var resourceMem = new MemoryStream();
        resource.CopyTo(resourceMem);
        byte[] originalBytes = resourceMem.ToArray();

        var rand = new Random(123);

        using var compresed = new MemoryStream();
        using (var inputStream = new MemoryStream(originalBytes))
        using (var compressor = new SnappyStream(compresed, CompressionMode.Compress, true))
        {
            // Write lots of small randomly sized chunks to increase change of hitting error conditions.
            byte[] buffer = new byte[100];
            int requestedSize = rand.Next(1, buffer.Length);
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

#if NET6_0_OR_GREATER

    // Test case that we know was failing on decompress with the default 8192 byte chunk size
    [Fact]
    public void Known8192ByteChunkStressTest()
    {
        using Stream resource = typeof(SnappyStreamTests).Assembly.GetManifestResourceStream("Snappier.Tests.TestData.streamerrorsequence.txt")!;
        byte[] originalBytes = ConvertFromHexStream(resource);

        using var compressed = new MemoryStream();
        using SnappyStream compressor = new(compressed, CompressionMode.Compress);

        compressor.Write(originalBytes, 0, originalBytes.Length);
        compressor.Flush();

        compressed.Position = 0;

        using SnappyStream decompressor = new(compressed, CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        decompressor.CopyTo(decompressed);

        Assert.True(decompressed.GetBuffer().AsSpan(0, (int) decompressed.Length).SequenceEqual(originalBytes));
    }

    private static byte[] ConvertFromHexStream(Stream stream)
    {
        using var output = new MemoryStream();

        using var textReader = new StreamReader(stream, Encoding.UTF8);

        char[] buffer = new char[1024];

        int charsRead = textReader.Read(buffer.AsSpan());
        while (charsRead > 0)
        {
            byte[] bytes = Convert.FromHexString(buffer.AsSpan(0, charsRead));
            output.Write(bytes.AsSpan());

            charsRead = textReader.Read(buffer, 0, buffer.Length);
        }

        return output.ToArray();
    }

#endif

    // Test case that we know was failing on decompress with the default 8192 byte chunk size
    [Fact]
    public void UncompressedBlock()
    {
        byte[] originalBytes = [..Enumerable.Range(0, 256).Select(p => (byte)p)];

        using var compressed = new MemoryStream();
        using SnappyStream compressor = new(compressed, CompressionMode.Compress);

        compressor.Write(originalBytes, 0, originalBytes.Length);
        compressor.Flush();

        // Snappy header + block header + uncompressed data
        Assert.Equal(10 + 8 + originalBytes.Length, compressed.Length);

        compressed.Position = 0;

        using SnappyStream decompressor = new(compressed, CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        decompressor.CopyTo(decompressed);

        Assert.True(decompressed.GetBuffer().AsSpan(0, (int)decompressed.Length).SequenceEqual(originalBytes));
    }

    // https://github.com/brantburnett/Snappier/security/advisories/GHSA-pggp-6c3x-2xmx
    [Fact(Timeout = 5000)]
    public async Task MalformedFrameInput()
    {
        byte[] data = [ 0x00, 0x04, 0x00, 0x00, 0x64, 0x4e, 0x6c, 0x71, 0x79, 0x20, 0x77, 0x6f, 0x72, 0x6c, 0x64 ];

        using var src = new MemoryStream(data);
        using SnappyStream decompressor = new(src, CompressionMode.Decompress);

        using var destination = new MemoryStream();

        // Ensure this throws, no infinite loop. The synchronous path runs on a worker thread so the
        // test ends when the timeout is reached, even if a regression makes CopyTo never return.
        await Assert.ThrowsAsync<InvalidDataException>(
            () => Task.Run(() => decompressor.CopyTo(destination), TestContext.Current.CancellationToken));
    }

    // https://github.com/brantburnett/Snappier/security/advisories/GHSA-wc5g-7q4c-9jmw
    [Theory]
    [InlineData(0x00, 0)]
    [InlineData(0x00, 1)]
    [InlineData(0x00, 2)]
    [InlineData(0x00, 3)]
    [InlineData(0x01, 0)]
    [InlineData(0x01, 1)]
    [InlineData(0x01, 2)]
    [InlineData(0x01, 3)]
    public void UndersizedDataChunk_ThrowsInvalidDataException(byte chunkType, byte chunkSize)
    {
        byte[] data =
        [
            // Stream identifier
            0xff, 0x06, 0x00, 0x00, 0x73, 0x4e, 0x61, 0x50, 0x70, 0x59,
            // Data chunk header declaring fewer bytes than its own CRC
            chunkType, chunkSize, 0x00, 0x00,
            // Bytes the decoder would otherwise consume as the CRC, plus one more
            0x01, 0x02, 0x03, 0x04, 0x09,
        ];

        using var src = new MemoryStream(data);
        using SnappyStream decompressor = new(src, CompressionMode.Decompress);

        using var destination = new MemoryStream();

        Assert.Throws<InvalidDataException>(() => decompressor.CopyTo(destination));
    }

    // Builds a framed stream with one compressed data chunk holding a placeholder CRC and the given block.
    private static byte[] CreateCompressedChunkStream(byte[] block)
    {
        int chunkSize = 4 + block.Length;

        return
        [
            // Stream identifier
            0xff, 0x06, 0x00, 0x00, 0x73, 0x4e, 0x61, 0x50, 0x70, 0x59,
            // Compressed data chunk header
            0x00, (byte)chunkSize, (byte)(chunkSize >> 8), (byte)(chunkSize >> 16),
            // CRC, never checked because the block is rejected first
            0x00, 0x00, 0x00, 0x00,
            ..block,
        ];
    }

    [Fact]
    public void CompressedChunk_DeclaredLengthAboveChunkExpansion_ThrowsInvalidDataException()
    {
        // The block declares 1,500,000,000 bytes but holds no tag data
        byte[] data = CreateCompressedChunkStream([0x80, 0xDE, 0xA0, 0xCB, 0x05]);

        using var src = new MemoryStream(data);
        using SnappyStream decompressor = new(src, CompressionMode.Decompress);

#if NET
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
#endif

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => decompressor.ReadByte());
        Assert.Equal("Invalid stream length", ex.Message);

#if NET
        // The declared length is rejected before a buffer is allocated for it
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, 0, 1 << 20);
#endif
    }

    [Fact]
    public void CompressedChunk_DeclaredLengthAbove64KiB_ThrowsInvalidDataException()
    {
        // A 1-byte literal followed by 1024 copies of 64 bytes, each a 3-byte tag, decompresses to 65,537 bytes.
        // The chunk holds enough tag data to produce that, but the framing format limits a chunk to 65,536 bytes.
        const int copies = 1024;
        byte[] block = new byte[3 + 2 + 3 * copies];
        block[0] = 0x81; // Uncompressed length 65,537 as a varint
        block[1] = 0x80;
        block[2] = 0x04;
        block[3] = 0x00; // Literal tag, length 1
        block[4] = (byte)'A';
        for (int i = 0; i < copies; i++)
        {
            block[5 + 3 * i] = 0xFE; // Copy tag with a 2-byte offset, length 64
            block[6 + 3 * i] = 0x01; // Offset 1, little-endian
            block[7 + 3 * i] = 0x00;
        }

        using var src = new MemoryStream(CreateCompressedChunkStream(block));
        using SnappyStream decompressor = new(src, CompressionMode.Decompress);

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => decompressor.ReadByte());
        Assert.Contains("65536", ex.Message);
    }

    [Fact]
    public void CompressAndDecompress_FullSizeChunks_RoundTrips()
    {
        // Highly compressible data fills each chunk with the full 65,536 bytes the framing format allows
        byte[] originalBytes = new byte[5 * 65536 + 17];

        using var compressed = new MemoryStream();
        using (SnappyStream compressor = new(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            compressor.Write(originalBytes, 0, originalBytes.Length);
        }

        compressed.Position = 0;

        using SnappyStream decompressor = new(compressed, CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        decompressor.CopyTo(decompressed);

        Assert.Equal(originalBytes, decompressed.ToArray());
    }

    [Fact]
    public void Decompress_OneByteReads_RoundTrips()
    {
        byte[] originalBytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Snappier one byte at a time. ", 100)));

        using var compressed = new MemoryStream();
        using (SnappyStream compressor = new(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            compressor.Write(originalBytes, 0, originalBytes.Length);
        }

        // Each chunk header and CRC arrives split across several reads
        using var src = new OneByteReadStream(compressed.ToArray());
        using SnappyStream decompressor = new(src, CompressionMode.Decompress);

        using var decompressed = new MemoryStream();
        decompressor.CopyTo(decompressed);

        Assert.Equal(originalBytes, decompressed.ToArray());
    }

    private sealed class OneByteReadStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, Math.Min(count, 1));

#if NET8_0_OR_GREATER
        public override int Read(Span<byte> buffer) =>
            base.Read(buffer.Slice(0, Math.Min(buffer.Length, 1)));
#endif
    }
}
