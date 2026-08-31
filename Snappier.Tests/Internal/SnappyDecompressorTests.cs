using System.Buffers;

namespace Snappier.Tests.Internal;

public class SnappyDecompressorTests
{
    #region Decompress

    [Fact]
    public void Decompress_SplitLength_Succeeds()
    {
        // Arrange

        using var decompressor = new SnappyDecompressor();

        // Requires 3 bytes to varint encode the length
        byte[] data = new byte[65536];
        using IMemoryOwner<byte> compressed = Snappy.CompressToMemory(data);

        // Act

        OperationStatus status = decompressor.Decompress(compressed.Memory.Span.Slice(0, 1), out int bytesConsumed);
        Assert.Equal(OperationStatus.NeedMoreData, status);
        Assert.Equal(0, bytesConsumed);
        Assert.True(decompressor.NeedMoreData);
        status = decompressor.Decompress(compressed.Memory.Span.Slice(0, 2), out bytesConsumed);
        Assert.Equal(OperationStatus.NeedMoreData, status);
        Assert.Equal(0, bytesConsumed);
        Assert.True(decompressor.NeedMoreData);
        status = decompressor.Decompress(compressed.Memory.Span, out bytesConsumed);
        Assert.Equal(OperationStatus.Done, status);
        Assert.Equal(compressed.Memory.Length, bytesConsumed);
        Assert.False(decompressor.NeedMoreData);

        using IMemoryOwner<byte> result = decompressor.ExtractData();

        // Assert

        Assert.Equal(65536, result.Memory.Length);
        Assert.True(result.Memory.Span.SequenceEqual(data));
    }

    [Fact]
    public void Decompress_TrailingData_StopsAtEndOfBlock()
    {
        // Arrange

        using var decompressor = new SnappyDecompressor();
        using IMemoryOwner<byte> compressed = Snappy.CompressToMemory(new byte[65536]);
        using IMemoryOwner<byte> trailingBlock = Snappy.CompressToMemory([1, 2, 3, 4]);
        byte[] input = [.. compressed.Memory.ToArray(), .. trailingBlock.Memory.ToArray()];

        // Act

        OperationStatus status = decompressor.Decompress(input, out int bytesConsumed);

        // Assert

        Assert.Equal(OperationStatus.Done, status);
        Assert.Equal(compressed.Memory.Length, bytesConsumed);
    }

    [Fact]
    public void Decompress_OneNewByteAtATime_ConsumesAvailableData()
    {
        // Arrange

        using var decompressor = new SnappyDecompressor();
        byte[] data = Enumerable.Range(0, 1024).Select(value => (byte)value).ToArray();
        using IMemoryOwner<byte> compressed = Snappy.CompressToMemory(data);

        // Act/Assert

        int inputStart = 0;
        for (int inputEnd = 1; inputEnd <= compressed.Memory.Length; inputEnd++)
        {
            OperationStatus status = decompressor.Decompress(
                compressed.Memory.Span.Slice(inputStart, inputEnd - inputStart), out int bytesConsumed);
            inputStart += bytesConsumed;

            Assert.Equal(inputEnd == compressed.Memory.Length ? OperationStatus.Done : OperationStatus.NeedMoreData,
                status);
        }

        Assert.Equal(compressed.Memory.Length, inputStart);

        using IMemoryOwner<byte> result = decompressor.ExtractData();
        Assert.True(result.Memory.Span.SequenceEqual(data));
    }

    [Fact]
    public void Decompress_InvalidData_ReturnsInvalidDataUntilReset()
    {
        // Arrange

        using var decompressor = new SnappyDecompressor();
        using IMemoryOwner<byte> compressed = Snappy.CompressToMemory([1, 2, 3, 4]);

        // Act/Assert

        Assert.Equal(OperationStatus.InvalidData, decompressor.Decompress([1, 1, 0], out _));
        Assert.Equal(OperationStatus.InvalidData, decompressor.Decompress(compressed.Memory.Span, out int bytesConsumed));
        Assert.Equal(0, bytesConsumed);

        decompressor.Reset();

        Assert.Equal(OperationStatus.Done, decompressor.Decompress(compressed.Memory.Span, out bytesConsumed));
        Assert.Equal(compressed.Memory.Length, bytesConsumed);
    }

    [Fact]
    public void Decompress_LiteralLongerThanExpected_ReturnsInvalidData()
    {
        using var decompressor = new SnappyDecompressor();
        byte[] input = [1, 4, 42];

        OperationStatus status = decompressor.Decompress(input, out int bytesConsumed);

        Assert.Equal(OperationStatus.InvalidData, status);
        Assert.Equal(input.Length, bytesConsumed);
    }

