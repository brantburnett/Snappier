using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Snappier.Internal;

namespace Snappier;

/// <summary>
/// Stream which supports compressing or decompressing data using the Snappy compression algorithm.
/// To decompress data, supply a stream to be read. To compress data, provide a stream to be written to.
/// </summary>
public sealed class SnappyStream : Stream
{
    private const int DefaultBufferSize = 8192;

    private Stream? _stream;
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
    /// <param name="leaveOpen">If true, leave the inner stream open when the SnappyStream is closed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">Stream read/write capability doesn't match with <paramref name="mode"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Invalid <paramref name="mode"/>.</exception>
    public SnappyStream(Stream stream, CompressionMode mode, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _mode = mode;
        _leaveOpen = leaveOpen;

        switch (mode)
        {
            case CompressionMode.Decompress:
                if (!stream.CanRead)
                {
                    ThrowHelper.ThrowArgumentException("Unreadable stream", nameof(stream));
                }

                _decompressor = new SnappyStreamDecompressor();

                break;

            case CompressionMode.Compress:
                if (!stream.CanWrite)
                {
                    ThrowHelper.ThrowArgumentException("Unwritable stream", nameof(stream));
                }

                _compressor = new SnappyStreamCompressor();
                break;

            default:
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(mode), "Invalid mode");
                break;
        }
    }

    /// <summary>
    /// The base stream being read from or written to.
    /// </summary>
    public Stream BaseStream
    {
        get
        {
            EnsureNotDisposed();
            return _stream;
        }
    }

    #region overrides

    /// <inheritdoc />
    public override bool CanRead => _mode == CompressionMode.Decompress && (_stream?.CanRead ?? false);

    /// <inheritdoc />
    public override bool CanWrite => _mode == CompressionMode.Compress && (_stream?.CanWrite ?? false);

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override long Length
    {
        get
        {
            ThrowHelper.ThrowNotSupportedException();
            return 0;
        }
    }

    /// <inheritdoc />
    public override long Position
    {
        get
        {
            ThrowHelper.ThrowNotSupportedException();
            return 0;
        }
        // ReSharper disable once ValueParameterNotUsed
        set => ThrowHelper.ThrowNotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush()
    {
        EnsureNotDisposed();

        if (_mode == CompressionMode.Compress && _wroteBytes)
        {
            DebugExtensions.Assert(_compressor != null);
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
            DebugExtensions.Assert(_compressor != null);
            return _compressor.FlushAsync(_stream, cancellationToken);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        ThrowHelper.ThrowNotSupportedException();
        return 0;
    }

    /// <inheritdoc />
    public override void SetLength(long value) => ThrowHelper.ThrowNotSupportedException();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => ReadCore(buffer.AsSpan(offset, count));

#if NET8_0_OR_GREATER

    /// <inheritdoc />
    public override int Read(Span<byte> buffer) => ReadCore(buffer);

    /// <inheritdoc />
    public override int ReadByte()
    {
        byte b = 0;
        int r = ReadCore(MemoryMarshal.CreateSpan(ref b, 1));
        return r == 0 ? -1 : b;
    }

#endif

    private int ReadCore(Span<byte> buffer)
    {
        EnsureDecompressionMode();
        EnsureNotDisposed();
        EnsureBufferInitialized();

        int totalRead = 0;

        DebugExtensions.Assert(_decompressor != null);
        while (true)
        {
            int bytesRead = _decompressor.Decompress(buffer.Slice(totalRead));
            totalRead += bytesRead;

            if (totalRead == buffer.Length)
            {
                break;
            }

            DebugExtensions.Assert(_buffer != null);
#if NET8_0_OR_GREATER
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
                ThrowHelper.ThrowInvalidDataException("Insufficient buffer");
            }

            _decompressor.SetInput(_buffer.AsMemory(0, bytes));
        }

        return totalRead;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
        CancellationToken cancellationToken) =>
        AsTask(ReadAsyncCore(buffer.AsMemory(offset, count), cancellationToken));

#if NET8_0_OR_GREATER
    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new CancellationToken()) =>
        ReadAsyncCore(buffer, cancellationToken);
#endif

    private
#if NET8_0_OR_GREATER
        ValueTask<int>
#else
        Task<int>
