using Arrkit.Processes;
using Arrkit.Subtitles;
using Muxarr.Core.Extensions;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Services;

// "Add an SRT without SDH" (subtitle setting): a language whose kept full
// subtitles are all SDH gets an SRT of its PGS track with the SDH text taken
// out, read with Tesseract, as Ripparr does when it rips a disc.
public static class SdhFreeSrt
{
    // SourceIndex: the PGS track to read. Name and BeforeIndex come from running
    // the profile on the file as if the SRT were already in it, so the next scan
    // finds it named and placed as the profile wants and leaves it alone.
    public sealed record Candidate(int SourceIndex, string LanguageCode, string Name, int BeforeIndex);

    public static List<Candidate> Find(MediaFile file, ConversionPlan target, Profile? profile)
    {
        if (profile?.SubtitleSettings is not { AddSrtWithoutSdh: true }
            || file.Snapshot.ContainerType.ToContainerFamily() != ContainerFamily.Matroska)
        {
            return [];
        }

        var source = file.Snapshot.Tracks.GroupBy(t => t.Index).ToDictionary(g => g.Key, g => g.First());
        var full = target.Tracks
            .Where(t => t.Type == MediaTrackType.Subtitles && source.ContainsKey(t.Index))
            .Select(t => (Plan: t, Source: source[t.Index]))
            .Select(x => (x.Plan, x.Source,
                Language: x.Plan.LanguageCode ?? x.Source.LanguageCode,
                Forced: x.Plan.IsForced ?? x.Source.IsForced,
                Commentary: x.Plan.IsCommentary ?? x.Source.IsCommentary,
                Sdh: x.Plan.IsHearingImpaired ?? x.Source.IsHearingImpaired))
            .Where(x => !x.Forced && !x.Commentary)
            .ToList();

        var candidates = new List<Candidate>();
        foreach (var language in full.GroupBy(x => Base(x.Language)))
        {
            if (language.Key is "" or "und" || !language.All(x => x.Sdh))
            {
                continue;
            }

            var pgs = language.FirstOrDefault(x => SubtitleCodecExtensions.ParseSubtitleCodec(x.Source.Codec) == SubtitleCodec.Pgs);
            if (pgs.Plan == null)
            {
                continue;
            }

            var placed = PlaceByProfile(file, target, profile, pgs.Source, pgs.Language);
            if (placed != null)
            {
                candidates.Add(placed with { SourceIndex = pgs.Source.Index });
            }
        }

        return candidates;
    }

    // Runs the profile on the file with the SRT added. Null when the profile
    // would not keep it: adding it would only have the next run take it out.
    private static Candidate? PlaceByProfile(MediaFile file, ConversionPlan target, Profile profile,
        TrackSnapshot pgs, string language)
    {
        var srt = new TrackSnapshot
        {
            Index = file.Snapshot.Tracks.Max(t => t.Index) + 1,
            Type = MediaTrackType.Subtitles,
            Codec = nameof(SubtitleCodec.Srt),
            LanguageCode = language,
            LanguageName = pgs.LanguageName,
            Name = "",
            DurationMs = pgs.DurationMs
        };

        var withSrt = new MediaFile
        {
            Id = file.Id,
            ProfileId = file.ProfileId,
            Title = file.Title,
            OriginalLanguage = file.OriginalLanguage,
            Path = file.Path,
            Size = file.Size,
            OriginalTrackNames = file.OriginalTrackNames,
            Snapshot = new MediaSnapshot
            {
                ContainerType = file.Snapshot.ContainerType,
                Title = file.Snapshot.Title,
                Resolution = file.Snapshot.Resolution,
                DurationMs = file.Snapshot.DurationMs,
                VideoBitDepth = file.Snapshot.VideoBitDepth,
                TrackCount = file.Snapshot.TrackCount + 1,
                HasChapters = file.Snapshot.HasChapters,
                HasAttachments = file.Snapshot.HasAttachments,
                Chapters = file.Snapshot.Chapters,
                GlobalTags = file.Snapshot.GlobalTags,
                Tracks = [..file.Snapshot.Tracks, srt]
            }
        };

        var plan = withSrt.BuildTargetFromProfile(profile).Tracks;
        var position = plan.FindIndex(t => t.Index == srt.Index);
        if (position < 0)
        {
            return null;
        }

        var name = plan[position].Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = string.IsNullOrWhiteSpace(pgs.LanguageName) ? language : pgs.LanguageName;
        }

        var kept = target.Tracks.Select(t => t.Index).ToHashSet();
        var before = plan.Skip(position + 1).Select(t => t.Index).FirstOrDefault(kept.Contains, -1);
        return new Candidate(pgs.Index, language, name, before);
    }

    // Extracts the PGS track, reads every caption with Tesseract and writes the
    // SRT with the SDH text taken out. Null, with the reason logged, when there
    // is nothing worth adding.
    public static async Task<string?> MakeAsync(string mkv, Candidate candidate, string workFolder,
        Action<string> log, Action<double>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(workFolder);
        var sup = Path.Combine(workFolder, $"track{candidate.SourceIndex}.sup");
        var extract = await ToolRunner.RunAsync("mkvextract", [mkv, "tracks", $"{candidate.SourceIndex}:{sup}"],
            TimeSpan.FromHours(2), null, token);
        if (!extract.Success || !File.Exists(sup))
        {
            log($"Warning: no SRT for track {candidate.SourceIndex}: mkvextract failed ({extract.LastLine}).");
            return null;
        }

        var captions = PgsReader.Read(sup);
        if (captions.Count == 0)
        {
            log($"Warning: no SRT for track {candidate.SourceIndex}: it has no captions.");
            return null;
        }

        var language = SubtitleOcr.TesseractLanguage(candidate.LanguageCode);
        log($"Reading {captions.Count} captions of track {candidate.SourceIndex} with Tesseract ({language}).");
        var ocr = await SubtitleOcr.RecogniseAsync(captions, "tesseract", language,
            Path.Combine(workFolder, "ocr"), progress, token);

        var cues = Cues(ocr);
        if (cues.Count == 0)
        {
            log($"Warning: no SRT for track {candidate.SourceIndex}: Tesseract found no text.");
            return null;
        }

        var srt = Path.Combine(workFolder, $"track{candidate.SourceIndex}.srt");
        await File.WriteAllTextAsync(srt, Srt.Write(cues), token);
        log($"{SubtitleOcr.Describe(ocr)}; {cues.Count} cues in the SRT \"{candidate.Name}\".");
        return srt;
    }

    // The cleaned text of each caption; a caption that was only SDH ([door slams]) goes.
    public static List<SrtCue> Cues(IEnumerable<OcrCaption> ocr) =>
        ocr.Where(c => !string.IsNullOrWhiteSpace(c.Clean))
            .Select(c => new SrtCue(c.Start, c.End, c.Clean))
            .ToList();

    private static string Base(string? language) =>
        (language ?? "").Trim().ToLowerInvariant().Split('-')[0];
}
