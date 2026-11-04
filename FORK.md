# About this fork

This is a personal fork of [KirovAir/muxarr](https://github.com/KirovAir/muxarr) (GPLv3).
It adds a few things on top of upstream, one milestone at a time:

| Milestone | What it adds |
| --- | --- |
| v0.1 | The fork builds and publishes its own image to `ghcr.io/yannb2/muxarr` |
| v0.2 | Atmos and DTS:X detection from ffprobe, custom codec names for track name templates |
| v0.3 | "Custom rules" track order (forced first, language priority, codec order) and a "Forced only" subtitle default |
| v0.4 | HDR and Dolby Vision detection (profile, FEL/MEL via dovi_tool) and a video track name template |

### SDR only at 2160p

`{range}` in the video track name writes "SDR" only for 4K video (2160p and up). Below 4K every file is
SDR, so nothing is written, the same rule as tag_release, Radarr's SDR custom format and Ripparr.

### Track descriptions

`{description}` in a track name template is the text that isn't from the template: the current name
minus the part one of the profile's templates wrote (default or flag override), so a rerun never
nests a name inside itself. The custom conversion has a Description box under each audio and
subtitle name, the same as Ripparr's disc page:

- What you type fills `{description}`, and the name below updates to what will be written.
- A template without `{description}` puts the text in brackets at the end, and keeps it on later runs.
- Text starting with `=` is the whole name, as typed.
- A commentary with nothing typed gets "Commentary", unless its template already says it
  (`{language} (Commentary) - {description} - {codec}`).

### Track checks

A file's page has a Track checks card, taken from Ripparr:

- **Listen** plays 20 seconds of an audio track (from a third of the way in), as stereo AAC.
- **Preview** shows up to 24 captions of a subtitle track: pictures for PGS, text for SRT and ASS.
- **Check SDH and commentary** reads up to 120 captions (Tesseract OCR for PGS, in the track's
  language) and says whether the track looks like SDH (sound descriptions, speaker labels, lyrics)
  or a commentary. It only reports: the flags are still set in Custom conversion.

Tick **Compare** on two or more PGS tracks and **Side by side** lines their captions up by time (from the
same slices), as Ripparr's compare page does: handy to tell the forced track from the full one, or SDH from
plain subtitles.

When the check disagrees with the track's flags, **Use this in Custom conversion** opens Custom
conversion with those flags already set. A lossy stereo track after a multichannel one in the same
language gets a **Commentary or AD?** hint (the file can't say which), with Commentary and AD buttons
that do the same.

Only 12 slices of 40 seconds, spread over the film past the titles and before the credits, are read
with ffmpeg, so a 60 GB remux takes seconds, not a full read. VobSub and DVB picture subtitles are not
read. The image ships Tesseract with English, French, German, Spanish and Italian.

### Release label and progress

- Custom conversion has a **Release label** box (Criterion, Arrow...) that writes the `DISTRIBUTED_BY`
  global tag, as Ripparr does; Jellyfin shows it as "Release: ..." through edition_tags.py.
- A running conversion shows two bars, as in Ripparr: the current step (remux, check, swap, rescan...)
  and the whole conversion.
- The sidebar's Docs link opens this file, and the version links to this fork.

### Checking the new file

Before the new file replaces the original, the fork also checks (from Ripparr's verify step):

- chapters the plan keeps are still there (a failure: the original stays);
- in MKV files, each track's name, language and default/forced/SDH/AD/commentary flags as planned (a
  warning in the conversion log: the file plays, a player only shows or picks something else).

### SRT without SDH

The profile's Subtitles section has **Add an SRT without SDH when a language only has SDH subtitles**
(off by default, MKV only), as Ripparr has. When every full subtitle track (not forced, not commentary)
the profile keeps in a language is SDH and one of them is PGS, the conversion:

1. extracts that PGS track with mkvextract and reads every caption with Tesseract;
2. takes the SDH text out ([door slams], JOHN:, ♪) and drops captions that were nothing else;
3. muxes the SRT in with the remux, named and placed by running the profile on the file as if the SRT
   were already there, so the next scan leaves it alone. If the profile would drop an SRT, none is made.

A track that gives no SRT (Tesseract missing, no text) is logged and the file is converted as if the
setting were off. Custom conversions never add one. Reading a film takes a few minutes; files already
in the library get it the next time they are queued.

## Shared code: arrkit

The subtitle code behind Track checks (PGS reader, OCR, SDH cleaner, classifier, track sampler) is
shared with Ripparr in [arrkit](https://github.com/yannb2/arrkit), a git submodule in `arrkit/`.
Clone with `--recurse-submodules`, or run `git submodule update --init` in an existing clone:

```sh
git clone --recurse-submodules https://github.com/yannb2/muxarr
git -C arrkit pull origin main && git add arrkit   # newer arrkit, then commit
```

Its tests live in arrkit, not here.

## Image

Every push to `master` builds `ghcr.io/yannb2/muxarr:dev` (amd64 and arm64).
A published GitHub release also tags `latest` and the version number.
Docker Hub is skipped unless the `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` secrets are set.

```yaml
services:
  muxarr:
    image: ghcr.io/yannb2/muxarr:dev
```

## Tools and updates

The image pins FFmpeg (BtbN static build), MKVToolNix and dovi_tool with the `ARG` lines in
`Muxarr.Web/Dockerfile`; the `env` block of `build-and-deploy.yml` repeats them for the tests and
the image build args (a test checks they match). The **Tools** page (sidebar) shows what each
tool reports, the pin and the newest upstream version, looked up once a day
(`Muxarr__Updates__Enabled=false` turns that off); Muxarr never installs anything.
Every Monday the "Tool updates" workflow opens a pull request bumping both files when something
newer is out. It needs Settings > Actions > General > "Allow GitHub Actions to create and approve
pull requests".

## Keeping up with upstream

```sh
git remote add upstream https://github.com/KirovAir/muxarr.git   # once
git fetch upstream
git switch master
git merge upstream/master
git push origin master
```

Merging (not rebasing) keeps `master` free of force pushes, so clones and the Gitea mirror never diverge.
Each fork feature stays in its own files and commits where it can, so merges stay small.
New EF Core migrations go after the latest upstream one; if upstream adds a migration
later, regenerate ours on top of it instead of interleaving them.
