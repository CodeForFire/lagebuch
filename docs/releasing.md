# Releasing (maintainers)

Pushing a version tag triggers the release workflow
([`.github/workflows/release.yml`](../.github/workflows/release.yml)), which
builds and attaches one package per platform:

| Platform | File |
|----------|------|
| Windows | `lagebuch-<version>-x64.msi` |
| Linux (Debian/Ubuntu) | `lagebuch_<version>_amd64.deb` |
| Android | `lagebuch-<version>.apk` |
| macOS (Apple Silicon) | `lagebuch-<version>-macos-arm64.dmg` |

```bash
git tag -s v0.6.0 -m "Lagebuch v0.6.0 — <Zusammenfassung>" && git push origin v0.6.0
```

The release notes are generated from the matching section of
[`CHANGELOG.md`](../CHANGELOG.md), so move the `[Unreleased]` entries under a
new version heading before tagging. The heading has to match the tag exactly
minus the `v` (`## [0.6.0-beta.1]` for `v0.6.0-beta.1`); otherwise the notes
fall back to a "no changelog entry found" placeholder.

## Betas and release candidates

A tag with a suffix — `v0.6.0-beta.1`, `v0.6.0-rc.2` — publishes as a GitHub
**pre-release**, so the previous full release stays the "Latest" download. The
workflow derives that from the tag; nothing else needs flipping. It also spells
the version the way each packaging format requires:

| | `v0.6.0-beta.1` | why |
|---|---|---|
| File names, APK, assembly | `0.6.0-beta.1` | |
| MSI `ProductVersion` | `0.6.0` | a ProductVersion must be numeric |
| `.deb` version | `0.6.0~beta.1` | `~` sorts *below* `0.6.0`, so `apt` treats the final release as an upgrade — a `-` would sort above it |

The MSI carrying the numeric core means a beta and the eventual final share a
ProductVersion; `AllowSameVersionUpgrades` in
[`Lagebuch.wxs`](../packaging/windows/Lagebuch.wxs) is what lets the final
replace the beta rather than install beside it.

All builds are self-contained — no .NET runtime needs to be installed
separately. The packages are **not code-signed** yet; the install notes in the
README explain the first-launch warnings on each platform.

## macOS

The macOS `.dmg` is built on demand rather than on every tag: run the
**Release** workflow manually (*Actions → Release → Run workflow*) with the
release version, and the `.dmg` is attached to that release.

## Local packaging

```bash
make package-linux VERSION=0.6.0   # builds a local .deb into dist/
make apk                           # builds an installable APK in Docker
```
