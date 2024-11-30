using System;
using Snappier.Internal;
using Xunit;

namespace Snappier.Tests
{
    public class HelpersTests
    {
        #region LeftShiftOverflows

        [Theory]
        [InlineData(2, 31)]
        [InlineData(0xff, 25)]
        public void LeftShiftOverflows_True(byte value, int shift)
        {
            // Act

            var result = Helpers.LeftShiftOverflows(value, shift);

            // Assert

            Assert.True(result);
        }

        [Theory]
        [InlineData(1, 31)]
        [InlineData(0xff, 24)]
        [InlineData(0, 31)]
        public void LeftShiftOverflows_False(byte value, int shift)
        {
            // Act

            var result = Helpers.LeftShiftOverflows(value, shift);

            // Assert

            Assert.False(result);
        }

        public static TheoryData<uint> Log2FloorValues() =>
        [
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31
        ];

        [Theory]
        [MemberData(nameof(Log2FloorValues))]
        public void Log2Floor(uint value)
        {
            // Act

            var result = Helpers.Log2Floor(value);

            // Assert

            Assert.Equal((int) Math.Floor(Math.Log(value, 2)), result);
        }

        [Fact]
        public void Log2Floor_Zero()
        {
            // Act

            var result = Helpers.Log2Floor(0);

            // Assert

            Assert.Equal(0, result);
        }

        #endregion
    }
}
