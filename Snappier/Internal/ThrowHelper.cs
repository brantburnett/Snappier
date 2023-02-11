using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;

namespace Snappier.Internal
{
    internal static class ThrowHelper
    {
        [DoesNotReturn]
        public static void ThrowArgumentException(string? message, string? paramName) =>
            throw new ArgumentException(message, paramName);

        [DoesNotReturn]
        public static void ThrowArgumentOutOfRangeException(string? paramName, string? message) =>
            throw new ArgumentOutOfRangeException(paramName, message);

#if NET6_0_OR_GREATER
        public static void ThrowIfNull([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null) =>
            ArgumentNullException.ThrowIfNull(argument, paramName);
#else
        [DoesNotReturn]
        private static void ThrowArgumentNullException(string? paramName) =>
            throw new ArgumentNullException(paramName);

        public static void ThrowIfNull([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null)
            {
                ThrowArgumentNullException(paramName);
            }
        }
#endif

        [DoesNotReturn]
        public static void ThrowInvalidDataException(string? message) =>
            throw new InvalidDataException(message);

        [DoesNotReturn]
        public static void ThrowInvalidOperationException(string? message) =>
            throw new InvalidOperationException(message);

        [DoesNotReturn]
        public static void ThrowNotSupportedException() =>
            throw new NotSupportedException();

        [DoesNotReturn]
        public static void ThrowObjectDisposedException(string? objectName) =>
            throw new ObjectDisposedException(objectName);
    }
}
