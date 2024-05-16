using System;
using System.Collections.Generic;
using System.Linq;
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

        #endregion

        #region Log2FloorNonZero

        public static IEnumerable<object[]> Log2FloorNonZeroValues() =>
            Enumerable.Range(1, 31).Select(p => new object[] {p});

        [Theory]
        [MemberData(nameof(Log2FloorNonZeroValues))]
        public void Log2FloorNonZero(uint value)
        {
            // Act

            var result = Helpers.Log2FloorNonZero(value);

            // Assert

            Assert.Equal((int) Math.Floor(Math.Log(value, 2)), result);
        }

        #endregion
    }
}
