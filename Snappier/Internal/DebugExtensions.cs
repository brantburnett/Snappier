using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Snappier.Internal;

internal static class DebugExtensions
{
    // Variant of Debug.Assert that is marked with DoesNotReturnIf and CallerArgumentExpression on down-level runtimes
    [Conditional("DEBUG")]
    public static void Assert([DoesNotReturnIf(false)] bool condition,
        [CallerArgumentExpression(nameof(condition))] string? message = null)
    {
#if NET8_0_OR_GREATER
        Debug.Assert(condition, message);
#else
        if (!condition)
        {
            Debug.Fail(message);
        }
#endif
    }

#if !NET8_0_OR_GREATER

    // Variant of Debug.Fail that is marked with DoesNotReturn on down-level runtimes
    [Conditional("DEBUG")]
    [DoesNotReturn]
    private static void Fail(string message)
    {
        Debug.Fail(message);

        // Unreachable but prevents compiler warnings, on down-level runtimes Debug.Fail is not marked as DoesNotReturn
        ThrowHelper.ThrowInvalidOperationException(message);
    }

#endif
}
