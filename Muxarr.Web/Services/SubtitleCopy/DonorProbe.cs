using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Services.SubtitleCopy;

/// <summary>Probes the donors of a queued Copy subtitles conversion again, right before it runs.</summary>
public static class DonorProbe
{
    public sealed record Probed(List<(DonorTrack Donor, TrackSnapshot Track)> Tracks, string? Error);

    public static async Task<Probed> ProbeAsync(IEnumerable<DonorTrack> donors)
    {
        var tracks = new List<(DonorTrack, TrackSnapshot)>();
        var probes = new Dictionary<string, MediaFile>(StringComparer.Ordinal);
        foreach (var donor in donors)
        {
            if (!File.Exists(donor.Path))
            {
                return new Probed(tracks, $"The donor file is gone: {donor.Path}");
            }

            if (!probes.TryGetValue(donor.Path, out var file))
            {
                file = new MediaFile { Path = donor.Path };
                var result = await file.SetFileDataFromFFprobe();
                if (result.Result == null)
                {
                    return new Probed(tracks, $"ffprobe could not read the donor {donor.Path}: {result.Error}");
                }

                probes[donor.Path] = file;
            }

            var track = file.Snapshot.Tracks.FirstOrDefault(t => t.Index == donor.TrackId);
            if (track == null)
            {
                return new Probed(tracks, $"The donor {Path.GetFileName(donor.Path)} has no track {donor.TrackId} any more.");
            }

            tracks.Add((donor, track));
        }

        return new Probed(tracks, null);
    }
}
