using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Snappier.Internal;
using Xunit;

namespace Snappier.Tests
{
    public class HelpersTests
    {
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
