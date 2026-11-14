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
    public static void ThrowArgumentExceptionInsufficientOutputBuffer(string? paramName) =>
        throw new ArgumentException("Output buffer is too small.", paramName);

    [DoesNotReturn]
    public static void ThrowInvalidDataException(string? message) =>
        throw new InvalidDataException(message);

    [DoesNotReturn]
    public static void ThrowInvalidDataExceptionIncompleteSnappyBlock() =>
        throw new InvalidDataException("Incomplete Snappy block.");

    [DoesNotReturn]
    public static void ThrowInvalidOperationException(string? message = null) =>
        throw new InvalidOperationException(message);

    [DoesNotReturn]
    public static void ThrowNotSupportedException(string? message = null) =>
        throw new NotSupportedException(message);

    // The helpers below hold fixed messages so the message string loads and argument setup stay out of the compiled
    // code of hot-path callers. Each one throws directly and is not marked NoInlining. The JIT never inlines a method
    // that only throws, and when it can see the callee's IL it marks the call as no-return. That lets it move the call
    // to a cold block and merge identical throw sites. NoInlining or forwarding to another helper hides the throw, so
    // the JIT keeps every call site and the code after it.

    [DoesNotReturn]
    public static void ThrowArgumentExceptionInputTooLarge(string? paramName) =>
        throw new ArgumentException($"{paramName} is larger than the maximum size of {uint.MaxValue} bytes.", paramName);

    [DoesNotReturn]
    public static void ThrowArgumentOutOfRangeExceptionScratchLengthExceedsLimit(string? paramName) =>
        throw new ArgumentOutOfRangeException(paramName, "Scratch length exceeds limit");

    [DoesNotReturn]
    public static void ThrowInvalidDataExceptionDataTooLong() =>
        throw new InvalidDataException("Data too long");

    [DoesNotReturn]
    public static void ThrowInvalidDataExceptionInvalidCopyOffset() =>
        throw new InvalidDataException("Invalid copy offset");

    [DoesNotReturn]
    public static void ThrowInvalidDataExceptionInvalidStreamLength() =>
        throw new InvalidDataException("Invalid stream length");

    [DoesNotReturn]
    public static void ThrowInvalidOperationExceptionAllDataDecompressed() =>
        throw new InvalidOperationException("All data has been decompressed");

    [DoesNotReturn]
    public static void ThrowInvalidOperationExceptionBlockNotFullyDecompressed() =>
        throw new InvalidOperationException("Block is not fully decompressed.");

    [DoesNotReturn]
    public static void ThrowInvalidOperationExceptionBufferNotInitialized() =>
        throw new InvalidOperationException("Buffer not initialized");

    [DoesNotReturn]
    public static void ThrowInvalidOperationExceptionBufferWriterInsufficientSpan() =>
        throw new InvalidOperationException("IBufferWriter<byte> did not return a sufficient span");

    [DoesNotReturn]
    public static void ThrowInvalidOperationExceptionBufferWriterInsufficientLengthPrefixSpan() =>
        throw new InvalidOperationException("IBufferWriter<byte> did not return a sufficient span for length prefix.");

    [DoesNotReturn]
    public static void ThrowInvalidOperationExceptionInsufficientBufferSize() =>
        throw new InvalidOperationException("Insufficient buffer size");

    [DoesNotReturn]
    public static void ThrowInvalidOperationExceptionInvalidStreamLength() =>
        throw new InvalidOperationException("Invalid stream length");

    [DoesNotReturn]
    public static void ThrowInvalidOperationExceptionNoDataPresent() =>
        throw new InvalidOperationException("No data present.");

    [DoesNotReturn]
    public static void ThrowInvalidOperationExceptionOverlappingSpans() =>
        throw new InvalidOperationException("Input and output spans must not overlap.");

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
