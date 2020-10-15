using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Snappier.Internal;

namespace Snappier
{
    /// <summary>
    /// Stream which supports compressing or decompressing data using the Snappy compression algorithm.
    /// To decompress data, supply a stream to be read. To compress data, provide a stream to be written to.
    /// </summary>
    public sealed class SnappyStream : Stream
    {
        private const int DefaultBufferSize = 8192;

        private Stream _stream;
        private readonly CompressionMode _mode;
        private readonly bool _leaveOpen;

        private SnappyStreamDecompressor? _decompressor;
        private SnappyStreamCompressor? _compressor;

        private byte[]? _buffer = null;
        private bool _wroteBytes;

        /// <summary>
        /// Create a stream which supports compressing or decompressing data using the Snappy compression algorithm.
        /// To decompress data, supply a stream to be read. To compress data, provide a stream to be written to.
        /// </summary>
        /// <param name="stream">Source or destination stream.</param>
        /// <param name="mode">Compression or decompression mode.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
        /// <exception cref="ArgumentException">Stream read/write capability doesn't match with <paramref name="mode"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Invalid <paramref name="mode"/>.</exception>
        /// <remarks>
        /// The stream will be closed when the SnappyStream is closed.
        /// </remarks>
        public SnappyStream(Stream stream, CompressionMode mode)
            : this(stream, mode, false)
        {
        }

        /// <summary>
        /// Create a stream which supports compressing or decompressing data using the Snappy compression algorithm.
        /// To decompress data, supply a stream to be read. To compress data, provide a stream to be written to.
        /// </summary>
        /// <param name="stream">Source or destination stream.</param>
        /// <param name="mode">Compression or decompression mode.</param>
        /// <param name="leaveOpen">If true, close the stream when the SnappyStream is closed.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
        /// <exception cref="ArgumentException">Stream read/write capability doesn't match with <paramref name="mode"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Invalid <paramref name="mode"/>.</exception>
        public SnappyStream(Stream stream, CompressionMode mode, bool leaveOpen)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _mode = mode;
            _leaveOpen = leaveOpen;

            switch (mode)
            {
                case CompressionMode.Decompress:
                    if (!stream.CanRead)
                    {
                        throw new ArgumentException("Unreadable stream", nameof(stream));
                    }

                    _decompressor = new SnappyStreamDecompressor();

                    break;

                case CompressionMode.Compress:
                    if (!stream.CanWrite)
                    {
                        throw new NotSupportedException("Unwritable stream");
                    }

                    _compressor = new SnappyStreamCompressor();
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        /// <summary>
        /// The base stream being read from or written to.
        /// </summary>
        public Stream BaseStream => _stream;

        #region overrides

        /// <inheritdoc />
        public override bool CanRead => _mode == CompressionMode.Decompress && (_stream?.CanRead ?? false);

        /// <inheritdoc />
        public override bool CanWrite => _mode == CompressionMode.Compress && (_stream?.CanWrite ?? false);

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Flush()
        {
            EnsureNotDisposed();

            if (_mode == CompressionMode.Compress && _wroteBytes)
            {
                Debug.Assert(_compressor != null);
                _compressor.Flush(_stream);
            }
        }

        /// <inheritdoc />
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            EnsureNoActiveAsyncOperation();
            EnsureNotDisposed();

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            if (_mode == CompressionMode.Compress && _wroteBytes)
            {
                Debug.Assert(_compressor != null);
                return _compressor.FlushAsync(_stream, cancellationToken).AsTask();
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => ReadCore(buffer.AsSpan(offset, count));

        #if !NETSTANDARD2_0
        /// <inheritdoc />
        public override int Read(Span<byte> buffer) => ReadCore(buffer);
        #endif

        private int ReadCore(Span<byte> buffer)
        {
            EnsureDecompressionMode();
            EnsureNotDisposed();
            EnsureBufferInitialized();

            int totalRead = 0;

            Debug.Assert(_decompressor != null);
            while (true)
            {
                int bytesRead = _decompressor.Decompress(buffer.Slice(totalRead));
                totalRead += bytesRead;

                if (totalRead == buffer.Length)
                {
                    break;
                }

                Debug.Assert(_buffer != null);
                #if !NETSTANDARD2_0
                int bytes = _stream.Read(_buffer);
                #else
                int bytes = _stream.Read(_buffer, 0, _buffer.Length);
                #endif
                if (bytes <= 0)
                {
                    break;
                }
                else if (bytes > _buffer.Length)
                {
                    throw new InvalidDataException();
                }

                _decompressor.SetInput(_buffer.AsMemory(0, bytes));
            }

            return totalRead;
        }

        /// <inheritdoc />
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) =>
            ReadAsyncCore(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        #if !NETSTANDARD2_0
        /// <inheritdoc />
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new CancellationToken()) =>
            ReadAsyncCore(buffer, cancellationToken);
        #endif

        private ValueTask<int> ReadAsyncCore(Memory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            EnsureDecompressionMode();
            EnsureNoActiveAsyncOperation();
            EnsureNotDisposed();

            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<int>(Task.FromCanceled<int>(cancellationToken));
            }

            EnsureBufferInitialized();

            bool cleanup = true;
            AsyncOperationStarting();
            try
            {
                Debug.Assert(_decompressor != null);

                // Finish decompressing any bytes in the input buffer
                int bytesRead = 0, bytesReadIteration = -1;
                while (bytesRead < buffer.Length && bytesReadIteration != 0)
                {
                    bytesReadIteration = _decompressor.Decompress(buffer.Span.Slice(bytesRead));
                    bytesRead += bytesReadIteration;
                }

                if (bytesRead != 0)
                {
                    // If decompression output buffer is not empty, return immediately.
                    return new ValueTask<int>(bytesRead);
                }

                #if !NETSTANDARD2_0
                ValueTask<int> readTask = _stream.ReadAsync(_buffer, cancellationToken);
                #else
                ValueTask<int> readTask = new ValueTask<int>(_stream.ReadAsync(_buffer, 0, _buffer.Length, cancellationToken));
                #endif
                cleanup = false;
                return FinishReadAsyncMemory(readTask, buffer, cancellationToken);
            }
            finally
            {
                // if we haven't started any async work, decrement the counter to end the transaction
                if (cleanup)
                {
                    AsyncOperationCompleting();
                }
            }
        }

