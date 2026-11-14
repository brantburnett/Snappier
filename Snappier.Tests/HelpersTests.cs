namespace Snappier.Tests;

public class HelpersTests
{
    #region ExtractLowBytes

    [Theory]
    [InlineData(0x12345678u, 0, 0x00000000u)]
    [InlineData(0x12345678u, 1, 0x00000078u)]
    [InlineData(0x12345678u, 2, 0x00005678u)]
    [InlineData(0x12345678u, 3, 0x00345678u)]
    [InlineData(0x12345678u, 4, 0x12345678u)]
    [InlineData(0xFFFFFFFFu, 4, 0xFFFFFFFFu)]
    public void ExtractLowBytes(uint value, int numBytes, uint expected)
    {
        // Act

        uint result = Helpers.ExtractLowBytes(value, numBytes);

        // Assert

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0x12345678u, 0, 0x00000000u)]
    [InlineData(0x12345678u, 1, 0x00000078u)]
    [InlineData(0x12345678u, 2, 0x00005678u)]
    [InlineData(0x12345678u, 3, 0x00345678u)]
    [InlineData(0x12345678u, 4, 0x12345678u)]
    [InlineData(0xFFFFFFFFu, 4, 0xFFFFFFFFu)]
    public void ExtractLowBytesFallback(uint value, int numBytes, uint expected)
    {
        // Act

        uint result = Helpers.ExtractLowBytesFallback(value, numBytes);

        // Assert

        Assert.Equal(expected, result);
    }

    #endregion

    #region LeftShiftOverflows

    [Theory]
    [InlineData(2, 31)]
    [InlineData(0xff, 25)]
    public void LeftShiftOverflows_True(byte value, int shift)
    {
        // Act

        bool result = Helpers.LeftShiftOverflows(value, shift);

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

        bool result = Helpers.LeftShiftOverflows(value, shift);

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

        int result = Helpers.Log2Floor(value);

        // Assert

        Assert.Equal((int) Math.Floor(Math.Log(value, 2)), result);
    }

    [Fact]
    public void Log2Floor_Zero()
    {
        // Act

        int result = Helpers.Log2Floor(0);

        // Assert

        Assert.Equal(0, result);
    }

    #endregion
}
