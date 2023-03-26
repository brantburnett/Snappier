using System;
using System.Runtime.CompilerServices;
using System.Text;
using Snappier.Internal;
using Xunit;

namespace Snappier.Tests.Internal
{
    public class SnappyCompressorTests
    {
        #region FindMatchLength

        [Theory]
        [InlineData(6, "012345", "012345", 6)]
        [InlineData(11, "01234567abc", "01234567abc", 11)]

        // Hit s1_limit in 64-bit loop, find a non-match in single-character loop.
        [InlineData(9, "01234567abc", "01234567axc", 9)]

        // Same, but edge cases.
        [InlineData(11, "01234567abc!", "01234567abc!", 11)]
        [InlineData(11, "01234567abc!", "01234567abc?", 11)]

        // Find non-match at once in first loop.
        [InlineData(0, "01234567xxxxxxxx", "?1234567xxxxxxxx", 16)]
        [InlineData(1, "01234567xxxxxxxx", "0?234567xxxxxxxx", 16)]
        [InlineData(4, "01234567xxxxxxxx", "01237654xxxxxxxx", 16)]
        [InlineData(7, "01234567xxxxxxxx", "0123456?xxxxxxxx", 16)]

        // Find non-match in first loop after one block.
        [InlineData(8, "abcdefgh01234567xxxxxxxx", "abcdefgh?1234567xxxxxxxx", 24)]
        [InlineData(9, "abcdefgh01234567xxxxxxxx", "abcdefgh0?234567xxxxxxxx", 24)]
        [InlineData(12, "abcdefgh01234567xxxxxxxx", "abcdefgh01237654xxxxxxxx", 24)]
        [InlineData(15, "abcdefgh01234567xxxxxxxx", "abcdefgh0123456?xxxxxxxx", 24)]

        // 32-bit version:

        // Short matches.
        [InlineData(0, "01234567", "?1234567", 8)]
        [InlineData(1, "01234567", "0?234567", 8)]
        [InlineData(2, "01234567", "01?34567", 8)]
        [InlineData(3, "01234567", "012?4567", 8)]
        [InlineData(4, "01234567", "0123?567", 8)]
        [InlineData(5, "01234567", "01234?67", 8)]
        [InlineData(6, "01234567", "012345?7", 8)]
        [InlineData(7, "01234567", "0123456?", 8)]
        [InlineData(7, "01234567", "0123456?", 7)]
        [InlineData(7, "01234567!", "0123456??", 7)]

        // Hit s1_limit in 32-bit loop, hit s1_limit in single-character loop.
        [InlineData(10, "xxxxxxabcd", "xxxxxxabcd", 10)]
        [InlineData(10, "xxxxxxabcd?", "xxxxxxabcd?", 10)]
        [InlineData(13, "xxxxxxabcdef", "xxxxxxabcdefx", 13)]

        // Same, but edge cases.
        [InlineData(12, "xxxxxx0123abc!", "xxxxxx0123abc!", 12)]
        [InlineData(12, "xxxxxx0123abc!", "xxxxxx0123abc?", 12)]

        // Hit s1_limit in 32-bit loop, find a non-match in single-character loop.
        [InlineData(11, "xxxxxx0123abc", "xxxxxx0123axc", 13)]

        // Find non-match at once in first loop.
        [InlineData(6, "xxxxxx0123xxxxxxxx", "xxxxxx?123xxxxxxxx", 18)]
        [InlineData(7, "xxxxxx0123xxxxxxxx", "xxxxxx0?23xxxxxxxx", 18)]
        [InlineData(8, "xxxxxx0123xxxxxxxx", "xxxxxx0132xxxxxxxx", 18)]
        [InlineData(9, "xxxxxx0123xxxxxxxx", "xxxxxx012?xxxxxxxx", 18)]

        // Same, but edge cases.
        [InlineData(6, "xxxxxx0123", "xxxxxx?123", 10)]
        [InlineData(7, "xxxxxx0123", "xxxxxx0?23", 10)]
        [InlineData(8, "xxxxxx0123", "xxxxxx0132", 10)]
        [InlineData(9, "xxxxxx0123", "xxxxxx012?", 10)]

        // Find non-match in first loop after one block.
        [InlineData(10, "xxxxxxabcd0123xx", "xxxxxxabcd?123xx", 16)]
        [InlineData(11, "xxxxxxabcd0123xx", "xxxxxxabcd0?23xx", 16)]
        [InlineData(12, "xxxxxxabcd0123xx", "xxxxxxabcd0132xx", 16)]
        [InlineData(13, "xxxxxxabcd0123xx", "xxxxxxabcd012?xx", 16)]

        // Same, but edge cases.
        [InlineData(10, "xxxxxxabcd0123", "xxxxxxabcd?123", 14)]
        [InlineData(11, "xxxxxxabcd0123", "xxxxxxabcd0?23", 14)]
        [InlineData(12, "xxxxxxabcd0123", "xxxxxxabcd0132", 14)]
        [InlineData(13, "xxxxxxabcd0123", "xxxxxxabcd012?", 14)]
        public void FindMatchLength(int expectedResult, string s1String, string s2String, int length)
        {
            var array = Encoding.ASCII.GetBytes(s1String + s2String
                                                         + new string('\0', Math.Max(0, length - s2String.Length)));

            ulong data = 0;
            ref byte s1 = ref array[0];
            ref byte s2 = ref Unsafe.Add(ref s1, s1String.Length);

            var result =
                SnappyCompressor.FindMatchLength(ref s1, ref s2, ref Unsafe.Add(ref s2, length), ref data);

            Assert.Equal(result.matchLength < 8, result.matchLengthLessThan8);
            Assert.Equal(expectedResult, result.matchLength);
        }

        #endregion
    }
}
