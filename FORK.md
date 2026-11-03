# About this fork

This is a personal fork of [KirovAir/muxarr](https://github.com/KirovAir/muxarr) (GPLv3).
It adds a few things on top of upstream, one milestone at a time:

| Milestone | What it adds |
| --- | --- |
| v0.1 | The fork builds and publishes its own image to `ghcr.io/yannb2/muxarr` |
| v0.2 | Atmos and DTS:X detection from ffprobe, custom codec names for track name templates |
| v0.3 | "Custom rules" track order (forced first, language priority, codec order) and a "Forced only" subtitle default |
| v0.4 | HDR and Dolby Vision detection (profile, FEL/MEL via dovi_tool) and a video track name template |

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
