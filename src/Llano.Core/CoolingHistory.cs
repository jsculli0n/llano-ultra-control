namespace Llano.Core;

public readonly record struct CoolingSample(DateTimeOffset At, int? Rpm, float? Cpu, float? Gpu);

public sealed class CoolingHistory
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private static readonly TimeSpan DetailWindow = TimeSpan.FromMinutes(5);
    private readonly Queue<CoolingSample> recent = new();
    private readonly Queue<CoolingSample> retained = new();
    private DateTimeOffset? lastRecent;
    private DateTimeOffset? lastRetained;

    public void Load(IEnumerable<CoolingSample> samples, DateTimeOffset now)
    {
        foreach (var sample in samples.Where(s => s.At >= now - Retention && s.At <= now).OrderBy(s => s.At))
            retained.Enqueue(sample);
        lastRetained = retained.Count == 0 ? null : retained.Last().At;
    }

    public bool Record(CoolingSample sample, out bool archived)
    {
        archived = false;
        if (lastRecent is { } latest && sample.At < latest)
        {
            recent.Clear();
            retained.Clear();
            lastRecent = lastRetained = null;
        }
        if (lastRecent is { } previous && sample.At - previous < TimeSpan.FromMilliseconds(950)) return false;
        lastRecent = sample.At;
        recent.Enqueue(sample);
        Trim(recent, sample.At - DetailWindow);
        if (lastRetained is null || sample.At - lastRetained.Value >= TimeSpan.FromSeconds(10))
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
        DateTimeOffset firstRecent = recent.TryPeek(out var first) ? first.At : now;
        return [.. retained.Where(s => s.At >= start && s.At < firstRecent),
            .. recent.Where(s => s.At >= start)];
    }

    public CoolingSample[] Archive() => retained.ToArray();

    private static void Trim(Queue<CoolingSample> queue, DateTimeOffset cutoff)
    {
        while (queue.TryPeek(out var oldest) && oldest.At < cutoff) queue.Dequeue();
    }
}
