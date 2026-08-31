using System.Buffers;

namespace Snappier.Tests;

public class SnappyBlockCodecTests
{
    [Fact]
    public void Encoder_ReusesWorkingMemoryAcrossSpanAndSequenceCalls()
    {
        // Arrange

        byte[] firstInput = Enumerable.Repeat((byte)1, 16_384).ToArray();
        byte[] secondInput = Enumerable.Repeat((byte)2, 32_768).ToArray();
        byte[] firstCompressed = new byte[Snappy.GetMaxCompressedLength(firstInput.Length)];
        var secondCompressed = new TestBufferWriter();
        using var encoder = new SnappyBlockEncoder();

        // Act

        int firstCompressedLength = encoder.Compress(firstInput, firstCompressed);
        encoder.Compress(SequenceHelpers.CreateSequence(secondInput, 257), secondCompressed);

        // Assert

        Assert.Equal(firstInput, Snappy.DecompressToArray(firstCompressed.AsSpan(0, firstCompressedLength)));
        Assert.Equal(secondInput, Snappy.DecompressToArray(secondCompressed.WrittenSpan));
    }

    [Fact]
    public void Encoder_DestinationTooSmall_ReturnsFalseOrThrows()
    {
        using var encoder = new SnappyBlockEncoder();

        Assert.False(encoder.TryCompress([1, 2, 3, 4], [], out int bytesWritten));
        Assert.Equal(0, bytesWritten);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => encoder.Compress([1, 2, 3, 4], []));
        Assert.Equal("destination", exception.ParamName);
    }

    [Fact]
    public void Decoder_IncrementalInputAndOutput_ReturnsOperationStatus()
    {
        // Arrange

        byte[] input = new byte[65_536];
        byte[] compressed = Snappy.CompressToArray(input);
        byte[] output = new byte[input.Length];
        using var decoder = new SnappyBlockDecoder();

        // Act/Assert: split the multi-byte uncompressed length prefix

        OperationStatus status = decoder.Decompress(compressed.AsSpan(0, 1), output,
            out int bytesConsumed, out int bytesWritten);

        Assert.Equal(OperationStatus.NeedMoreData, status);
        Assert.Equal(1, bytesConsumed);
        Assert.Equal(0, bytesWritten);

        // Act/Assert: consume the remaining source while applying output backpressure

        status = decoder.Decompress(compressed.AsSpan(1), output.AsSpan(0, 17),
            out bytesConsumed, out bytesWritten);

        Assert.Equal(OperationStatus.DestinationTooSmall, status);
        Assert.Equal(compressed.Length - 1, bytesConsumed);
        Assert.Equal(17, bytesWritten);

        int outputOffset = bytesWritten;
        while (status == OperationStatus.DestinationTooSmall)
        {
            status = decoder.Decompress([], output.AsSpan(outputOffset, Math.Min(127, output.Length - outputOffset)),
                out bytesConsumed, out bytesWritten);

            Assert.Equal(0, bytesConsumed);
            outputOffset += bytesWritten;
        }

        Assert.Equal(OperationStatus.Done, status);
        Assert.Equal(input.Length, outputOffset);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Decoder_Reset_AllowsAnotherBlockAfterDoneOrInvalidData()
    {
        // Arrange

        byte[] compressed = Snappy.CompressToArray([1, 2, 3, 4]);
        Span<byte> output = stackalloc byte[4];
        using var decoder = new SnappyBlockDecoder();

        // Act/Assert: completed block requires reset before another block

        Assert.Equal(OperationStatus.Done,
            decoder.Decompress(compressed, output, out int bytesConsumed, out int bytesWritten));
        Assert.Equal(compressed.Length, bytesConsumed);
        Assert.Equal(output.Length, bytesWritten);
        Assert.Equal(OperationStatus.Done, decoder.Decompress(compressed, output, out bytesConsumed, out bytesWritten));
        Assert.Equal(0, bytesConsumed);
        Assert.Equal(0, bytesWritten);

        decoder.Reset();

        // Act/Assert: invalid data remains invalid until reset

        Assert.Equal(OperationStatus.InvalidData, decoder.Decompress([1, 1, 0], output, out _, out _));
        Assert.Equal(OperationStatus.InvalidData, decoder.Decompress(compressed, output, out _, out _));

        decoder.Reset();

        Assert.Equal(OperationStatus.Done, decoder.Decompress(compressed, output, out bytesConsumed, out bytesWritten));
        Assert.Equal(compressed.Length, bytesConsumed);
        Assert.Equal(output.Length, bytesWritten);
    }

    [Fact]
    public void Decoder_SequenceToWriter_ResetsBetweenBlocks()
    {
        // Arrange

        byte[] firstInput = Enumerable.Repeat((byte)1, 16_384).ToArray();
        byte[] secondInput = Enumerable.Repeat((byte)2, 32_768).ToArray();
        byte[] firstCompressed = Snappy.CompressToArray(firstInput);
        byte[] secondCompressed = Snappy.CompressToArray(secondInput);
        var firstOutput = new TestBufferWriter();
        var secondOutput = new TestBufferWriter();
        using var decoder = new SnappyBlockDecoder();

        // Act

        decoder.Decompress(new ReadOnlySequence<byte>(firstCompressed), firstOutput);
        decoder.Decompress(SequenceHelpers.CreateSequence(secondCompressed, 127), secondOutput);

        // Assert

        Assert.Equal(firstInput, firstOutput.WrittenSpan.ToArray());
        Assert.Equal(secondInput, secondOutput.WrittenSpan.ToArray());
    }

    [Fact]
    public void Decoder_SequenceToWriter_InvalidOrIncompleteData_Throws()
    {
        using var decoder = new SnappyBlockDecoder();
        byte[] firstBlock = Snappy.CompressToArray([1, 2, 3, 4]);
        byte[] secondBlock = Snappy.CompressToArray([5, 6, 7, 8]);

        Assert.Throws<InvalidDataException>(() =>
            decoder.Decompress(new ReadOnlySequence<byte>([1, 1, 0]), new TestBufferWriter()));
        Assert.Throws<InvalidDataException>(() =>
            decoder.Decompress(new ReadOnlySequence<byte>([128]), new TestBufferWriter()));
        Assert.Throws<InvalidDataException>(() =>
            decoder.Decompress(new ReadOnlySequence<byte>([.. firstBlock, .. secondBlock]), new TestBufferWriter()));
    }

    [Fact]
    public void Codecs_AfterDispose_ThrowObjectDisposedException()
    {
        var encoder = new SnappyBlockEncoder();
        var decoder = new SnappyBlockDecoder();
        encoder.Dispose();
        decoder.Dispose();

        Assert.Throws<ObjectDisposedException>(() => encoder.TryCompress([], [], out _));
        Assert.Throws<ObjectDisposedException>(() => decoder.Reset());
    }

    private sealed class TestBufferWriter : IBufferWriter<byte>
    {
        private byte[] _buffer = new byte[256];

        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, WrittenCount);

        public int WrittenCount { get; private set; }

        public void Advance(int count) => WrittenCount += count;

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(WrittenCount);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(WrittenCount);
        }

        private void EnsureCapacity(int sizeHint)
        {
            int requiredCapacity = WrittenCount + Math.Max(sizeHint, 1);
            if (requiredCapacity > _buffer.Length)
            {
                Array.Resize(ref _buffer, Math.Max(requiredCapacity, _buffer.Length * 2));
            }
        }
    }
}
