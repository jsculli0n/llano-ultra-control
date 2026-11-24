namespace Llano.Core;

public readonly record struct CoolingSample(DateTimeOffset At, int? Rpm, float? Cpu, float? Gpu);

public sealed class CoolingHistory
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private static readonly TimeSpan DetailWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(950);
    private static readonly TimeSpan ArchiveInterval = TimeSpan.FromSeconds(10);
    private readonly Queue<CoolingSample> recent = new();
    private readonly Queue<CoolingSample> retained = new();
    private DateTimeOffset? lastRecent;
    private DateTimeOffset? lastRetained;

    public void Load(IEnumerable<CoolingSample> samples, DateTimeOffset now)
    {
        recent.Clear();
        retained.Clear();
        lastRecent = lastRetained = null;
        DateTimeOffset cutoff = now - Retention;
        foreach (var sample in samples.Where(s => s.At >= cutoff && s.At <= now).OrderBy(s => s.At))
        {
            if (lastRetained is { } previous && sample.At - previous < ArchiveInterval) continue;
            retained.Enqueue(sample);
            lastRetained = sample.At;
        }
    }

    public bool Record(CoolingSample sample, out bool archived)
    {
        archived = false;
        if (lastRecent is { } latest && sample.At < latest)
        {
            lastRecent = KeepThrough(recent, sample.At);
            if (lastRetained > sample.At)
            {
                lastRetained = KeepThrough(retained, sample.At);
                archived = true;
            }
        }
        if (lastRecent is { } previous && sample.At - previous < SampleInterval) return false;
        lastRecent = sample.At;
        recent.Enqueue(sample);
        Trim(recent, sample.At - DetailWindow);
        if (lastRetained is null || sample.At - lastRetained.Value >= ArchiveInterval)
        {
            retained.Enqueue(sample);
            lastRetained = sample.At;
            archived = true;
        }
        Trim(retained, sample.At - Retention);
        return true;
    }

    public CoolingSample[] View(DateTimeOffset now, TimeSpan range)
    {
        DateTimeOffset start = now - range;
        if (!recent.TryPeek(out var first))
            return retained.Where(s => s.At >= start && s.At <= now).ToArray();

        var detail = recent.Where(s => s.At >= start && s.At <= now);
        return start >= first.At ? detail.ToArray() :
            [.. retained.Where(s => s.At >= start && s.At < first.At && s.At <= now), .. detail];
    }

    public CoolingSample[] Archive() => retained.ToArray();

    public static CoolingSample? Nearest(ReadOnlySpan<CoolingSample> orderedSamples, DateTimeOffset time)
    {
        if (orderedSamples.IsEmpty) return null;
        int low = 0, high = orderedSamples.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (orderedSamples[middle].At < time) low = middle + 1;
            else high = middle;
        }
        if (low == 0) return orderedSamples[0];
        var before = orderedSamples[low - 1];
        if (low == orderedSamples.Length) return before;
        var after = orderedSamples[low];
        return time - before.At <= after.At - time ? before : after;
    }

    private static DateTimeOffset? KeepThrough(Queue<CoolingSample> queue, DateTimeOffset time)
    {
        DateTimeOffset? last = null;
        for (int remaining = queue.Count; remaining > 0; remaining--)
        {
            var sample = queue.Dequeue();
            if (sample.At > time) continue;
            queue.Enqueue(sample);
            last = sample.At;
        }
        return last;
    }

    private static void Trim(Queue<CoolingSample> queue, DateTimeOffset cutoff)
    {
        while (queue.TryPeek(out var oldest) && oldest.At < cutoff) queue.Dequeue();
    }
}
