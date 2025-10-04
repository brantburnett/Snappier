namespace Snappier.Benchmarks;

public class LeftShiftOverflows
{
    private byte _value = 24;
    private int _shift = 7;

    [Benchmark(Baseline = true)]
    public bool Current()
    {
        return Helpers.LeftShiftOverflows(_value, _shift);
    }
}
