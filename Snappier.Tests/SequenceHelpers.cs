using System.Buffers;

#nullable enable

namespace Snappier.Tests;

public static class SequenceHelpers
{
    public static ReadOnlySequence<byte> CreateSequence(ReadOnlyMemory<byte> source, int maxSegmentSize)
    {
        ReadOnlySequenceSegment<byte>? lastSegment = null;
        ReadOnlySequenceSegment<byte>? currentSegment = null;

        while (source.Length > 0)
        {
            int index = Math.Max(source.Length - maxSegmentSize, 0);

            currentSegment = new Segment(
                source.Slice(index),
                currentSegment,
                index);

            lastSegment ??= currentSegment;
            source = source.Slice(0, index);
        }

        if (currentSegment is null)
        {
            return default;
        }

        return new ReadOnlySequence<byte>(currentSegment, 0, lastSegment!, lastSegment!.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory, ReadOnlySequenceSegment<byte>? next, long runningIndex)
        {
            Memory = memory;
            Next = next;
            RunningIndex = runningIndex;
        }
    }
}
