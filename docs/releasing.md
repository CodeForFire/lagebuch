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
git tag v0.6.0 && git push origin v0.6.0
```

The release notes are generated from the matching section of
[`CHANGELOG.md`](../CHANGELOG.md), so move the `[Unreleased]` entries under a
new version heading before tagging.

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
