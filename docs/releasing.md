# Releasing (maintainers)

Pushing a version tag triggers the release workflow
([`.github/workflows/release.yml`](../.github/workflows/release.yml)), which
builds and attaches one package per platform:

| Platform | File |
|----------|------|
| Windows | `lagebuch-<version>-x64.msi` |
| Linux (Debian/Ubuntu) | `lagebuch_<version>_amd64.deb` |
| Android | `lagebuch-<version>.apk` (plus a `.aab` build artefact, not a release asset) |
| macOS (Apple Silicon) | `lagebuch-<version>-macos-arm64.dmg` |

## Cutting a release

`CHANGELOG.md` is assembled from `changelog.d/`, never edited by hand, so a
release starts by folding the fragments into a version section:

```bash
pip install -r .github/requirements-changelog.txt
python scripts/changelog-prlinks.py --check   # rehearse; writes nothing
towncrier build --draft --version 0.6.0       # preview; writes nothing

python scripts/changelog-prlinks.py           # prepends each entry's PR link
towncrier build --version 0.6.0               # writes the section, consumes the fragments
```

Every bullet opens with a link to the pull request the entry arrived in. Nobody
writes that link by hand — the number does not exist while the pull request that
carries the fragment is still open — so
[`scripts/changelog-prlinks.py`](../scripts/changelog-prlinks.py) recovers it
first: after a squash-merge exactly one commit adds a given fragment, and its
subject ends in `(#NNN)`. The script rewrites each fragment in place, which is
safe because `towncrier build` consumes them in the next command, and it is
idempotent, so rehearsing with `--draft` and then building for real does not
prefix the link twice.

**If it cannot resolve a fragment it says so and exits non-zero — stop there.**
A fragment that is not committed yet, or one whose commit subject carries no
`(#NNN)`, has no link to find; commit it, or add the link by hand, rather than
cutting a release with one entry silently unlinked.

`title_format` in [`towncrier.toml`](../towncrier.toml) renders the heading as
`## [0.6.0] - <Datum>`, and the version in it has to match the tag exactly minus
the `v` — that is what the workflow looks for. Three things go with the build,
none of which towncrier does for you:

- **The summary paragraph** under the new heading, if the release deserves one.
  The earlier sections have one; towncrier writes the entries and nothing else.
  It is English like the rest of the file — the German summary a Kommandant
  reads is the GitHub release body, not this.
- **The link references at the bottom of `CHANGELOG.md`.** Repoint
  `[Unreleased]` at `compare/v<new>...HEAD` and add a `[<new>]:
  compare/v<previous>...v<new>` line above the existing ones. A missing line
  renders the version heading as plain text instead of a diff link.
- **The milestone.** Close the milestone the release corresponds to, and move
  anything still open in it to the next one before closing — an issue that
  silently loses its milestone is an issue nobody plans again. Nothing in CI
  touches milestones; [`ROADMAP.md`](../ROADMAP.md) explains the grouping and
  has to be updated in the same breath.

Open that as its own pull request. Pushing the tag once it merges is what
triggers the release:

```bash
git tag -s v0.6.0 -m "Lagebuch v0.6.0 — <Zusammenfassung>" && git push origin v0.6.0
```

The release body links to the version's section rather than inlining it, and a
final release whose version has no section is refused by the workflow's `version`
job, before any build minutes are spent — so a forgotten `towncrier build` costs
seconds rather than a release with no notes. Prereleases (`v0.6.0-beta.1`) are
exempt: they are cut from work in progress, before that version's section is
assembled.

## After the tag: check the release

