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

## winget (Windows Package Manager)

`winget install CodeForFire.Lagebuch` matters beyond convenience: a silent MSI
install through winget does not raise the SmartScreen dialog, which makes it
the only warning-free Windows path while the packages are unsigned.

Submitting a version is a **manual step at release time**, and deliberately so.
[Komac](https://github.com/russellbanks/Komac), the tool the winget community
uses for this, needs a *classic* GitHub token with the `public_repo` scope —
fine-grained tokens can create the commit but fail to open the pull request.
Running it from CI would mean keeping such a token, which can write to every
public repository its owner has, in this repository's secrets. Running it from
a laptop reuses the login `gh` already holds:

```bash
komac token add --token="$(gh auth token)"   # once
komac update CodeForFire.Lagebuch \
  --version 0.6.0 \
  --urls https://github.com/CodeForFire/lagebuch/releases/download/v0.6.0/lagebuch-0.6.0-x64.msi \
  --submit
```

Komac downloads the MSI, computes the SHA-256, reads the ProductCode out of it,
carries the previous version's metadata forward and opens the pull request from
your winget-pkgs fork. Cross-check the hash it reports against the release's own
`SHA256SUMS.txt` — they must agree.

**Full releases only.** winget has no notion of a pre-release, so submitting a
beta would make it the version every user is offered.

The upstream repository requires a moderator to approve community pull
requests, so the new version appears in the catalogue hours to weeks after the
release, not minutes.

### Doing it by hand

Without Komac the manifests are four small YAML files under
`manifests/c/CodeForFire/Lagebuch/<version>/` in a branch of the fork. Take the
previous version as the template and change the version, the installer URL, the
SHA-256 and the MSI's ProductCode:

```bash
gh release download v<version> -p 'lagebuch-*-x64.msi'
sha256sum lagebuch-<version>-x64.msi          # InstallerSha256, uppercase
msiinfo export lagebuch-<version>-x64.msi Property | grep -E 'ProductCode|UpgradeCode'
```

`msiinfo` comes from `msitools` and reads the MSI on Linux; the UpgradeCode
must stay `{A03EAA1B-3D77-4FF0-A60A-5ABE27C27B18}`, the one fixed in
[`Lagebuch.wxs`](../packaging/windows/Lagebuch.wxs). The manifests can be
validated without Windows against the published JSON schemas
(`microsoft/winget-cli`, `schemas/JSON/manifests/v1.12.0/`); upstream's own
pipeline is the authoritative check.

## macOS

The macOS `.dmg` is built on demand rather than on every tag: run the
**Release** workflow manually (*Actions → Release → Run workflow*) with the
release version, and the `.dmg` is attached to that release.

## Local packaging

```bash
make package-linux VERSION=0.6.0   # builds a local .deb into dist/
make apk                           # builds an installable APK in Docker
```
