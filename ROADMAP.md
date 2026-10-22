# Roadmap

Where Lagebuch is going and what 1.0 means. Each milestone below exists as a
[GitHub Milestone](https://github.com/CodeForFire/lagebuch/milestones), so the
issue list is always the current truth — this file explains the reasoning behind
the grouping.

There are no release dates. Lagebuch is built by volunteer firefighters in their
own time, and a date we would have to miss is worth less than an honest order.
The one hard date below is not ours to move.

## Where we are

Version 0.9.1, with a release every one to two weeks. The app is in real use,
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

## v0.7 — Stability, performance and field polish — **released**

Shipped 2026-10-01. The hot spots the September 2026 architecture review found
are gone: the Kräfte, Funktionen, Aufgaben, ETB and CO-Messung lists update in
place instead of being rebuilt, so a change from anywhere in the Einsatz no
longer throws away the selection, the focus or an open editor; saves coalesce,
and the Übersicht no longer opens every recent file on the UI thread.

Sync grew up with it. Two devices now connect when their wire contracts overlap
instead of when their app versions match, a joined device heals missed updates
and survives a dropped link, the host keeps one identity per install, and a
global PIN budget closes the share after ten wrong guesses.

The rest of the Übung feedback landed too — Pflichtfeld markers, restarting the
Rückmelde timer from an ETB entry, the Lagebuchführer handover, the two-step
join, the Kontakte module, the BETEILIGTE tab, a phone layout and closing an
Einsatz straight into the PDF protocol.

See [CHANGELOG.md](CHANGELOG.md#070---2026-10-01) for the full list.

Issues: #241, #288, #290, #291, #292, #293, #294, #295, #383, #414, #415, #425,
#426, #443, #458, #459, #460, #463, #464, #465, #467, #468, #469, #470, #481,
#509, #511, #518, #529

0.7.1 followed on 2026-10-02 as a patch so the 0.7 line could go to Google Play:
Play automatic protection refuses a bundle below Android 7.0 (API 24), which is
now the app's floor (#550).

## v0.8 — Keyboard control — **released**

Shipped 2026-10-06. A Lagebuchführer at the ELW laptop types far more than they
click, and the field said so directly (#466). The app now keeps one keyboard
contract (#545) everywhere: dialogs and inline panels take focus, keep Tab
inside, close on Esc and give focus back; Enter submits every entry form and
returns to its first field; the suggestion fields share one keyboard model; a
Druckkontrolle is recorded without the mouse; the lists answer to row keys;
global shortcuts open modules and jump to the most urgent warning, with an
overview on F1; and keyboard focus is visible on every control. All of it is
pinned by headless tests, including a keyboard-only Übung walkthrough with a
keystroke budget for the everyday flows.

#246, editing an Aufgabe, shipped in the same release. Because a joined device
can send that edit, the sync protocol rose to 7, so every device in a shared
Einsatz needs 0.8.0.

See [CHANGELOG.md](CHANGELOG.md#080---2026-10-06) for the full list.

Issues: #545, #537, #541, #538, #539, #540, #466, #542, #543, #544, #246

0.8.1 followed on 2026-10-07 as a patch for Windows: the MSI had been built as a
32-bit package and installed into `Program Files (x86)`; it is now a real x64
package (#574).

## v0.9 — Security and trust — **released**

Shipped 2026-10-20. The parts that face the network and the file system are
hardened: Android opens and shares attachments only through the same gate as the
desktop and checks a picked file's size while copying it rather than afterwards,
the 25 MB cap holds on both ends of the sync, and a joined device deletes the
attachments it pulled from the host when it leaves. (The sync host's pairing and
the broadcasts that could swallow a rejected command were planned here and
shipped early, in 0.7.) Stripping the GPS coordinates out of photos (#384) did
not make it and stays open.

Failures are visible now. A crash leaves a small report — version, OS and stack
traces, never an exception's message — that the Startseite points to on the next start, a
hung audio player no longer leaks a thread, and sharing, export and the Dateien
tab say in German what went wrong. The latest Übung's findings landed alongside,
from Atemschutz double-click guards to Stammdaten and module lists that no longer
freeze on a long roster. The sync protocol stays at 7.

Two further ideas were considered here and declined: sharing on one chosen
network instead of every interface (#599), and sealing a closed Einsatz with a
hash printed in the Bericht (#598). Both add steps at an Einsatz against an
unlikely attacker on the ELW's own network, and the exported PDF already is the
record. The first connection to a host stays Trust-on-First-Use on purpose;
SECURITY.md explains why a mandatory Kennung comparison would cost more than it
protects.

See [CHANGELOG.md](CHANGELOG.md#090---2026-10-20) for the full list.

Issues: #289, #296, #300, #382, #406, #407

0.9.1 followed on 2026-10-22 as a patch with the first field reports on 0.9.0,
all of them about the screen and the keyboard: Tab order, where the search and
the actions sit, the Funktionen column order, phone number formatting, muting
the alarm tones and less manual sorting in the Stammdaten. The reports that ask
for something new to be stored went to v0.10 instead.

The OpenSSF Scorecard badge in the README reports two checks as weak, and both
readings are correct. Branch-Protection and Code-Review score low because `main`
requires zero approving reviews. With one maintainer there is nobody to approve,
and requiring an approval would simply stop the project. It stays as it is until
a second maintainer exists, at which point the requirement goes to one. Force
pushes, branch deletion and merging without green CI are already blocked, for
administrators included.

## v0.10 — New incident modules and Stammdaten

The feature wishes that came back from the field, as opposed to the corrections:
a Dekon-Platz with its Platzführung, up to six freely named gases instead of CO
alone, umluftabhängige Filtergeräte, Häuser and Einsatzpläne pre-defined in the
Stammdaten rather than retyped at every Einsatz, an address that can hold a house
number, a PLZ and an Ort, images pasted straight from the clipboard, and links
opened without leaving the app.

The first field reports on 0.9.0 added to the list: where a Betroffener is now,
the CO-Messung linking a Wohnung to the Betroffene found in it, who reports to
whom among the Funktionen, and Stichwörter from the Stammdaten offered as
suggestions in the Einsatzdaten.

This used to be v0.11, behind i18n. It moved ahead after 0.9.0 for two reasons.
These are what the people using the app today are asking for, and an
architecture release would have made them wait for work they cannot see. And
every one of them adds or changes something the `.fwincident` file stores, so
they are cheapest before the freeze below, after which each costs a forward
migration that has to keep working for good. The German wording they add is
written inline like all the rest and extracted with it in v0.11.

Issues: #384, #401, #410, #420, #421, #423, #427, #428, #429, #461, #592, #701,
#702, #703, #704

## v0.11 — i18n and architecture

Lagebuch is German software, and that is a strength. It should not also be an
accident of implementation. Today the German wording is literal text inside
views and domain code, so there is no translation to contribute even if someone
wanted to. Deciding the mechanism comes first; extracting strings before that
means doing it twice. It used to come before the new modules and moved behind
them after 0.9.0 (see v0.10): the wording those modules add is extracted here
with everything else, once #350 has decided how.

The portability review belongs to the same decision. "ILS" is the Bavarian name
for the dispatch centre and is hardcoded — including in the ETB entry written
into the incident file, where it stays for the life of that file. The CO ppm
bands are pegged to the German AGW, the floor labels to EG/OG/UG, the report to
A4 with column widths tuned to German headings, and the Atemschutz retreat rule
to a flat 50 bar with "bar" as a literal rather than a unit. Each of them is the
same question — configuration, translation, or a stable key rendered at display
time — and answering it once is the whole point of doing #350 first.

Alongside it, the layering work: `MasterDataSet` moving out of the persistence
assembly (#297), by then with the new Stammdaten categories from v0.10 in it,
the session abstractions finding their right home, and German ETB wording moving
out of the domain types.

Alt-mnemonics wait for the same decision: a mnemonic is a letter of the label,
so picking them before the labels are translatable means picking them twice.

Issues: #282, #297, #298, #299, #302, #304, #350, #400, #402, #403, #404, #462

## v0.12 — Wasserförderung

Wasserförderung über lange Wegstrecken, restarted in slices rather than as one
large change: the FI 5.008 planner engine first, then the tab with persistence,
sync and the PDF, the Einsatzauftrag per pump, the map mode salvaged from #164,
and finally the execution mode in the field. The maps and elevation packs are
already published in
[lagebuch-regions](https://github.com/CodeForFire/lagebuch-regions).

It used to sit beyond 1.0. It moved forward because it adds tables to the
`.fwincident` file, and like the modules above that is cheap before the freeze
and a permanent forward migration after it.

Issues: #87, #150, #584, #585, #586, #587

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

- **Installs without a warning.** Most of what this used to be a milestone for
  has shipped: winget installs on Windows without the SmartScreen dialog,
  Android releases go out through Google Play, and every release carries SHA-256
  checksums and Sigstore build attestations. The Microsoft Store was dropped
  (#308). Code signing is what remains: the SignPath Foundation declined, and
  OSSign requires six months of activity, so **February 2027** is the earliest a
  free Windows certificate is possible. We plan to apply then (#208). Apple's
  Developer ID is a paid membership and independent of both.
- **The UX review backlog** — accumulated findings from using the app on real
  incidents. Issue: #262.

## Influencing this

This roadmap is a plan, not a contract, and feedback from people who work in an
ELW carries more weight here than anything else.

- Tell us what is missing in [Discussions](https://github.com/CodeForFire/lagebuch/discussions).
- Pick something up: the [good first issues](https://github.com/CodeForFire/lagebuch/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22)
  are real work with a written-out starting point. See
  [CONTRIBUTING.md](CONTRIBUTING.md).
