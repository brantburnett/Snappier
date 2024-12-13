﻿using System;
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
        /// This is useful for allocating a sufficient output buffer before calling <see cref="Compress(ReadOnlySpan{byte}, Span{byte})"/>.
        /// </remarks>
        public static int GetMaxCompressedLength(int inputLength) =>
            // When used to allocate a precise buffer for compression, we need to also pad for the length encoding.
            // Failure to do so will cause the compression process to think the buffer may not be large enough after the
            // length is encoded and use a temporary buffer for compression which must then be copied.
            Helpers.MaxCompressedLength(inputLength) + VarIntEncoding.MaxLength;

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
        /// <param name="output">Buffer writer to receive the compressed data.</param>
        /// <remarks>
        /// For the best performance the input sequence should be comprised of segments some multiple of 64KB
        /// in size or a single <see cref="ReadOnlySpan{T}"/> wrapped in a sequence.
        /// </remarks>
        public static void Compress(ReadOnlySequence<byte> input, IBufferWriter<byte> output)
        {
            ThrowHelper.ThrowIfNull(output);

            using var compressor = new SnappyCompressor();

            compressor.Compress(input, output);
        }

        /// <summary>
        /// Compress a block of Snappy data.
        /// </summary>
        /// <param name="input">Data to compress.</param>
        /// <returns>An <see cref="IMemoryOwner{T}"/> with the compressed data. The caller is responsible for disposing this object.</returns>
        /// <remarks>
        /// Failing to dispose of the returned <see cref="IMemoryOwner{T}"/> may result in performance loss.
        /// </remarks>
        public static IMemoryOwner<byte> CompressToMemory(ReadOnlySpan<byte> input)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(GetMaxCompressedLength(input.Length));

            try
            {
                int length = Compress(input, buffer);

                return new ByteArrayPoolMemoryOwner(buffer, length);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
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
            using IMemoryOwner<byte> buffer = CompressToMemory(input);
            Span<byte> bufferSpan = buffer.Memory.Span;

            byte[] result = new byte[bufferSpan.Length];
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
        /// This is useful for allocating a sufficient output buffer before calling <see cref="Decompress(ReadOnlySpan{byte}, Span{byte})"/>.
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
                ThrowHelper.ThrowInvalidDataException("Incomplete Snappy block.");
            }

            int read = decompressor.Read(output);

            if (!decompressor.EndOfFile)
            {
                ThrowHelper.ThrowArgumentException("Output buffer is too small.", nameof(output));
            }

            return read;
        }

        /// <summary>
        /// Decompress a block of Snappy data. This must be an entire block.
        /// </summary>
        /// <param name="input">Data to decompress.</param>
        /// <param name="output">Buffer writer to receive the decompressed data.</param>
        /// <exception cref="InvalidDataException">Invalid Snappy block.</exception>
        public static void Decompress(ReadOnlySequence<byte> input, IBufferWriter<byte> output)
        {
            ThrowHelper.ThrowIfNull(output);

            using var decompressor = new SnappyDecompressor()
            {
                BufferWriter = output
            };

            foreach (ReadOnlyMemory<byte> segment in input)
            {
                decompressor.Decompress(segment.Span);
            }

            if (!decompressor.AllDataDecompressed)
            {
                ThrowHelper.ThrowInvalidDataException("Incomplete Snappy block.");
            }
        }

        /// <summary>
        /// Decompress a block of Snappy to a new memory buffer. This must be an entire block.
        /// </summary>
        /// <param name="input">Data to decompress.</param>
        /// <returns>An <see cref="IMemoryOwner{T}"/> with the decompressed data. The caller is responsible for disposing this object.</returns>
        /// <exception cref="InvalidDataException">Incomplete Snappy block.</exception>
        /// <remarks>
        /// Failing to dispose of the returned <see cref="IMemoryOwner{T}"/> may result in performance loss.
        /// </remarks>
        public static IMemoryOwner<byte> DecompressToMemory(ReadOnlySpan<byte> input)
        {
            using var decompressor = new SnappyDecompressor();

            decompressor.Decompress(input);

            if (!decompressor.AllDataDecompressed)
            {
                ThrowHelper.ThrowInvalidDataException("Incomplete Snappy block.");
            }

            return decompressor.ExtractData();
        }

        /// <summary>
        /// Decompress a block of Snappy to a new memory buffer. This must be an entire block.
        /// </summary>
        /// <param name="input">Data to decompress.</param>
        /// <returns>An <see cref="IMemoryOwner{T}"/> with the decompressed data. The caller is responsible for disposing this object.</returns>
        /// <exception cref="InvalidDataException">Incomplete Snappy block.</exception>
        /// <remarks>
        /// Failing to dispose of the returned <see cref="IMemoryOwner{T}"/> may result in performance loss.
        /// </remarks>
        public static IMemoryOwner<byte> DecompressToMemory(ReadOnlySequence<byte> input)
        {
            using var decompressor = new SnappyDecompressor();

            foreach (ReadOnlyMemory<byte> segment in input)
            {
                decompressor.Decompress(segment.Span);
            }

            if (!decompressor.AllDataDecompressed)
            {
                ThrowHelper.ThrowInvalidDataException("Incomplete Snappy block.");
            }

            return decompressor.ExtractData();
        }

        /// <summary>
        /// Decompress a block of Snappy to a new byte array. This must be an entire block.
        /// </summary>
        /// <param name="input">Data to decompress.</param>
        /// <returns>The decompressed data.</returns>
        /// <exception cref="InvalidDataException">Invalid Snappy block.</exception>
        /// <remarks>
        /// The resulting byte array is allocated on the heap. If possible, <see cref="DecompressToMemory(ReadOnlySpan{byte})"/> should
        /// be used instead since it uses a shared buffer pool.
        /// </remarks>
        public static byte[] DecompressToArray(ReadOnlySpan<byte> input)
        {
            int length = GetUncompressedLength(input);

            byte[] result = new byte[length];

            Decompress(input, result);

            return result;
        }
    }
}