        private async ValueTask<int> FinishReadAsyncMemory(
            ValueTask<int> readTask, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            try
            {
                Debug.Assert(_decompressor != null && _buffer != null);
                while (true)
                {
                    int bytesRead = await readTask.ConfigureAwait(false);
                    EnsureNotDisposed();

                    if (bytesRead <= 0)
                    {
                        // This indicates the base stream has received EOF
                        return 0;
                    }
                    else if (bytesRead > _buffer.Length)
                    {
                        // The stream is either malicious or poorly implemented and returned a number of
                        // bytes larger than the buffer supplied to it.
                        throw new InvalidDataException();
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    // Feed the data from base stream into decompression engine
                    _decompressor.SetInput(_buffer.AsMemory(0, bytesRead));

                    // Finish inflating any bytes in the input buffer
                    int inflatedBytes = 0, bytesReadIteration = -1;
                    while (inflatedBytes < buffer.Length && bytesReadIteration != 0)
                    {
                        bytesReadIteration = _decompressor.Decompress(buffer.Span.Slice(inflatedBytes));
                        inflatedBytes += bytesReadIteration;
                    }

                    if (inflatedBytes != 0)
                    {
                        // If decompression output buffer is not empty, return immediately.
                        return inflatedBytes;
                    }
                    else
                    {
                        // We could have read in head information and didn't get any data.
                        // Read from the base stream again.
                        #if !NETSTANDARD2_0
                        readTask = _stream.ReadAsync(_buffer, cancellationToken);
                        #else
                        readTask = new ValueTask<int>(_stream.ReadAsync(_buffer, 0, _buffer.Length, cancellationToken));
                        #endif
                    }
                }
            }
            finally
            {
                AsyncOperationCompleting();
            }
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) =>
            WriteCore(buffer.AsSpan(offset, count));

        #if !NETSTANDARD2_0
        /// <inheritdoc />
        public override void Write(ReadOnlySpan<byte> buffer) => WriteCore(buffer);
        #endif

        private void WriteCore(ReadOnlySpan<byte> buffer)
        {
            EnsureCompressionMode();
            EnsureNotDisposed();

            Debug.Assert(_compressor != null);
            _compressor.Write(buffer, _stream);

            _wroteBytes = true;
        }

        /// <inheritdoc />
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsyncCore(buffer.AsMemory(offset, count), cancellationToken).AsTask();

#if !NETSTANDARD2_0
        /// <inheritdoc />
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            WriteAsyncCore(buffer, cancellationToken);
#endif

        private ValueTask WriteAsyncCore(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            EnsureCompressionMode();
            EnsureNoActiveAsyncOperation();
            EnsureNotDisposed();

            return cancellationToken.IsCancellationRequested
                ? new ValueTask(Task.FromCanceled(cancellationToken))
                : WriteAsyncMemoryCore(buffer, cancellationToken);
        }

        private async ValueTask WriteAsyncMemoryCore(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            AsyncOperationStarting();
            try
            {
                Debug.Assert(_compressor != null);

                await _compressor.WriteAsync(buffer, _stream, cancellationToken).ConfigureAwait(false);

                _wroteBytes = true;
            }
            finally
            {
                AsyncOperationCompleting();
            }
        }

        // This is called by Dispose:
        private void PurgeBuffers()
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (_stream == null || _mode != CompressionMode.Compress)
            {
                return;
            }

            Debug.Assert(_compressor != null);
            // Make sure to only "flush" when we actually had some input
            if (_wroteBytes)
            {
                Flush();
            }
        }

