# Roadmap

Where Lagebuch is going and what 1.0 means. Each milestone below exists as a
[GitHub Milestone](https://github.com/CodeForFire/lagebuch/milestones), so the
issue list is always the current truth — this file explains the reasoning behind
the grouping.

There are no release dates. Lagebuch is built by volunteer firefighters in their
own time, and a date we would have to miss is worth less than an honest order.
The one hard date below is not ours to move.

## Where we are

Version 0.5.0, with a release every one to two weeks. The app is in real use,
but the `.fwincident` file format can still change between versions. That caveat
is what 1.0 removes.

## v0.6 — Stability and performance

The September 2026 architecture review found a set of hot spots that are
invisible on a small incident and painful on a large one: quadratic rescans,
SQLite probes on the UI thread at startup, and list rebuilds that throw away the
user's selection and scroll position mid-Einsatz.

Nothing new gets added here. The tab you are looking at should stay where you
left it.

Issues: #290, #291, #292, #293, #294, #241, #246

## v0.7 — Security and trust

Two halves. Harden the parts that face the network and the file system: the sync
host's pairing, the Android attachment path, and the fire-and-forget broadcasts
that today can swallow a rejected command without telling anyone.

Then make failures visible. File logging and global unhandled-exception handlers
mean that when something goes wrong on an ELW laptop at two in the morning, there
is something to send us afterwards.

Issues: #288, #289, #295, #296, #300

## v0.8 — Install without warnings

Today every install path asks the user to click past a warning: SmartScreen on
Windows, quarantine on macOS, unknown sources on Android. For a public-sector
organisation that is a hard stop, and it undercuts the security work above.

The plan, in order of what is actually achievable:

- **winget and the Microsoft Store** on Windows. A silent MSI install through
  winget does not raise the SmartScreen dialog, which makes it the recommended
  Windows path even before a certificate exists.
- **SHA-256 checksums and Sigstore-backed build attestations** on every release.
  Not recognised by the operating system, but verifiable, and they are the
  artefacts a Datenschutzbeauftragter can actually check.
- **A published Android signing-key fingerprint**, and distribution through
  Obtainium so updates do not mean re-downloading an APK by hand.

On code signing, plainly: the SignPath Foundation reviewed this project and
declined. OSSign requires six months of activity on the account, the
organisation and the project; CodeForFire was founded in August 2026, so
**February 2027** is the earliest a free Windows certificate is possible, and we
plan to apply then. Apple's Developer ID is a paid membership and independent of
both.

Issues: #209, #308, #208

## v0.9 — i18n and architecture

Lagebuch is German software, and that is a strength. It should not also be an
accident of implementation. Today the German wording is literal text inside
views and domain code, so there is no translation to contribute even if someone
wanted to. Deciding the mechanism comes first; extracting strings before that
means doing it twice.

Alongside it, the layering work: `MasterDataSet` moving out of the persistence
assembly, the session abstractions finding their right home, and German ETB
wording moving out of the domain types.

Issues: #297, #298, #299, #302, #304, #350

## v1.0 — File format freeze

1.0 is not a feature set. It is a promise about your data: a file written by any
1.x version opens in every later 1.x version.

The machinery already exists. Files are migrated forward on open, and a file
from a newer version is refused with a clear message instead of being corrupted.
What is missing is the commitment, the checked-in fixture files that prove old
incidents still open, and one deliberate schema review before the door closes.

Issue: #351

## Beyond 1.0

Listed so the direction is visible, deliberately not scheduled:

- **Wasserförderung über lange Wegstrecken** — planning and execution modes,
  using the map and elevation packs already published in
  [lagebuch-regions](https://github.com/CodeForFire/lagebuch-regions).
  Issues: #87, #150.
- **FF-Agent integration** — reading existing brigade data instead of asking for
  it twice. Issue: #77.
- **The UX review backlog** — accumulated findings from using the app on real
  incidents. Issues: #262, #282, #167.

## Influencing this

This roadmap is a plan, not a contract, and feedback from people who work in an
ELW carries more weight here than anything else.

- Tell us what is missing in [Discussions](https://github.com/CodeForFire/lagebuch/discussions).
- Pick something up: the [good first issues](https://github.com/CodeForFire/lagebuch/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22)
  are real work with a written-out starting point. See
  [CONTRIBUTING.md](CONTRIBUTING.md).
