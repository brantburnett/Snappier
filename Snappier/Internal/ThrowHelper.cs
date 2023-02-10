using System;
using System.Diagnostics.CodeAnalysis;

namespace Snappier.Internal
{
    internal static class ThrowHelper
    {
        [DoesNotReturn]
        public static void ThrowInvalidOperationException(string message)
        {
            throw new InvalidOperationException(message);
        }
    }
}