        private ValueTask PurgeBuffersAsync()
        {
            // Same logic as PurgeBuffers, except with async counterparts.

            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (_stream == null || _mode != CompressionMode.Compress)
            {
                return default;
            }

            Debug.Assert(_compressor != null);
            // Make sure to only "flush" when we actually had some input
            if (_wroteBytes)
            {
                return new ValueTask(FlushAsync());
            }

            return default;
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            try
            {
                PurgeBuffers();
            }
            finally
            {
                // Stream.Close() may throw here (may or may not be due to the same error).
                // In this case, we still need to clean up internal resources, hence the inner finally blocks.
                try
                {
                    if (disposing && !_leaveOpen)
                        _stream?.Dispose();
                }
                finally
                {
                    _stream = null!;

                    try
                    {
                        _decompressor?.Dispose();
                        _compressor?.Dispose();
                    }
                    finally
                    {
                        _decompressor = null;
                        _compressor = null;

                        byte[]? buffer = _buffer;
                        if (buffer != null)
                        {
                            _buffer = null;
                            if (!AsyncOperationIsActive)
                            {
                                ArrayPool<byte>.Shared.Return(buffer);
                            }
                        }

                        base.Dispose(disposing);
                    }
                }
            }
        }

        #if !NETSTANDARD2_0
        /// <inheritdoc />
        public override async ValueTask DisposeAsync()
        {
            // Same logic as Dispose(true), except with async counterparts.

            try
            {
                await PurgeBuffersAsync().ConfigureAwait(false);
            }
            finally
            {

                // Stream.Close() may throw here (may or may not be due to the same error).
                // In this case, we still need to clean up internal resources, hence the inner finally blocks.
                Stream stream = _stream;
                _stream = null!;
                try
                {
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                    if (!_leaveOpen && stream != null)
                    {
                        await stream.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        _decompressor?.Dispose();
                    }
                    finally
                    {
                        _decompressor = null;

                        byte[]? buffer = _buffer;
                        if (buffer != null)
                        {
                            _buffer = null;
                            if (!AsyncOperationIsActive)
                            {
                                ArrayPool<byte>.Shared.Return(buffer);
                            }
                        }
                    }
                }
            }
        }
        #endif

        #endregion

        private void EnsureNotDisposed()
        {
            if (_stream == null)
            {
                throw new ObjectDisposedException(nameof(SnappyStream));
            }
        }

        private void EnsureDecompressionMode()
        {
            if (_mode != CompressionMode.Decompress)
            {
                throw new NotSupportedException();
            }
        }

        private void EnsureCompressionMode()
        {
            if (_mode != CompressionMode.Compress)
            {
                throw new NotSupportedException();
            }
        }

        private void EnsureBufferInitialized()
        {
            if (_buffer == null)
            {
                _buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
            }
        }

        #region async controls

        private int _activeAsyncOperation;
        private bool AsyncOperationIsActive => _activeAsyncOperation != 0;

        private void EnsureNoActiveAsyncOperation()
        {
            if (AsyncOperationIsActive)
            {
                ThrowInvalidBeginCall();
            }
        }

        private void AsyncOperationStarting()
        {
            if (Interlocked.CompareExchange(ref _activeAsyncOperation, 1, 0) != 0)
            {
                ThrowInvalidBeginCall();
            }
        }

        private void AsyncOperationCompleting()
        {
            int oldValue = Interlocked.CompareExchange(ref _activeAsyncOperation, 0, 1);
            Debug.Assert(oldValue == 1, $"Expected {nameof(_activeAsyncOperation)} to be 1, got {oldValue}");
        }

        private static void ThrowInvalidBeginCall()
        {
            throw new InvalidOperationException("Invalid begin call");
        }

        #endregion
    }
}