The checksums and the provenance are what the README tells users to verify, and
only a tag push runs those steps — they landed after v0.5.0 was cut (#375), so
they had never run once when this list was written. Four checks, once per
release:

- [ ] `SHA256SUMS.txt` is attached and lists the `.msi`, the `.deb` and the
      `.apk` under their bare names. `sha256sum -c SHA256SUMS.txt` has to pass
      in a folder holding nothing but the downloads.
- [ ] `gh attestation verify <file> --repo CodeForFire/lagebuch` passes for
      those three. It reads the file, not the URL, so download them first.
- [ ] The macOS run attached both the `.dmg` and its `.sha256`, and
      `shasum -a 256 -c lagebuch-<version>-macos-arm64.dmg.sha256` passes.
- [ ] The release is marked pre-release if and only if the tag carries a suffix.

## Betas and release candidates

A tag with a suffix — `v0.6.0-beta.1`, `v0.6.0-rc.2` — publishes as a GitHub
**pre-release**, so the previous full release stays the "Latest" download. The
workflow derives that from the tag; nothing else needs flipping. It also spells
the version the way each packaging format requires:

| | `v0.6.0-beta.1` | why |
|---|---|---|
| File names — every artefact, `.deb` included | `0.6.0-beta.1` | |
| APK, assembly | `0.6.0-beta.1` | |
| MSI `ProductVersion` | `0.6.0` | a ProductVersion must be numeric |
| `.deb` control `Version:` | `0.6.0~beta.1` | `~` sorts *below* `0.6.0`, so `apt` treats the final release as an upgrade — a `-` would sort above it |
| Android `versionCode` | `600201` | must be a single integer, and strictly greater than every build Google Play has already seen |

The last two rows are the same version spelled two ways, and only the control
field gets the tilde. It is deliberately kept out of the file name: GitHub
rewrites a `~` in a release asset name to `.`, so the release once offered
`lagebuch_0.6.0.rc.1_amd64.deb` while `SHA256SUMS.txt` named
`lagebuch_0.6.0~rc.1_amd64.deb`, and `sha256sum -c` failed on a download that
did not exist (#448). `apt` reads the control field, never the file name, so
the sort order is unaffected. Both spellings are now derived inside
[`build-deb.sh`](../packaging/linux/build-deb.sh) from the one version it is
passed, so they cannot drift apart again.

The Android `versionCode` faces the `.deb`'s ordering problem with none of its
expressiveness: Play accepts one integer, and refuses any upload that does not
raise it — to *any* track, permanently. So it is derived in the `version` job,
never typed:

```
MAJOR*10000000 + MINOR*100000 + PATCH*1000 + offset
offset:  alpha.N -> 100+N    beta.N -> 200+N    rc.N -> 300+N    final -> 999
```

| tag | `versionCode` |
|---|---|
| `v0.6.0-beta.1` | 600201 |
| `v0.6.0-rc.1` | 600301 |
| `v0.6.0` | 600999 |
| `v0.6.1` | 601999 |
| `v0.7.0` | 700999 |
| `v1.0.0` | 10000999 |

[`scripts/android-version-code.sh`](../scripts/android-version-code.sh) is the
source of truth for this — the table is worked examples, not the rule, and
`--self-test` covers every row of it plus the project's whole release history.

A prerelease sorts below its final release, exactly as the `.deb`'s `~` does.
The ceiling — `209.99.99` — lands on `2099999999`, one below Play's limit of
`2100000000`, and the job fails the build rather than silently wrapping past it.
An unrecognised suffix fails the build too: a scheme nobody can order is worse
than a rejected tag.

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

## Google Play

Google Play has not accepted an APK for a new app since 2021, so the upload is an
**Android App Bundle**. The build produces one: `AndroidPackageFormats` is
`aab;apk` in
[`LageBuch.App.Android.csproj`](../src/LageBuch.App.Android/LageBuch.App.Android.csproj),
and the SDK then runs bundletool over that same signed bundle to extract the
universal `.apk` the GitHub release carries. The sideload APK is therefore a
render of the exact bytes Play receives, not a second, independently built
artefact — which is why there is no separate "verify the bundle" step below.

Like winget, **submitting is a manual step at release time, and deliberately
so.** Automating it would mean keeping the upload keystore and its password in
this repository's secrets; a credential that can publish to every installed
device is a larger concession than the winget token this project already
declined, and releases here are tagged deliberately, a handful of times a year.
The repository holds exactly one secret today and this is not the change that
should make it six.

### One-time setup

The upload key is generated once, on a laptop, and never committed
(`.gitignore` carries `*.p12`, `*.jks`, `*.keystore`, `*.pass` as defence in
depth — the mechanism is that it lives outside the tree):

```bash
mkdir -p ~/.config/lagebuch && chmod 700 ~/.config/lagebuch
keytool -genkeypair -v \
  -keystore ~/.config/lagebuch/lagebuch-upload.p12 -storetype PKCS12 \
  -alias lagebuch-upload -keyalg RSA -keysize 4096 -validity 10000 \
  -dname "CN=CodeForFire, O=CodeForFire, C=DE"
printf '%s' '<password>' > ~/.config/lagebuch/lagebuch-upload.pass
chmod 600 ~/.config/lagebuch/lagebuch-upload.pass
```

`-validity 10000` is about 27 years, past Play's 2033 minimum. Use one password
for store and key so a single file serves both properties. **Back the keystore
up off the laptop, encrypted.**

Under Play App Signing, Google holds the key that actually signs what users
install; this one only authorises uploads. Losing it is recoverable through Play
support, which is precisely why Play App Signing is the right choice for a
project with one maintainer — but recovery is a support round-trip, not a
convenience.

Three things can never be changed after the first upload: the package name
(`de.codeforfire.lagebuch`), the app signing key, and the account type. The
package name already matches the sideloaded APK; keep it.

### Per release

Build from the tag, not from `main`, so the bundle is the code that was released:

```bash
git checkout v0.6.1
make aab VERSION=0.6.1
```

The `versionCode` is **derived, not typed** —
[`scripts/android-version-code.sh`](../scripts/android-version-code.sh) computes
it from `VERSION`, and the release workflow calls the same script, so the two
cannot disagree. The target prints the number before it builds:

```
versionCode 601999   (0.6.1)
```

`CODE=` overrides it, and should stay unused. Play burns a `versionCode`
permanently on first sight — a number typed by hand is a mistake that cannot be
taken back, which is the whole reason the script exists.

The keystore path, password file and alias are overridable (`KEYSTORE=`,
`KEYSTORE_PASS=`, `KEY_ALIAS=`) but default to the locations above. The target
refuses to run without a keystore and a password file.

Then, in the Play Console: pick the track → *Create new release* → upload
`de.codeforfire.lagebuch-Signed.aab` → German release notes → roll out.

**Which track takes what**

*Production is full releases only*, the same rule as winget: a version that
reaches the Play Store is one that exists as a tag and a GitHub release.

A **prerelease may go to internal or closed testing**, and for this project it
has to be able to. A *personal* Play developer account cannot reach production
until a closed test has run **12 testers opted in for 14 continuous days**, and
seeding that gate with a release candidate is exactly what a release candidate
is for — `v0.6.1-rc.1` gives `601301`, which stays below `0.6.1`'s `601999`, so
the real release still installs over it as an upgrade.

And note the ratchet once more: a build uploaded to *any* track burns its
`versionCode` forever, so even a throwaway test upload uses the derived number.

### The listing itself

Everything the Console asks for lives in `docs/play/`, so the listing is
reproducible instead of being retyped from memory each time:

| | Where | Regenerate |
|---|---|---|
| Text — name, short and full description, reviewer notes | [`docs/play/listing-de-DE.md`](play/listing-de-DE.md) | by hand; `make play-listing-check` |
| Icon, 512x512, 32-bit | `docs/play/icon-512.png` | `make logo-assets` |
| Feature graphic, 1024x500, no alpha | `docs/play/feature-graphic-1024x500.png` | `make logo-assets` |
| Screenshots, 1920x1080 | `docs/play/screenshots/*.png` | `make play-screenshots` |

`make play-listing-check` counts characters against the Console's limits — it
truncates silently rather than warning — and greps the text for the wording in
*What the listing must not say* below. Run it before pasting.

Two things about these assets are deliberate and easy to undo by accident:

- **The icon is the text-free emblem, not the full badge.** The badge carries a
  wordmark, a tagline and a `github.com` URL. At the 48-192 px Play actually
  renders an icon, that is unreadable, and Play's icon guidance disallows
  promotional text and URLs. The badge is right for the feature graphic, where it
  is large enough to read, and wrong for the icon.
- **Neither image goes through `shrink()`** in `build-logo-assets.sh`. That
  function quantises to 256 colours, which turns the output into an 8-bit palette
  PNG; Play specifies a 32-bit icon. It is why `docs/logo/lagebuch-logo.png`
  cannot be uploaded as the store icon even though it is already 512x512.

**The screenshots are harness renders, not device captures.** They come from
`DemoFlowRenderTests` — the same fictional Einsatz as the README grid, the same
Avalonia views the Android app draws, rendered at 1920x1080 instead of the
README's 1920x1032 because Play insists on exactly 16:9 and rejects 1.86:1. They
are honest for the 7-inch and 10-inch tablet slots, which is the form factor an
ELW actually uses.

They are **not** a substitute for phone captures. The phone layout has a known
limitation — see the skipped `CommandBarReachabilityTests` — and a landscape
tablet render in the phone slot would promise a phone experience nobody has
checked. Capture those from a real device or an accelerated emulator before
filling the phone slot. (An emulator needs `/dev/kvm`; without it an x86_64
system image is too slow to drive.)

### The declarations, frozen

These are answered once and must be answered the same way every time; re-deciding
them per release is how a Data-safety section drifts away from what the app does.

- **Privacy policy**: <https://codeforfire.github.io/datenschutz/>, built from
  [`datenschutz-und-sicherheit.md`](datenschutz-und-sicherheit.md), so it stays
  current on its own.
- **Data safety — no data collected, no data shared.** The app has no account, no
  server, no telemetry, no crash reporting, no update check and no ads or
  analytics SDKs; the vendor receives nothing. The one thing that leaves the
  device is the optional multi-device sync, and that is a user-initiated,
  TLS-encrypted, PIN-gated transfer to a second device the same organisation
  controls, reaching no developer or third-party endpoint. *Encrypted in transit:
  yes. Data deletion: everything is app-private and removed on uninstall.*
  If Play ever queries the sync, that is the answer — it is written here so the
  next release does not improvise a different one.
- **Content rating** (IARC): Utility/Productivity; no violence, sexual content,
  language, substances, gambling or horror; does **not** let users interact or
  exchange content with strangers (the sync pairs the operator's own devices by
  typed address and PIN, with no directory and no discovery); does not share
  location. Expected outcome USK 0 / PEGI 3.
- **Target audience**: 18 and over. Do not tick a younger bracket — it pulls the
  app into Families policy and an SDK audit for no benefit.
- **Ads**: none. **Account deletion URL**: not applicable, there are no accounts.
  **News / health / financial / government app**: no to all.
- **Category**: Productivity.

### What the listing must not say

Play restricts apps that imply official or governmental status, and apps that
present themselves as emergency services. Lagebuch is a volunteer project that
documents an Einsatz; it does not alert, dispatch or replace anything.

- No coat of arms, Feuerwehr emblem or BOS insignia in the icon or graphics.
- Not *amtlich*, *offiziell*, *behördlich*, *ILS*, *Leitstelle*, *112* or
  *Notruf* in the name or descriptions, and no named Wehr or Kreisbrandinspektion.
- No safety claims. The Rückzugsalarm is a timer with an acoustic reminder —
  describe it as one, not as something that keeps anybody safe.
- Do not advertise desktop-only capabilities. The Android app cannot host an
  Einsatz for other devices and cannot export the PDF report; a listing that
  implies otherwise is a metadata violation as well as a broken promise.

Keep the disclaimer in the description: Lagebuch ist keine amtliche Anwendung und
steht in keiner Verbindung zu einer Behörde oder einer Integrierten Leitstelle.

### After the upload

- The bundle's manifest carries the expected build number:
  `java -jar bundletool.jar dump manifest --bundle <file>.aab | head -1`
  (bundletool ships with the workload, under
  `~/.dotnet/packs/Microsoft.Android.Sdk.Linux/*/tools/`).
- The native libraries are still 16 KB-aligned, which Play requires of anything
  targeting Android 15+. Today every one of them is, and the app ships only
  64-bit ABIs:
  ```bash
  unzip -q -o -d /tmp/lb lagebuch-<version>.apk 'lib/*'
  for f in /tmp/lb/lib/*/*.so; do
    readelf -lW "$f" | awk -v f="$f" '$1=="LOAD" && $NF!="0x4000" { print "NOT 16K:", f }'
  done
  ```
  If that ever prints something, the offender is a native library shipped by a
  NuGet package and the fix is a package bump — no MSBuild switch realigns
  someone else's `.so`.
- Play's pre-launch report has no crashes on the tested devices.

## macOS

The macOS `.dmg` is built on demand rather than on every tag: run the
**Release** workflow manually with the release version, and the `.dmg` plus its
`.sha256` are attached to that release.

**Start the run on the tag, not on `main`.** A manual run checks out the ref it
was started on, so starting it on `main` builds whatever has merged since the
tag and uploads that as the release's `.dmg`:

```bash
gh workflow run release.yml --ref v0.6.0 -f version=0.6.0
```

In the web UI: *Actions → Release → Run workflow*, then set **Use workflow
from** to the tag before filling in the version.

The `.dmg` is built after the tag release already exists, so it cannot join
that release's `SHA256SUMS.txt`; it carries a `.dmg.sha256` next to it instead.

## Local packaging

```bash
make package-linux VERSION=0.6.0   # builds a local .deb into dist/
make apk                           # builds an installable APK in Docker (debug key)
make aab VERSION=0.6.1             # builds the signed Play bundle (needs the upload key)
```

`make apk` is the fast loop — Debug, debug key, `make install` puts it on the
emulator. `make aab` is the release path and is described under
[Google Play](#google-play) above.
