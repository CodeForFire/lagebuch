# Roadmap

Where Lagebuch is going and what 1.0 means. Each milestone below exists as a
[GitHub Milestone](https://github.com/CodeForFire/lagebuch/milestones), so the
issue list is always the current truth — this file explains the reasoning behind
the grouping.

There are no release dates. Lagebuch is built by volunteer firefighters in their
own time, and a date we would have to miss is worth less than an honest order.
The one hard date below is not ours to move.

## Where we are

Version 0.6.0, with a release every one to two weeks. The app is in real use,
but the `.fwincident` file format can still change between versions. That caveat
is what 1.0 removes.

## v0.6 — Field fixes and verifiable releases — **released**

Shipped 2026-09-22. Since 0.5.0 the tree gained the Einsatzdaten dialog —
Stichwort, Einsatznummer and Adresse in one place, which the PDF's "Adresse"
line had been waiting for — `SHA256SUMS.txt` and a Sigstore-backed attestation
on every artefact, the German Datenschutz- und Sicherheitsseite, fictional
sample data to try the app out with, the Sicherheitstrupp, Trupp-Typ rules that
live in the Stammdaten instead of in a Trupp's name, and a long row of fixes to
the PDF export, the Übersicht and the Stammdaten.

The first Übung's findings landed in it too: the CO-Messreihe per Wohnung, the
places where the app lost input or a click without saying so, and the Android
APK that declared no `INTERNET` permission, which had made joining an incident
impossible in a released build (#381).

See [CHANGELOG.md](CHANGELOG.md#060---2026-09-22) for the full list.

## v0.7 — Stability, performance and field polish

The September 2026 architecture review found a set of hot spots that are
invisible on a small incident and painful on a large one: quadratic rescans,
SQLite probes on the UI thread at startup, and list rebuilds that throw away the
user's selection and scroll position mid-Einsatz.

Alongside them the rest of the Übung feedback that changes no file format and
adds no module: marking which fields are mandatory, offering to reset the
ILS-Erinnerung after a Rückmeldung, an attachment opened from the Files tab that
cleans its temp copy up again, and a report that says when a Trupp was last asked
for its pressure.

Nothing new gets added here. The tab you are looking at should stay where you
left it.

Issues: #241, #246, #290, #291, #292, #293, #294, #383, #414, #415, #425, #426

## v0.8 — Security and trust

Two halves. Harden the parts that face the network and the file system: the sync
host's pairing, the Android attachment path, the attachment size cap that today
is enforced on upload only, and the fire-and-forget broadcasts that can swallow a
rejected command without telling anyone. The photos a joined device pulls from
the host belong here too — they are now capped at 500 MB, but nothing deletes
them when the Einsatz ends, and nothing strips the GPS coordinates out of them.

Then make failures visible. File logging and global unhandled-exception handlers
mean that when something goes wrong on an ELW laptop at two in the morning, there
is something to send us afterwards.

Issues: #288, #289, #295, #296, #300, #382, #384, #406, #407

The OpenSSF Scorecard badge in the README reports two checks as weak, and both
readings are correct. Branch-Protection and Code-Review score low because `main`
requires zero approving reviews. With one maintainer there is nobody to approve,
and requiring an approval would simply stop the project. It stays as it is until
a second maintainer exists, at which point the requirement goes to one. Force
pushes, branch deletion and merging without green CI are already blocked, for
administrators included.

## v0.9 — Install without warnings

Today every install path asks the user to click past a warning: SmartScreen on
Windows, quarantine on macOS, unknown sources on Android. For a public-sector
organisation that is a hard stop, and it undercuts the security work above.

The plan, in order of what is actually achievable:

- **winget and the Microsoft Store** on Windows. A silent MSI install through
  winget does not raise the SmartScreen dialog, which makes it the recommended
  Windows path even before a certificate exists.
- **SHA-256 checksums and Sigstore-backed build attestations** on every release.
  Not recognised by the operating system, but verifiable, and they are the
  artefacts a Datenschutzbeauftragter can actually check. These ship as of 0.6.
- **Google Play** on Android. A bundle signed by Google installs without the
  unknown-sources prompt and updates itself — for a public-sector organisation
  the only Android path with no warning to click past, and a far shorter road
  than a Windows certificate. The build already produces the bundle; what is
  missing is the developer account and the listing. The APK on the GitHub
  release stays alongside it.
- **A published Android signing-key fingerprint** for that APK, and distribution
  through Obtainium so updates do not mean re-downloading it by hand — the path
  for anyone who does not want Play. This needs the release APK to be signed with
  a stable key of its own first; today it carries the CI runner's throwaway debug
  key, which is why a sideloaded install cannot be upgraded in place at all.

On code signing, plainly: the SignPath Foundation reviewed this project and
declined. OSSign requires six months of activity on the account, the
organisation and the project; CodeForFire was founded in August 2026, so
**February 2027** is the earliest a free Windows certificate is possible, and we
plan to apply then. Apple's Developer ID is a paid membership and independent of
both.

Issues: #209, #308, #208

## v0.10 — i18n and architecture

Lagebuch is German software, and that is a strength. It should not also be an
accident of implementation. Today the German wording is literal text inside
views and domain code, so there is no translation to contribute even if someone
wanted to. Deciding the mechanism comes first; extracting strings before that
means doing it twice.

The portability review belongs to the same decision. "ILS" is the Bavarian name
for the dispatch centre and is hardcoded — including in the ETB entry written
into the incident file, where it stays for the life of that file. The CO ppm
bands are pegged to the German AGW, the floor labels to EG/OG/UG, the report to
A4 with column widths tuned to German headings, and the Atemschutz retreat rule
to a flat 50 bar with "bar" as a literal rather than a unit. Each of them is the
same question — configuration, translation, or a stable key rendered at display
time — and answering it once is the whole point of doing #350 first.

Alongside it, the layering work: `MasterDataSet` moving out of the persistence
assembly, the session abstractions finding their right home, and German ETB
wording moving out of the domain types.

Issues: #297, #298, #299, #302, #304, #350, #400, #402, #403, #404

## v0.11 — New incident modules and Stammdaten

The feature wishes that came back from the field, as opposed to the corrections:
a Dekon-Platz with its Platzführung, up to six freely named gases instead of CO
alone, umluftabhängige Filtergeräte, Häuser and Einsatzpläne pre-defined in the
Stammdaten rather than retyped at every Einsatz, an address that can hold a house
number, a PLZ and an Ort, images pasted straight from the clipboard, and links
opened without leaving the app.

They sit here rather than earlier for one reason: every one of them adds or
changes something the `.fwincident` file stores. That makes them the last work
that can be done cheaply, because after the freeze below every one of them costs
a forward migration that has to keep working for good.

Issues: #401, #410, #420, #421, #423, #427, #428, #429

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
- **The UX review backlog** — accumulated findings from using the app on real
  incidents. Issues: #262, #282.

## Influencing this

This roadmap is a plan, not a contract, and feedback from people who work in an
ELW carries more weight here than anything else.

- Tell us what is missing in [Discussions](https://github.com/CodeForFire/lagebuch/discussions).
- Pick something up: the [good first issues](https://github.com/CodeForFire/lagebuch/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22)
  are real work with a written-out starting point. See
  [CONTRIBUTING.md](CONTRIBUTING.md).
