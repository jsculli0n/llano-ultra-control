namespace Llano.Core;

public sealed class SignInPowerOn(TimeSpan startedAt)
{
    private static readonly TimeSpan DiscoveryWindow = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);
    private const int MaximumAttempts = 3;
    private readonly TimeSpan deadline = startedAt + DiscoveryWindow;
    private TimeSpan nextAttempt = startedAt;
    private int attempts;

    public bool Pending { get; private set; } = true;

    public bool TryAttempt(TimeSpan now, bool? powered)
    {
        if (!Pending) return false;
        if (powered == true || now >= deadline)
        {
            Pending = false;
            return false;
        }
        if (powered is null || now < nextAttempt) return false;

        nextAttempt = now + RetryInterval;
        if (++attempts == MaximumAttempts) Pending = false;
        return true;
    }
}
