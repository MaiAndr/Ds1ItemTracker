namespace Ds1ItemTracker.Services;

using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class UpdateChecker
{
    private const string ApiUrl =
        "https://api.github.com/repos/MaiAndr/Ds1ItemTracker/releases/latest";

    private const string ReleasesUrl =
        "https://github.com/MaiAndr/Ds1ItemTracker/releases/latest";

    /// <summary>Null = up to date or check failed. Non-null = newer tag available.</summary>
    public string? LatestTag { get; private set; }

    public string ReleasePage => ReleasesUrl;

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    /// <summary>
    /// Checks GitHub for a newer release. Safe to call on a background thread.
    /// Returns true if a newer version was found.
    /// </summary>
    public async Task<bool> CheckAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Ds1ItemTracker", CurrentVersion.ToString()));
            client.Timeout = TimeSpan.FromSeconds(8);

            var json = await client.GetStringAsync(ApiUrl).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);
            if (release?.TagName is null) return false;

            // Strip leading 'v'
            var tagClean = release.TagName.TrimStart('v');
            if (!Version.TryParse(tagClean, out var latest)) return false;

            if (latest > CurrentVersion)
            {
                LatestTag = release.TagName;
                return true;
            }
        }
        catch { /* network errors are non-fatal */ }

        return false;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }
    }
}
