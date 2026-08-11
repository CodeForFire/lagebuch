# Lagebuch

Desktop application for fire-brigade incident documentation (Einsatzdokumentation).
Replaces a legacy macro-enabled Excel template with a robust, offline, cross-platform
.NET 8 + Avalonia application.

## Status

Under construction. See plans under `docs/` (note: design/plan artifacts are not committed).

## Install

Pushing a version tag publishes a release under [Releases](../../releases) with an installer per
platform:

```bash
git tag v0.2.0 && git push origin v0.2.0
```

| Platform | File | Built |
|----------|------|-------|
| Windows | `lagebuch-<version>-x64.msi` | on every tag |
| Linux (Debian/Ubuntu) | `lagebuch_<version>_amd64.deb` | on every tag |
| Android | `lagebuch-<version>.apk` | on every tag |
| macOS (Apple Silicon) | `lagebuch-<version>-macos-arm64.dmg` | on request |

macOS runners bill Actions minutes at 10× on a private repo, so the `.dmg` is built on demand rather
than on every tag: run the **Release** workflow manually (Actions → Release → *Run workflow*) with
the release version, and the `.dmg` is attached to that release.

The packages are **not code-signed**, so the OS warns on first launch:

- **Windows** — run the `.msi`; if SmartScreen appears, *More info → Run anyway*.
- **macOS** — open the `.dmg`, drag Lagebuch to Applications, then **right-click the app → Open**
  once (or `xattr -dr com.apple.quarantine /Applications/Lagebuch.app`).
- **Linux** — `sudo dpkg -i lagebuch_*.deb` (or `sudo apt install ./lagebuch_*.deb`).
- **Android** — open the `.apk` from your file manager/browser; enable *install from unknown
  sources* for that app once when prompted. The package is unsigned, same as the desktop builds.

All builds are self-contained; no .NET runtime needs to be installed separately.

## Build & Test

Building the Android head requires the .NET Android workload (one-time, per machine):

```bash
dotnet workload install android
```

```bash
dotnet build
dotnet test
```

## Run

```bash
dotnet run --project src/Feuerwehr.App/Feuerwehr.App.csproj
```

## Master data

Dropdown contents (roles, radio call signs, streets, personnel, ...) are treated
as PII and are **never compiled into the application**. A fresh install starts
with **empty** master data; you populate it in the in-app **Stammdaten** editor
by importing a JSON file, and can write your own data back out again.

### Where it is stored

On first start an empty `masterdata.db` is created. It is the live database the
app reads and writes from then on, and where the Stammdaten editor saves:

| Platform | Path |
|---|---|
| Windows | `%AppData%\Lagebuch\masterdata.db` |
| Linux   | `~/.config/Lagebuch/masterdata.db` |
| macOS   | `~/.config/Lagebuch/masterdata.db` |

On macOS the app uses `~/.config`, **not** `~/Library/Application Support` —
that is simply where .NET's `ApplicationData` folder resolves on Unix. To start
over, delete `masterdata.db`; the app recreates it empty on the next launch.

### Import and export

Open **Stammdaten** and use the header buttons:

- **IMPORTIEREN** — offered only while the data is still empty (a first-run
  bootstrap, not a merge). Pick a JSON file; its contents load into the editor as
  unsaved changes for review, and reach `masterdata.db` only when you press
  **SPEICHERN** (or **VERWERFEN** to discard).
- **EXPORTIEREN** — writes the current master data (including unsaved edits) to a
  JSON file you can back up or hand to another install.

The file is one JSON object; every top-level key is optional, so a file may hold
the whole set, only the roster, or anything in between. See
[`docs/master-data.example.json`](docs/master-data.example.json) for the full
schema. The keys are the string lists `roles`, `status`, `unitStatus`,
`equipment`, `districts`, `radioCallSigns`, `brigades`, `truppTypes`,
`einsatzarten` and `checklistTemplate`; `streets` (`{ name, district }`); and `personnel`
(`{ lastName, firstName, role?, callSign?, phone? }` — `lastName` is required,
`firstName` may be `null`, and `role`/`callSign`/`phone` may each be `null` or
omitted).

### PII

Any real master-data or personnel JSON — street lists, station and call-sign
names, and above all names and mobile numbers — is personal/identifying data and
must be kept **out of the repository**. `seed-source/` and `*.masterdata.json`
are gitignored for exactly this reason; only the anonymised
`docs/master-data.example.json` is tracked. An empty roster is a fully supported
state: the name field on the Funktionen tab offers the roster as suggestions but
always accepts free text, so off-roster and mutual-aid personnel can be entered
either way.