#endif
        ReadAsyncCore(Memory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
    {
        EnsureDecompressionMode();
        EnsureNoActiveAsyncOperation();
        EnsureNotDisposed();

        if (cancellationToken.IsCancellationRequested)
        {
            return FromCanceled<int>(cancellationToken);
        }

        EnsureBufferInitialized();

        bool cleanup = true;
        AsyncOperationStarting();
        try
        {
            DebugExtensions.Assert(_decompressor != null);

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
                return FromResult(bytesRead);
            }

#if NET8_0_OR_GREATER
            ValueTask<int> readTask = _stream.ReadAsync(_buffer, cancellationToken);
#else
            Task<int> readTask = _stream.ReadAsync(_buffer, 0, _buffer.Length, cancellationToken);
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

    private async
#if NET8_0_OR_GREATER
        ValueTask<int> FinishReadAsyncMemory(ValueTask<int> readTask,
#else
        Task<int> FinishReadAsyncMemory(Task<int> readTask,
#endif
        Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            DebugExtensions.Assert(_decompressor != null && _buffer != null);
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
                    ThrowHelper.ThrowInvalidDataException("Insufficient buffer");
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
#if NET8_0_OR_GREATER
                    readTask = _stream.ReadAsync(_buffer, cancellationToken);
#else
                    readTask = _stream.ReadAsync(_buffer, 0, _buffer.Length, cancellationToken);
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

#if NET8_0_OR_GREATER

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer) => WriteCore(buffer);

    /// <inheritdoc />
    public override void WriteByte(byte value) => WriteCore(MemoryMarshal.CreateReadOnlySpan(ref value, 1));

#endif

    private void WriteCore(ReadOnlySpan<byte> buffer)
    {
        EnsureCompressionMode();
        EnsureNotDisposed();

        DebugExtensions.Assert(_compressor != null);
        _compressor.Write(buffer, _stream);

        _wroteBytes = true;
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        AsTask(WriteAsyncCore(buffer.AsMemory(offset, count), cancellationToken));

#if NET8_0_OR_GREATER
    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        WriteAsyncCore(buffer, cancellationToken);
#endif

    private
#if NET8_0_OR_GREATER
        ValueTask
#else
        Task
#endif
        WriteAsyncCore(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        EnsureCompressionMode();
        EnsureNoActiveAsyncOperation();
        EnsureNotDisposed();

        return cancellationToken.IsCancellationRequested
            ? FromCanceled(cancellationToken)
            : WriteAsyncMemoryCore(buffer, cancellationToken);
    }

    private async
#if NET8_0_OR_GREATER
        ValueTask
#else
        Task
#endif
        WriteAsyncMemoryCore(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        AsyncOperationStarting();
        try
        {
            DebugExtensions.Assert(_stream != null);
            DebugExtensions.Assert(_compressor != null);

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

        DebugExtensions.Assert(_compressor != null);
        // Make sure to only "flush" when we actually had some input
        if (_wroteBytes)
        {
            Flush();
        }
    }

#if NET8_0_OR_GREATER

    private ValueTask PurgeBuffersAsync()
    {
        // Same logic as PurgeBuffers, except with async counterparts.

        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        if (_stream == null || _mode != CompressionMode.Compress)
        {
            return default;
        }

        DebugExtensions.Assert(_compressor != null);
        // Make sure to only "flush" when we actually had some input
        if (_wroteBytes)
        {
            return new ValueTask(FlushAsync());
        }

        return default;
    }

#endif

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
                            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                        }
                    }

                    base.Dispose(disposing);
                }
            }
        }
    }

#if NET8_0_OR_GREATER
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
            Stream? stream = _stream;
            _stream = null;
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
                            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                        }
                    }
                }
            }
        }
    }
#endif

    #endregion

    [MemberNotNull(nameof(_stream))]
    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_stream is null, this);
    }

    private void EnsureDecompressionMode()
    {
        if (_mode != CompressionMode.Decompress)
        {
            ThrowHelper.ThrowNotSupportedException();
        }
    }

    private void EnsureCompressionMode()
    {
        if (_mode != CompressionMode.Compress)
        {
            ThrowHelper.ThrowNotSupportedException();
        }
    }

    [MemberNotNull(nameof(_buffer))]
    private void EnsureBufferInitialized()
    {
        _buffer ??= ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
    }

    #region async controls

    private int _activeAsyncOperation;
    private bool AsyncOperationIsActive => _activeAsyncOperation != 0;

    private void EnsureNoActiveAsyncOperation()
    {
        if (AsyncOperationIsActive)
        {
            ThrowHelper.ThrowInvalidOperationException("Invalid begin call");
        }
    }

    private void AsyncOperationStarting()
    {
        if (Interlocked.CompareExchange(ref _activeAsyncOperation, 1, 0) != 0)
        {
            ThrowHelper.ThrowInvalidOperationException("Invalid begin call");
        }
    }

    private void AsyncOperationCompleting()
    {
        int oldValue = Interlocked.CompareExchange(ref _activeAsyncOperation, 0, 1);
        DebugExtensions.Assert(oldValue == 1, $"Expected {nameof(_activeAsyncOperation)} to be 1, got {oldValue}");
    }

    // The helpers below aid with the various conditional compilation differences between legacy Task-based
    // async methods and modern .NET ValueTask-based async methods. Since all call paths in legacy .NET always
    // end up a Task in the end before returning to the caller, we can use Task throughout these code paths
    // and avoid a dependency on System.Threading.Tasks.Extensions for ValueTask support.

#if NET8_0_OR_GREATER
    private static ValueTask<T> FromResult<T>(T result) => ValueTask.FromResult(result);
    private static ValueTask<T> FromCanceled<T>(CancellationToken cancellationToken) => ValueTask.FromCanceled<T>(cancellationToken);
    private static ValueTask FromCanceled(CancellationToken cancellationToken) => ValueTask.FromCanceled(cancellationToken);
    private static Task<T> AsTask<T>(ValueTask<T> valueTask) => valueTask.AsTask();
    private static Task AsTask(ValueTask valueTask) => valueTask.AsTask();
#else
    private static Task<T> FromResult<T>(T result) => Task.FromResult(result);
    private static Task<T> FromCanceled<T>(CancellationToken cancellationToken) => Task.FromCanceled<T>(cancellationToken);
    private static Task FromCanceled(CancellationToken cancellationToken) => Task.FromCanceled(cancellationToken);
    private static Task<T> AsTask<T>(Task<T> valueTask) => valueTask;
    private static Task AsTask(Task valueTask) => valueTask;
#endif

    #endregion
}
