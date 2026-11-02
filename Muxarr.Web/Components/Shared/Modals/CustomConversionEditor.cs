using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Core.MkvToolNix;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Components.Shared.Modals;

/// <summary>
/// What the custom conversion modal is actually doing: an editable copy of the
/// file's tracks, which of them to keep, and the order they go out in. It lives
/// outside the component so the rules (profile overlay, reordering, what reaches
/// the plan) can be exercised without a renderer.
/// </summary>
public class CustomConversionEditor
{
    private readonly MediaFile _file;
    private readonly List<TrackSnapshot> _tracks;
    private readonly HashSet<int> _selected = [];

    // Names follow whichever profile last wrote them: the one applied here, or
    // the file's own. Deliberately not the profile the page has selected, which
    // is a preview and may never touch this file.
    private Profile? _appliedProfile;

    public CustomConversionEditor(MediaFile file)
    {
        _file = file;
        _tracks = file.Snapshot.Tracks.Select(t => t.ToSnapshot()).ToList();
        _tracks.AttachOriginalNames(file.OriginalTrackNames, file.Snapshot.Tracks);
        FileTitle = file.Snapshot.Title ?? "";
        Chapters = file.Snapshot.Chapters
            .Select(c => new ChapterEdit(c.StartMs, c.Title ?? "", c.Title ?? "")
            {
                Original = file.OriginalTrackNames?.OriginalChapterTitle(c.StartMs)
            })
            .ToList();

        // Start from raw file state: every track kept, no mutations applied.
        // ApplyProfile is how the user opts into a profile's rules.
        foreach (var track in _tracks)
        {
            _selected.Add(track.Index);
        }

        var present = _tracks
            .Where(t => t.Type != MediaTrackType.Video)
            .Select(t => t.LanguageName)
            .ToHashSet();

        Languages = IsoLanguage.Languages
            .OrderByDescending(l => present.Contains(l.Name))
            .ThenBy(l => l.Name)
            .ToList();
    }

    public IReadOnlyList<TrackSnapshot> Tracks => _tracks;

    /// <summary>
    /// The container title to write. Starts as the file's own; empty clears it.
    /// Only a change from the current title reaches the plan.
    /// </summary>
    public string FileTitle { get; set; } = "";

    public string CurrentFileTitle => _file.Snapshot.Title ?? "";

    /// <summary>One chapter's title as the file has it and as the user wants it.</summary>
    public sealed class ChapterEdit(long startMs, string current, string title)
    {
        public long StartMs { get; } = startMs;
        public string Current { get; } = current;
        public string Title { get; set; } = title;
        public bool IsChanged => !string.IsNullOrWhiteSpace(Title) && Title.Trim() != Current;

        /// <summary>The title from the track name backup, when the file arrived with one.</summary>
        public string? Original { get; init; }
    }

    /// <summary>True when the backup holds a chapter title that differs from the file's.</summary>
    public bool HasOriginalChapterTitles =>
        Chapters.Any(c => !string.IsNullOrEmpty(c.Original) && c.Original != c.Current);

    /// <summary>Puts back the titles the file arrived with, where the backup has one.</summary>
    public void UseOriginalChapterTitles()
    {
        foreach (var chapter in Chapters.Where(c => !string.IsNullOrEmpty(c.Original)))
        {
            chapter.Title = chapter.Original!;
        }
    }

    /// <summary>The file's chapters with editable titles. Only Matroska files can have them renamed.</summary>
    public List<ChapterEdit> Chapters { get; }

    public bool CanRenameChapters =>
        Chapters.Count > 0 && _file.Snapshot.ContainerType.ToContainerFamily() == ContainerFamily.Matroska;

    /// <summary>Titles every chapter "Chapter 1", "Chapter 2", ... with the given word.</summary>
    public void NumberChapters(string word = "Chapter")
    {
        var width = Chapters.Count >= 10 ? 2 : 1;
        for (var i = 0; i < Chapters.Count; i++)
        {
            Chapters[i].Title = $"{word} {(i + 1).ToString().PadLeft(width, '0')}";
        }
    }

    public void ResetChapters()
    {
        foreach (var chapter in Chapters)
        {
            chapter.Title = chapter.Current;
        }
    }

    public IReadOnlyList<IsoLanguage> Languages { get; }

    /// <summary>The name the track arrived with, when the backup has one that differs from the name now.</summary>
    public string? OriginalNameOf(TrackSnapshot track)
    {
        return string.IsNullOrWhiteSpace(track.OriginalName) || track.OriginalName == track.Name
            ? null
            : track.OriginalName;
    }

    /// <summary>Puts the original name back as the track's name, to edit from there.</summary>
    public void UseOriginalName(TrackSnapshot track)
    {
        if (OriginalNameOf(track) is { } original)
        {
            track.Name = original;
        }
    }

    public bool IsSelected(TrackSnapshot track)
    {
        return _selected.Contains(track.Index);
    }

    /// <summary>A conversion with no audio would produce an unplayable file.</summary>
    public bool HasAudio => _tracks.Any(t => t.Type == MediaTrackType.Audio && _selected.Contains(t.Index));