    [Fact]
    public void Decompress_SplitLongLiteralHeaderLongerThanExpected_ReturnsInvalidData()
    {
        using var decompressor = new SnappyDecompressor();

        OperationStatus status = decompressor.Decompress([1, 0xf0], out int bytesConsumed);

        Assert.Equal(OperationStatus.NeedMoreData, status);
        Assert.Equal(2, bytesConsumed);

        status = decompressor.Decompress([1, 42], out bytesConsumed);

        Assert.Equal(OperationStatus.InvalidData, status);
        Assert.Equal(2, bytesConsumed);
    }

    [Fact]
    public void Decompress_InvalidTag_ReportsBytesConsumed()
    {
        using var decompressor = new SnappyDecompressor();
        byte[] input = [1, 1, 0];

        OperationStatus status = decompressor.Decompress(input, out int bytesConsumed);

        Assert.Equal(OperationStatus.InvalidData, status);
        Assert.Equal(1, bytesConsumed);
    }

    [Fact]
    public void Decompress_SplitInvalidTag_ReportsBytesConsumed()
    {
        using var decompressor = new SnappyDecompressor();

        OperationStatus status = decompressor.Decompress([1, 1], out int bytesConsumed);

        Assert.Equal(OperationStatus.NeedMoreData, status);
        Assert.Equal(2, bytesConsumed);

        status = decompressor.Decompress([0], out bytesConsumed);

        Assert.Equal(OperationStatus.InvalidData, status);
        Assert.Equal(0, bytesConsumed);
    }

    #endregion

    #region DecompressAllTags

    [Fact]
    public void DecompressAllTags_ShortInputBufferWhichCopiesToScratch_DoesNotReadPastEndOfScratch()
    {
        // Arrange

        var decompressor = new SnappyDecompressor();
        decompressor.SetExpectedLengthForTest(1024);

        decompressor.WriteToBufferForTest(Enumerable.Range(0, 255).Select(p => (byte)p).ToArray());

        // if in error, decompressor will read the 222, 0, 0 as the next tag and throw a copy offset exception
        decompressor.LoadScratchForTest([222, 222, 222, 222, 0, 0], 0);

        // Act

        decompressor.DecompressAllTags([150, 255, 0]);

    }

    #endregion

    #region ExtractData

    [Fact]
    public void ExtractData_NoLength_InvalidOperationException()
    {
        // Arrange

        using var decompressor = new SnappyDecompressor();

        // Act/Assert

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => decompressor.ExtractData());

        Assert.Equal("No data present.", ex.Message);
    }

    [Fact]
    public void ExtractData_NotFullDecompressed_InvalidOperationException()
    {
        // Arrange

        using var decompressor = new SnappyDecompressor();

        using IMemoryOwner<byte> compressed = Snappy.CompressToMemory([1, 2, 3, 4]);

        // Only length is forwarded
        decompressor.Decompress(compressed.Memory.Span.Slice(0, 1), out _);

        // Act/Assert

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => decompressor.ExtractData());
        Assert.Equal("Block is not fully decompressed.", ex.Message);
    }

    [Fact]
    public void ExtractData_ZeroLength_EmptyMemory()
    {
        // Arrange

        using var decompressor = new SnappyDecompressor();

        using IMemoryOwner<byte> compressed = Snappy.CompressToMemory([]);

        decompressor.Decompress(compressed.Memory.Span, out _);

        // Act

        using IMemoryOwner<byte> result = decompressor.ExtractData();

        // Assert

        Assert.Equal(0, result.Memory.Length);
    }

    [Fact]
    public void ExtractData_SomeData_Memory()
    {
        // Arrange

        using var decompressor = new SnappyDecompressor();

        using IMemoryOwner<byte> compressed = Snappy.CompressToMemory([1, 2, 3, 4]);

        decompressor.Decompress(compressed.Memory.Span, out _);

        // Act

        using IMemoryOwner<byte> result = decompressor.ExtractData();

        // Assert

        Assert.Equal(4, result.Memory.Length);
    }

    [Fact]
    public void ExtractData_SomeData_DoesResetForReuse()
    {
        // Arrange

        using var decompressor = new SnappyDecompressor();

        using IMemoryOwner<byte> compressed = Snappy.CompressToMemory([1, 2, 3, 4]);
        using IMemoryOwner<byte> compressed2 = Snappy.CompressToMemory([4, 3, 2, 1]);

        decompressor.Decompress(compressed.Memory.Span, out _);

        // Act

        using IMemoryOwner<byte> result = decompressor.ExtractData();

        decompressor.Decompress(compressed2.Memory.Span, out _);

        using IMemoryOwner<byte> result2 = decompressor.ExtractData();

        // Assert

        Assert.Equal(4, result2.Memory.Length);
        Assert.Equal(new byte[] { 4, 3, 2, 1 }, result2.Memory.ToArray());
    }

    #endregion
}
