using System;
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
        /// The output buffer must be large enough to
        /// </remarks>
        public static int Compress(ReadOnlySpan<byte> input, Span<byte> output)
        {
            var compressor = new SnappyCompressor();

            return compressor.Compress(input, output);
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
        public static int Decompress(ReadOnlySpan<byte> input, Span<byte> output)
        {
            var decompressor = new SnappyDecompressor();

            decompressor.Decompress(input);

            if (!decompressor.AllDataDecompressed)
            {
                throw new InvalidDataException("Incomplete Snappy block");
            }

            return decompressor.Read(output);
        }
    }
}
