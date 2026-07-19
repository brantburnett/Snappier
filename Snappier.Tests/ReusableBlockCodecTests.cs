using System.Buffers;
using System.Text;

namespace Snappier.Tests;

public class ReusableBlockCodecTests
{
    [Fact]
    public void Compressor_ReusesWorkingMemoryForSingleAndMultiSegmentInput()
    {
        byte[] firstInput = Encoding.UTF8.GetBytes(new string('a', 16_384));
        byte[] secondInput = Encoding.UTF8.GetBytes(new string('b', 32_768));
        using var compressor = new SnappyCompressor();

        var firstCompressed = new TestBufferWriter();
        compressor.Compress(new ReadOnlySequence<byte>(firstInput), firstCompressed);

        var secondCompressed = new TestBufferWriter();
        compressor.Compress(
            SequenceHelpers.CreateSequence(secondInput, maxSegmentSize: 257),
            secondCompressed);

        Assert.Equal(firstInput, Snappy.DecompressToArray(firstCompressed.WrittenSpan));
        Assert.Equal(secondInput, Snappy.DecompressToArray(secondCompressed.WrittenSpan));
    }

    [Fact]
    public void Decompressor_ResetReplacesBufferWriter()
    {
        byte[] firstInput = Encoding.UTF8.GetBytes(new string('a', 16_384));
        byte[] secondInput = Encoding.UTF8.GetBytes(new string('b', 32_768));
        byte[] firstCompressed = Snappy.CompressToArray(firstInput);
        byte[] secondCompressed = Snappy.CompressToArray(secondInput);
        var firstOutput = new TestBufferWriter();
        var secondOutput = new TestBufferWriter();
        using var decompressor = new SnappyDecompressor();

        decompressor.Decompress(new ReadOnlySequence<byte>(firstCompressed), firstOutput);
        decompressor.Decompress(
            SequenceHelpers.CreateSequence(secondCompressed, maxSegmentSize: 127),
            secondOutput);

        Assert.Equal(firstInput, firstOutput.WrittenSpan.ToArray());
        Assert.Equal(secondInput, secondOutput.WrittenSpan.ToArray());
    }

    [Fact]
    public void Decompressor_ReplaceBufferWriterDuringBlock_ThrowsInvalidOperationException()
    {
        byte[] compressed = Snappy.CompressToArray(new byte[256]);
        using var decompressor = new SnappyDecompressor
        {
            BufferWriter = new TestBufferWriter()
        };

        decompressor.Decompress(compressed.AsSpan(0, 2));

        var exception = Assert.Throws<InvalidOperationException>(
            () => decompressor.BufferWriter = new TestBufferWriter());
        Assert.Equal(
            "The buffer writer cannot be replaced while a block is active. Call Reset first.",
            exception.Message);
    }

    private sealed class TestBufferWriter : IBufferWriter<byte>
    {
        private byte[] _buffer = new byte[256];

        public int WrittenCount { get; private set; }

        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, WrittenCount);

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
            if (requiredCapacity <= _buffer.Length)
                return;

            Array.Resize(ref _buffer, Math.Max(requiredCapacity, _buffer.Length * 2));
        }
    }
}
