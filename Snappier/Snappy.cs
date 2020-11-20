using System;
using System.Buffers;
using System.IO;
using Snappier.Internal;

namespace Snappier
{
    /// <summary>
    /// Routines for performing Snappy compression and decompression on raw data blocks using <see cref="Span{T}"/>.
    /// These routines do not read or write any Snappy framing.
    /// </summary>
    public static class Snappy
    {
        /// <summary>
        /// For a given amount of input data, calculate the maximum potential size of the compressed output.
        /// </summary>
        /// <param name="inputLength">Length of the input data, in bytes.</param>
        /// <returns>The maximum potential size of the compressed output.</returns>
        /// <remarks>
        /// This is useful for allocating a sufficient output buffer before calling <see cref="Compress"/>.
        /// </remarks>
        public static int GetMaxCompressedLength(int inputLength) =>
            Helpers.MaxCompressedLength(inputLength);

        /// <summary>
        /// Compress a block of Snappy data.
        /// </summary>
        /// <param name="input">Data to compress.</param>
        /// <param name="output">Buffer to receive the compressed data.</param>
        /// <returns>Number of bytes written to <paramref name="output"/>.</returns>
        /// <remarks>
        /// The output buffer must be large enough to contain the compressed output.
        /// </remarks>
        public static int Compress(ReadOnlySpan<byte> input, Span<byte> output)
        {
            using var compressor = new SnappyCompressor();

            return compressor.Compress(input, output);
        }

        /// <summary>
        /// Compress a block of Snappy data.
        /// </summary>
        /// <param name="input">Data to compress.</param>
        /// <returns>An <see cref="IMemoryOwner{T}"/> with the decompressed data. The caller is responsible for disposing this object.</returns>
        /// <remarks>
        /// Failing to dispose of the returned <see cref="IMemoryOwner{T}"/> may result in memory leaks.
        /// </remarks>
        public static IMemoryOwner<byte> CompressToMemory(ReadOnlySpan<byte> input)
        {
            var buffer = MemoryPool<byte>.Shared.Rent(GetMaxCompressedLength(input.Length));

            try
            {
                var length = Compress(input, buffer.Memory.Span);

                return new SlicedMemoryOwner(buffer, length);
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Compress a block of Snappy data.
        /// </summary>
        /// <param name="input">Data to compress.</param>
        /// <remarks>
        /// The resulting byte array is allocated on the heap. If possible, <see cref="CompressToMemory"/> should
        /// be used instead since it uses a shared buffer pool.
        /// </remarks>
        public static byte[] CompressToArray(ReadOnlySpan<byte> input)
        {
            using var buffer = CompressToMemory(input);
            var bufferSpan = buffer.Memory.Span;

            var result = new byte[bufferSpan.Length];
            bufferSpan.CopyTo(result);
            return result;
        }

        /// <summary>
        /// Get the uncompressed data length from a compressed Snappy block.
        /// </summary>
        /// <param name="input">Compressed snappy block.</param>
        /// <returns>The length of the uncompressed data in the block.</returns>
        /// <exception cref="InvalidDataException">The data in <paramref name="input"/> has an invalid length.</exception>
        /// <remarks>
        /// This is useful for allocating a sufficient output buffer before calling <see cref="Decompress"/>.
        /// </remarks>
        public static int GetUncompressedLength(ReadOnlySpan<byte> input) =>
            SnappyDecompressor.ReadUncompressedLength(input);

        /// <summary>
        /// Decompress a block of Snappy data. This must be an entire block.
        /// </summary>
        /// <param name="input">Data to decompress.</param>
        /// <param name="output">Buffer to receive the decompressed data.</param>
        /// <returns>Number of bytes written to <paramref name="output"/>.</returns>
        /// <exception cref="InvalidDataException">Invalid Snappy block.</exception>
        /// <exception cref="ArgumentException">Output buffer is too small.</exception>
        public static int Decompress(ReadOnlySpan<byte> input, Span<byte> output)
        {
            using var decompressor = new SnappyDecompressor();

            decompressor.Decompress(input);

            if (!decompressor.AllDataDecompressed)
            {
                throw new InvalidDataException("Incomplete Snappy block.");
            }

            var read = decompressor.Read(output);

            if (!decompressor.EndOfFile)
            {
                throw new ArgumentException("Output buffer is too small.", nameof(output));
            }

            return read;
        }

        /// <summary>
        /// Decompress a block of Snappy to a new memory buffer. This must be an entire block.
        /// </summary>
        /// <param name="input">Data to decompress.</param>
        /// <returns>An <see cref="IMemoryOwner{T}"/> with the decompressed data. The caller is responsible for disposing this object.</returns>
        /// <remarks>
        /// Failing to dispose of the returned <see cref="IMemoryOwner{T}"/> may result in memory leaks.
        /// </remarks>
        public static IMemoryOwner<byte> DecompressToMemory(ReadOnlySpan<byte> input)
        {
            using var decompressor = new SnappyDecompressor();

            decompressor.Decompress(input);

            if (!decompressor.AllDataDecompressed)
            {
                throw new InvalidDataException("Incomplete Snappy block.");
            }

            return decompressor.ExtractData();
        }

        /// <summary>
        /// Decompress a block of Snappy to a new byte array. This must be an entire block.
        /// </summary>
        /// <param name="input">Data to decompress.</param>
        /// <returns>The decompressed data.</returns>
        /// <remarks>
        /// The resulting byte array is allocated on the heap. If possible, <see cref="DecompressToMemory"/> should
        /// be used instead since it uses a shared buffer pool.
        /// </remarks>
        public static byte[] DecompressToArray(ReadOnlySpan<byte> input)
        {
            var length = GetUncompressedLength(input);

            var result = new byte[length];

            Decompress(input, result);

            return result;
        }
    }
}