    public void ToggleTrack(TrackSnapshot track)
    {
        if (track.Type == MediaTrackType.Video)
        {
            return;
        }

        if (!_selected.Add(track.Index))
        {
            _selected.Remove(track.Index);
        }
    }

    /// <summary>
    /// Overlays a profile's target on the editable tracks: its selection, and any
    /// field it has an opinion on. Null fields mean "no opinion" and leave the
    /// track alone; "" on a name is an explicit clear.
    /// </summary>
    public void ApplyProfile(Profile profile)
    {
        var target = _file.BuildTargetFromProfile(profile);
        _appliedProfile = profile;

        var allowed = target.Tracks.Select(t => t.Index).ToHashSet();
        _selected.Clear();
        foreach (var track in _tracks.Where(t => allowed.Contains(t.Index)))
        {
            _selected.Add(track.Index);
        }

        foreach (var resolved in target.Tracks)
        {
            var editable = _tracks.FirstOrDefault(t => t.Index == resolved.Index);
            if (editable == null)
            {
                continue;
            }

            if (resolved.Name != null)
            {
                editable.Name = string.IsNullOrEmpty(resolved.Name) ? null : resolved.Name;
            }

            if (resolved.LanguageCode != null)
            {
                var iso = IsoLanguage.Find(resolved.LanguageCode);
                editable.LanguageCode = resolved.LanguageCode;
                editable.LanguageName = iso != IsoLanguage.Unknown ? iso.Name : editable.LanguageName;
            }

            if (resolved.IsDefault is { } isDefault)
            {
                editable.IsDefault = isDefault;
            }

            if (resolved.IsForced is { } isForced)
            {
                editable.IsForced = isForced;
            }

            if (resolved.IsCommentary is { } isComm)
            {
                editable.IsCommentary = isComm;
            }

            if (resolved.IsHearingImpaired is { } isHi)
            {
                editable.IsHearingImpaired = isHi;
            }

            if (resolved.IsVisualImpaired is { } isAd)
            {
                editable.IsVisualImpaired = isAd;
            }

            // Matroska strips IsDub during resolution and encodes it in the title;
            // read it back from whichever title applies so the UI stays consistent.
            editable.IsDub = resolved.IsDub ?? TrackNameFlags.ContainsDub(editable.Name);

            // Same for a language a container cannot tag: the resolver downgraded
            // the code and put the variant in the name, so read it back the way
            // the next scan will, or the dropdown contradicts the name beside it.
            editable.RefineLanguageFromName();
        }
    }

    /// <summary>
    /// Every metadata edit goes through here, so the track name follows whatever
    /// changed. The user sees the title that will actually be written, and the
    /// profile has nothing left to rename behind the conversion.
    /// </summary>
    public void Edit(TrackSnapshot track, Action<TrackSnapshot> edit)
    {
        track.ApplyTrackEdit(_file, _appliedProfile ?? _file.Profile, _tracks, edit);
    }

    public void SetLanguage(TrackSnapshot track, string? languageName)
    {
        if (string.IsNullOrEmpty(languageName))
        {
            return;
        }

        var iso = IsoLanguage.Find(languageName);
        Edit(track, t =>
        {
            t.LanguageName = iso.Name;
            t.LanguageCode = iso.WriteCode ?? t.LanguageCode;
        });
    }

    public bool CanMoveUp(TrackSnapshot track)
    {
        var index = _tracks.IndexOf(track);
        return index > 0 && _tracks[index - 1].Type != MediaTrackType.Video;
    }

    public bool CanMoveDown(TrackSnapshot track)
    {
        var index = _tracks.IndexOf(track);
        return index >= 0 && index < _tracks.Count - 1;
    }

    public void Move(TrackSnapshot track, int direction)
    {
        var index = _tracks.IndexOf(track);
        var newIndex = index + direction;
        if (index < 0 || newIndex < 0 || newIndex >= _tracks.Count)
        {
            return;
        }

        _tracks.RemoveAt(index);
        _tracks.Insert(newIndex, track);
    }

    /// <summary>
    /// The plan for what is on screen. Video always survives; everything else in
    /// display order, which is what the converter writes and what the planner
    /// compares against the source to decide a remux is needed.
    /// </summary>
    public ConversionPlan Build()
    {
        var video = _tracks.Where(t => t.Type == MediaTrackType.Video);
        var kept = _tracks.Where(t => t.Type != MediaTrackType.Video && _selected.Contains(t.Index));
        var plan = _file.BuildTargetFromCustom(video.Concat(kept).ToList());

        var title = FileTitle.Trim();
        if (title != CurrentFileTitle)
        {
            plan.Title = title;
        }

        if (CanRenameChapters && Chapters.Any(c => c.IsChanged))
        {
            plan.Chapters = Chapters
                .Select(c => new ChapterInfo { StartMs = c.StartMs, Title = c.IsChanged ? c.Title.Trim() : null })
                .ToList();
        }

        return plan;
    }
}
