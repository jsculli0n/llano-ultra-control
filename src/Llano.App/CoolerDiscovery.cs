using Llano.Core;

namespace Llano.App;

// A single USB worker owns this cache. Recheck all interfaces before writing.
internal sealed class CoolerDiscovery
{
    private static readonly TimeSpan DiscoveryInterval = TimeSpan.FromSeconds(4);
    private IReadOnlyList<HidCooler> devices = [];
    private TimeSpan nextDiscovery;
    private bool previouslyResponded;

    public (IReadOnlyList<HidCooler> Devices, V12UltraStatus? Status) Poll(TimeSpan now, bool force = false)
    {
        bool discovered = force || now >= nextDiscovery;
        if (discovered) Discover(now);
        var status = devices.Count == 1 ? devices[0].ReadStatus() : null;
        if (!discovered && previouslyResponded && status is null)
        {
            Discover(now);
            if (devices.Count == 1) status = devices[0].ReadStatus();
        }
        previouslyResponded = status is not null;
        return (devices, status);
    }

    private void Discover(TimeSpan now)
    {
        devices = HidCooler.Find();
        nextDiscovery = now + DiscoveryInterval;
    }
}
