using System.Net.Http.Json;
using Muxarr.Web.Components.Shared;

namespace Muxarr.Web.Services.Notifications.Providers;

public class JellyfinSettings
{
    [Field("Server URL", Type = FieldType.Url, Placeholder = "http://192.168.1.10:8096",
        HelpText = "Your Jellyfin server base URL.")]
    public string ServerUrl { get; set; } = "";

    [Field("API Key", Type = FieldType.Password,
        HelpText = "Generate one at Dashboard > API Keys.")]
    public string ApiKey { get; set; } = "";

    [Field("Library Item ID", Placeholder = "leave blank to refresh the entire server",
        HelpText = "VirtualFolder ItemId. Find via /Library/VirtualFolders.")]
    public string LibraryItemId { get; set; } = "";

    [Field("Full metadata refresh", Type = FieldType.Checkbox,
        HelpText =
            "Re-fetches all metadata and images instead of only what's missing. Slower; only applies when a Library Item ID is set.")]
    public bool FullMetadataRefresh { get; set; }

    [Field("Refresh only the file", Type = FieldType.Checkbox,
        HelpText =
            "Tells Jellyfin which file changed instead of refreshing a whole library, so the new tracks show up in " +
            "seconds. Events without a file (Queue finished, Test) still use the refresh above.")]
    public bool RefreshFileOnly { get; set; }

    [Field("Muxarr path prefix", Placeholder = "/movies",
        HelpText = "Only when Jellyfin sees the files under another path: the start of the path as Muxarr sees it.")]
    public string MuxarrPathPrefix { get; set; } = "";

    [Field("Jellyfin path prefix", Placeholder = "/data/movies",
        HelpText = "What that prefix is replaced with for Jellyfin.")]
    public string JellyfinPathPrefix { get; set; } = "";
}

public class JellyfinProvider : NotificationProvider<JellyfinSettings>
{
    public override string Icon => "bi-collection-play";

    protected override async Task SendCoreAsync(HttpClient client, JellyfinSettings s, NotificationPayload payload)
    {
        if (string.IsNullOrWhiteSpace(s.ApiKey))
        {
            throw new InvalidOperationException("Jellyfin API Key is required.");
        }

        using var request = s.RefreshFileOnly && !string.IsNullOrWhiteSpace(payload.FilePath)
            ? BuildFileUpdatedRequest(s.ServerUrl,
                MapPath(payload.FilePath, s.MuxarrPathPrefix, s.JellyfinPathPrefix))
            : new HttpRequestMessage(HttpMethod.Post,
                BuildRefreshUrl(s.ServerUrl, s.LibraryItemId, s.FullMetadataRefresh));

        // Modern Jellyfin auth. The legacy X-Emby-Token header still works on 10.11.x but the
        // DisableLegacyAuthorization migration in Jellyfin master will flip it off on upgrade.
        request.Headers.Add("Authorization", $"MediaBrowser Token=\"{s.ApiKey}\"");
        await SendRequestAsync(client, request);
    }

    /// <summary>
    /// Builds the refresh endpoint URL. Jellyfin and Emby share the same /Library/Refresh and
    /// /Items/{id}/Refresh paths (Jellyfin forked from Emby in 2018), so EmbyProvider also calls this.
    /// Auth headers differ - each provider adds its own.
    /// </summary>
    public static string BuildRefreshUrl(string serverUrl, string libraryItemId, bool fullMetadataRefresh)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            throw new InvalidOperationException("Server URL is required.");
        }

        var baseUrl = serverUrl.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(libraryItemId))
        {
            return $"{baseUrl}/Library/Refresh";
        }

        var mode = fullMetadataRefresh ? "FullRefresh" : "Default";
        return $"{baseUrl}/Items/{Uri.EscapeDataString(libraryItemId.Trim())}/Refresh"
               + $"?metadataRefreshMode={mode}&imageRefreshMode={mode}";
    }

    /// <summary>
    /// <c>POST /Library/Media/Updated</c>: Jellyfin finds the item by path and
    /// rescans just that file, the same call Radarr and Sonarr make after an import.
    /// </summary>
    public static HttpRequestMessage BuildFileUpdatedRequest(string serverUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            throw new InvalidOperationException("Server URL is required.");
        }

        var body = new { Updates = new[] { new { Path = path, UpdateType = "Modified" } } };
        return new HttpRequestMessage(HttpMethod.Post, $"{serverUrl.TrimEnd('/')}/Library/Media/Updated")
        {
            Content = JsonContent.Create(body)
        };
    }

    /// <summary>Swaps <paramref name="from"/> for <paramref name="to"/> at the start of the path; unchanged otherwise.</summary>
    public static string MapPath(string path, string? from, string? to)
    {
        if (string.IsNullOrWhiteSpace(from))
        {
            return path;
        }

        var prefix = from.Trim().TrimEnd('/', '\\');
        var rest = path.Length > prefix.Length ? path[prefix.Length..] : "";
        var atBoundary = path.Length == prefix.Length || rest.StartsWith('/') || rest.StartsWith('\\');
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || !atBoundary)
        {
            return path;
        }

        return (to ?? "").Trim().TrimEnd('/', '\\') + rest;
    }
}
