using System.Net.Http;
using System.Text.Json;

namespace Llano.App;

internal static class ReleaseUpdates
{
    private const string Repository = "jsculli0n/llano-ultra-control";
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxResponseContentBufferSize = 1024 * 1024
    };

    static ReleaseUpdates()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd($"LlanoUltraControl/{MainWindow.ReleaseVersion}");
        Client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static async Task<Version> LatestAsync(CancellationToken cancellationToken)
    {
        using var reply = await Client.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", cancellationToken);
        reply.EnsureSuccessStatusCode();
        using var data = JsonDocument.Parse(await reply.Content.ReadAsStreamAsync(cancellationToken));
        if (data.RootElement.ValueKind == JsonValueKind.Object && data.RootElement.TryGetProperty("tag_name", out var value) &&
            value.ValueKind == JsonValueKind.String && value.GetString() is { } tag && tag.StartsWith('v') &&
            Version.TryParse(tag.AsSpan(1), out var version) && version.Build >= 0 && version.Revision < 0 &&
            tag == $"v{version.ToString(3)}")
            return version;

        throw new InvalidDataException("GitHub did not return a release version.");
    }

    public static string ReleasePage(Version version) => $"https://github.com/{Repository}/releases/tag/v{version.ToString(3)}";
}
