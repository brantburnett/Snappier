using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Snappier.Internal;

internal static class ThrowHelper
{
    [DoesNotReturn]
    public static void ThrowArgumentException(string? message, string? paramName) =>
        throw new ArgumentException(message, paramName);

    [DoesNotReturn]
    public static void ThrowArgumentOutOfRangeException(string? paramName, string? message) =>
        throw new ArgumentOutOfRangeException(paramName, message);

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)] // Avoid inlining to reduce code size for a cold path
    public static void ThrowArgumentExceptionInsufficientOutputBuffer(string? paramName) =>
        ThrowArgumentException("Output buffer is too small.", paramName);

    [DoesNotReturn]
    public static void ThrowInvalidDataException(string? message) =>
        throw new InvalidDataException(message);

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)] // Avoid inlining to reduce code size for a cold path
    public static void ThrowInvalidDataExceptionIncompleteSnappyBlock() =>
        throw new InvalidDataException("Incomplete Snappy block.");

    [DoesNotReturn]
    public static void ThrowInvalidOperationException(string? message = null) =>
        throw new InvalidOperationException(message);

    [DoesNotReturn]
    public static void ThrowNotSupportedException(string? message = null) =>
        throw new NotSupportedException(message);

    // The helpers below forward fixed messages to the general throw helpers above. They are not inlined so
    // the message string loads and argument setup stay out of the compiled code of hot-path callers.

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowArgumentExceptionInputTooLarge(string? paramName) =>
        ThrowArgumentException($"{paramName} is larger than the maximum size of {uint.MaxValue} bytes.", paramName);

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowArgumentOutOfRangeExceptionScratchLengthExceedsLimit(string? paramName) =>
        ThrowArgumentOutOfRangeException(paramName, "Scratch length exceeds limit");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidDataExceptionDataTooLong() =>
        ThrowInvalidDataException("Data too long");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidDataExceptionInvalidCopyOffset() =>
        ThrowInvalidDataException("Invalid copy offset");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidDataExceptionInvalidStreamLength() =>
        ThrowInvalidDataException("Invalid stream length");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidOperationExceptionAllDataDecompressed() =>
        ThrowInvalidOperationException("All data has been decompressed");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidOperationExceptionBlockNotFullyDecompressed() =>
        ThrowInvalidOperationException("Block is not fully decompressed.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidOperationExceptionBufferNotInitialized() =>
        ThrowInvalidOperationException("Buffer not initialized");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidOperationExceptionBufferWriterInsufficientSpan() =>
        ThrowInvalidOperationException("IBufferWriter<byte> did not return a sufficient span");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidOperationExceptionBufferWriterInsufficientLengthPrefixSpan() =>
        ThrowInvalidOperationException("IBufferWriter<byte> did not return a sufficient span for length prefix.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidOperationExceptionInsufficientBufferSize() =>
        ThrowInvalidOperationException("Insufficient buffer size");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidOperationExceptionInvalidStreamLength() =>
        ThrowInvalidOperationException("Invalid stream length");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidOperationExceptionNoDataPresent() =>
        ThrowInvalidOperationException("No data present.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidOperationExceptionOverlappingSpans() =>
        ThrowInvalidOperationException("Input and output spans must not overlap.");

#if !NET8_0_OR_GREATER
    [DoesNotReturn]
    private static void ThrowArgumentNullException(string? paramName) =>
        throw new ArgumentNullException(paramName);

    [DoesNotReturn]
    private static void ThrowObjectDisposedException(object? instance) =>
        throw new ObjectDisposedException(instance?.GetType().FullName);

    extension(ArgumentNullException)
    {
        public static void ThrowIfNull([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null)
            {
                ThrowArgumentNullException(paramName);
            }
        }
    }

    extension(ObjectDisposedException)
    {
        public static void ThrowIf([DoesNotReturnIf(true)] bool condition, object instance)
        {
            if (condition)
            {
                ThrowObjectDisposedException(instance);
            }
        }
    }
#endif
}
