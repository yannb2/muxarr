using System.Net;
using System.Text.Json;
using Muxarr.Core.Config;
using Muxarr.Web.Services;
using Muxarr.Web.Services.Notifications;
using Muxarr.Web.Services.Notifications.Providers;

namespace Muxarr.Tests;

[TestClass]
public class NotificationEventTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private static async Task<CapturingHandler> SendJellyfin(Dictionary<string, string> settings, string? filePath)
    {
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        var provider = new JellyfinProvider();
        settings.TryAdd("ServerUrl", "http://jellyfin:8096/");
        settings.TryAdd("ApiKey", "key");
        var config = new NotificationConfig { Provider = provider.Type, Settings = settings };
        var payload = new NotificationPayload
        {
            Title = "Conversion Completed",
            Body = "Movie (2026).mkv - saved 1 GB",
            EventType = NotificationEventType.Completed,
            FilePath = filePath
        };

        await provider.SendAsync(client, config, payload);
        return handler;
    }

    // --- Jellyfin: refresh only the file ---

    [TestMethod]
    public async Task Jellyfin_FileOnly_PostsTheConvertedPath()
    {
        var handler = await SendJellyfin(new() { ["RefreshFileOnly"] = "true" },
            "/movies/live-action/Movie (2026)/Movie (2026).mkv");

        Assert.AreEqual("http://jellyfin:8096/Library/Media/Updated", handler.Request!.RequestUri!.ToString());
        Assert.AreEqual("MediaBrowser Token=\"key\"", handler.Request.Headers.GetValues("Authorization").Single());
        var update = JsonDocument.Parse(handler.Body!).RootElement.GetProperty("updates")[0];
        Assert.AreEqual("/movies/live-action/Movie (2026)/Movie (2026).mkv", update.GetProperty("path").GetString());
        Assert.AreEqual("Modified", update.GetProperty("updateType").GetString());
    }

    [TestMethod]
    public async Task Jellyfin_FileOnly_MapsThePath()
    {
        var handler = await SendJellyfin(new()
        {
            ["RefreshFileOnly"] = "true",
            ["MuxarrPathPrefix"] = "/movies/",
            ["JellyfinPathPrefix"] = "/data/movies"
        }, "/movies/anime/Movie (2026)/Movie (2026).mkv");

        var update = JsonDocument.Parse(handler.Body!).RootElement.GetProperty("updates")[0];
        Assert.AreEqual("/data/movies/anime/Movie (2026)/Movie (2026).mkv", update.GetProperty("path").GetString());
    }

    [TestMethod]
    public async Task Jellyfin_FileOnly_WithoutAPathRefreshesTheLibrary()
    {
        var handler = await SendJellyfin(new() { ["RefreshFileOnly"] = "true" }, null);

        Assert.AreEqual("http://jellyfin:8096/Library/Refresh", handler.Request!.RequestUri!.ToString());
    }

    [TestMethod]
    public async Task Jellyfin_FileOnlyOff_RefreshesTheLibrary()
    {
        var handler = await SendJellyfin(new(), "/movies/Movie.mkv");

        Assert.AreEqual("http://jellyfin:8096/Library/Refresh", handler.Request!.RequestUri!.ToString());
    }

    [TestMethod]
    [DataRow("/movies/a.mkv", "/movies", "/data/movies", "/data/movies/a.mkv")]
    [DataRow("/movies/a.mkv", "/movies/", "/data/movies/", "/data/movies/a.mkv")]
    [DataRow("/movies-4k/a.mkv", "/movies", "/data/movies", "/movies-4k/a.mkv")]
    [DataRow("/tv/a.mkv", "/movies", "/data/movies", "/tv/a.mkv")]
    [DataRow("/movies/a.mkv", "", "/data", "/movies/a.mkv")]
    [DataRow(@"M:\movies\a.mkv", @"M:\movies", "/data/movies", @"/data/movies\a.mkv")]
    public void Jellyfin_MapPath(string path, string from, string to, string expected)
    {
        Assert.AreEqual(expected, JellyfinProvider.MapPath(path, from, to));
    }

    // --- Scan warnings ---

    [TestMethod]
    public void ScanWarning_LimitedPerWindow()
    {
        var sent = new Queue<DateTime>();
        var start = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

        var results = Enumerable.Range(0, NotificationService.ScanWarningLimit + 2)
            .Select(i => NotificationService.TakeScanWarningSlot(sent, start.AddSeconds(i)))
            .ToList();

        Assert.AreEqual(NotificationService.ScanWarningLimit, results.Count(r => r.Send));
        Assert.IsTrue(results[NotificationService.ScanWarningLimit - 1].Last, "the last allowed one says so");
        Assert.IsFalse(results[0].Last);
        Assert.IsFalse(results[^1].Send);

        var later = start + NotificationService.ScanWarningWindow + TimeSpan.FromSeconds(1);
        Assert.IsTrue(NotificationService.TakeScanWarningSlot(sent, later).Send, "the window rolls");
    }

    [TestMethod]
    public void ScanWarning_PayloadUsesTheLastErrorLine()
    {
        var payload = NotificationService.BuildScanWarningPayload(
            new ScanWarningEvent("/movies/Movie (2026)/Movie (2026).mkv",
                "[matroska @ 0x1] Element at 0x2 ending at 0x3 exceeds containing master element\nTruncated file\n",
                false), false);

        Assert.AreEqual("Scan Warning", payload.Title);
        Assert.AreEqual(NotificationEventType.ScanWarning, payload.EventType);
        Assert.AreEqual("Movie (2026).mkv - Truncated file", payload.Body);
        Assert.AreEqual("/movies/Movie (2026)/Movie (2026).mkv", payload.FilePath);
    }

    [TestMethod]
    public void ScanWarning_FailedProbeWithoutOutput()
    {
        var payload = NotificationService.BuildScanWarningPayload(
            new ScanWarningEvent("/movies/a.mkv", null, true), true);

        Assert.AreEqual("Scan Failed", payload.Title);
        StringAssert.StartsWith(payload.Body, "a.mkv - ffprobe could not read the file (more warnings");
    }
}
