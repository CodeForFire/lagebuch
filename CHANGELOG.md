# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project is pre-1.0 — breaking changes may occur in any release; see
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) for what that means
once we reach 1.0.

## [Unreleased]

Unreleased entries are not kept in this file. Each change carries its own file under
[`changelog.d/`](changelog.d/), which is assembled into a section here when a release is
cut — see [CONTRIBUTING.md](CONTRIBUTING.md). For the commits themselves, see the
[unreleased changes][Unreleased].

<!-- towncrier release notes start -->

## [0.6.0] - 2026-09-22

The findings of the first exercise, verifiable downloads and the Einsatzdaten dialog the PDF
report's "Adresse" line had been waiting for — plus the Sicherheitstrupp, Trupp-Typ rules that
live in the Stammdaten instead of in a Trupp's name, and a long run of corrections to the PDF
export, the Übersicht and the Stammdaten.

### Added

- [#313](https://github.com/CodeForFire/lagebuch/pull/313) - Stichwort, Einsatznummer, Straße
  and Ortsteil are edited together in one Einsatzdaten dialog from the workspace header, so the
  PDF's "Adresse" line is no longer always empty.
- [#316](https://github.com/CodeForFire/lagebuch/pull/316) - The join dialog prefills the host
  address from the last successful connection instead of starting empty.
- [#333](https://github.com/CodeForFire/lagebuch/pull/333) - Fictional sample data for a first
  try-out — `docs/samples/demo-stammdaten.json` and a matching `uebung.fwincident` — and a
  README that opens in German for ELW crews, with a demo GIF and a sourced comparison table.
- [#375](https://github.com/CodeForFire/lagebuch/pull/375) - Every release ships
  `SHA256SUMS.txt` and a Sigstore-backed build attestation for each installer, so a download
  can be verified while the packages are still unsigned.
- [#380](https://github.com/CodeForFire/lagebuch/pull/380) -
  `docs/datenschutz-und-sicherheit.md`, the German data-protection page for Kommandanten and
  kommunale Datenschutzbeauftragte, naming every category of personal data, where it is stored,
  what the multi-device connection transmits and the DSGVO classification.
- [#408](https://github.com/CodeForFire/lagebuch/pull/408) - Atemschutz records the
  **Sicherheitstrupp** as a real assignment instead of a word in the Trupp-Art, warning — never
  blocking — when a Trupp is in without one, and carrying it into the ETB and the PDF report.
- [#432](https://github.com/CodeForFire/lagebuch/pull/432) - The CO-Messprotokoll keeps a
  Messreihe per Wohnung instead of one overwritten number, each reading timed and attributed,
  printed in the PDF Verlauf and logged to the ETB as its own "Messung" entry.
- [#435](https://github.com/CodeForFire/lagebuch/pull/435) - Tapping either Atemschutz header
  banner opens the ATEMSCHUTZ tab, selects the Trupp the banner names and scrolls its row into
  view. (#422)

### Changed

- [#315](https://github.com/CodeForFire/lagebuch/pull/315) - "Feuerwehr / Wache" and
  "Funkrufname" in the Kräfte entry dock are plain text fields, because the dropdown they used
  to open only re-offered what the Fahrzeug picker already lists.
- [#318](https://github.com/CodeForFire/lagebuch/pull/318) - Wachen and Funkrufnamen are
  derived from the Fahrzeuge and the Personal roster instead of being maintained as separate
  Stammdaten lists.
- [#332](https://github.com/CodeForFire/lagebuch/pull/332) - The "ÖFFNEN" actions in the Links
  tab and the Über dialog share one URL-opening helper, so a link that cannot be opened is
  reported identically in both. (#302)
- [#356](https://github.com/CodeForFire/lagebuch/pull/356) - Each attached PDF in the exported
  report is prefixed with a caption page echoing its Files-list row. (#262)
- [#376](https://github.com/CodeForFire/lagebuch/pull/376) - The task list's high-priority and
  due text and the home view's accent gradient use the shared signal colour token instead of
  duplicating its hex value; rendered output is unchanged.
- [#390](https://github.com/CodeForFire/lagebuch/pull/390) - Every path is joined with
  `Path.Join` instead of `Path.Combine`, which silently discards everything before an argument
  that turns out to be rooted, and `IDE0059` is now a warning so a dead local fails the build.
- [#431](https://github.com/CodeForFire/lagebuch/pull/431) - Stammdaten hold 0..n named
  Checkliste-Vorlagen instead of a fixed Aufbau/Abbau pair, and a new **Navigation** category
  sets the order of the Einsatz sidebar and which entries it shows (schema V23; the ETB cannot
  be switched off).
- [#440](https://github.com/CodeForFire/lagebuch/pull/440) - The inert TRUPPNUMMER field is
  gone from the Atemschutz Bereitstellen row; the number is still assigned automatically and
  shown in the TRUPP column. (#416)

### Fixed

- [#310](https://github.com/CodeForFire/lagebuch/pull/310) - Linux: the desktop entry is
  complete and the app icon resolves in the menu and the dock — a real file per hicolor size
  plus the scalable SVG, and a dependency on `hicolor-icon-theme`.
- [#314](https://github.com/CodeForFire/lagebuch/pull/314) - The sync server binds dual-stack
  instead of IPv4 only, so a device reachable only over IPv6 can join a hosted incident.
- [#325](https://github.com/CodeForFire/lagebuch/pull/325) - Linux: the `.deb` declares the
  system libraries it actually needs, so a machine without a desktop environment no longer
  installs it successfully and then dies on a missing ICU package.
- [#329](https://github.com/CodeForFire/lagebuch/pull/329) - The four small preference files
  are written atomically, so a crash or a full disk mid-write can no longer leave a truncated
  file the next start reads as "nothing remembered". (#302)
- [#331](https://github.com/CodeForFire/lagebuch/pull/331) - A failed write to `trust.json` no
  longer leaves the running app trusting a host certificate the file does not record; the write
  now happens before the change is adopted in memory. (#302)
- [#334](https://github.com/CodeForFire/lagebuch/pull/334) - The PDF's Aufgaben section marks a
  task "FÄLLIG" against the moment the export was taken rather than the wall clock while the
  document renders, so exporting a closed Einsatz twice produces the same table. (#302)
- [#335](https://github.com/CodeForFire/lagebuch/pull/335) - Alarm cues get a dedicated thread
  each instead of queueing behind unrelated work on a saturated thread pool, where one could
  sit unplayed for seconds or outlast its own watchdog.
- [#355](https://github.com/CodeForFire/lagebuch/pull/355) - Kräfte: a unit's Status shows even
  when the Stammdaten no longer list it, and a Funktion matching the Stammdaten apart from case
  or stray spaces is recorded with the Stammdaten spelling. (#302)
- [#359](https://github.com/CodeForFire/lagebuch/pull/359) - CO-Messung: the empty-state
  message uses German typographic quotes and the missing comma before "um zu beginnen".
- [#371](https://github.com/CodeForFire/lagebuch/pull/371) - Handing the workspace view from
  one incident to another no longer lets the first incident's "weiter bearbeiten" prompt act on
  the second. (#302)
- [#372](https://github.com/CodeForFire/lagebuch/pull/372) - PDF export: the CO-Messprotokoll
  legend draws real colour swatches and a task is marked ● against ○, instead of borrowing ■
  and ✔ from whatever font the rendering machine happened to have.
- [#377](https://github.com/CodeForFire/lagebuch/pull/377) - A PDF export no longer fails
  outright because of a single character the report font cannot draw; such a character is left
  blank and the export completes.
- [#387](https://github.com/CodeForFire/lagebuch/pull/387) - A failing once-a-second update no
  longer takes the other ticker subscribers down with it, and the tick no longer allocates a
  fresh subscriber array every second. (#302)
- [#388](https://github.com/CodeForFire/lagebuch/pull/388) - Android: a file picked from
  another app goes through the same sanitiser as an attachment name from a joined device, so a
  name that only Windows rejects can no longer reach a Windows peer intact. (#302)
- [#389](https://github.com/CodeForFire/lagebuch/pull/389) - A joined device's attachment cache
  is capped at 500 MB instead of growing without limit across every Einsatz the tablet ever
  joined. (#302)
- [#392](https://github.com/CodeForFire/lagebuch/pull/392) - Android: picking a file that
  cannot be read reports an error line instead of taking the app down from inside Android's
  result callback. (#302)
- [#394](https://github.com/CodeForFire/lagebuch/pull/394) - The download-verification
  instructions name the `.dmg.sha256` path, instead of sending macOS users to a
  `SHA256SUMS.txt` that cannot contain their download.
- [#395](https://github.com/CodeForFire/lagebuch/pull/395) - Übersicht: the page no longer
  jumps sideways while scrolled, the "Zuletzt verwendet" list no longer has a scrollbar of its
  own, and the corner glow no longer renders as a grey rectangle with hard edges. (#376)
- [#396](https://github.com/CodeForFire/lagebuch/pull/396) - An Einsatzdatei written by a build
  from another development branch opens again: the app now reconciles a file's actual columns
  against the expected schema regardless of the recorded version.
- [#411](https://github.com/CodeForFire/lagebuch/pull/411) - Atemschutz: a Trupp-Typ carries
  its own Stärke and Einsatzzeit in the Stammdaten, so a brigade naming it `CSA Trupp` or
  `Chemietrupp` no longer gets a two-person Trupp accepted on the full 30 minutes. (#418)
- [#436](https://github.com/CodeForFire/lagebuch/pull/436) - Funktionen: the Übergabe panel's
  heading and arrow use `TextSecondary` instead of the half-transparent `TextMuted`, which was
  about 1.9:1 against the dark panel where WCAG AA asks for 4.5:1. (#413)
- [#437](https://github.com/CodeForFire/lagebuch/pull/437) - An empty required field says so
  instead of leaving the button grey and unexplained; the message explains and never blocks a
  running Einsatz. (#412)
- [#439](https://github.com/CodeForFire/lagebuch/pull/439) - ETB: the history lines of an
  amended entry use `TextSecondary` instead of the half-transparent `TextMuted`, which was
  practically unreadable on the dark panel. (#438)
- [#441](https://github.com/CodeForFire/lagebuch/pull/441) - Atemschutz: both header banners
  and the ETB lines naming a second Trupp lead with the Funkrufname, so it is clear which
  brigade's "Trupp 2" is being called, and the red Rückzugsalarm is truncated with an ellipsis
  instead of losing its reason off the edge. (#417)
- [#442](https://github.com/CodeForFire/lagebuch/pull/442) - CO-Messprotokoll: shrinking a
  Wohnungszahl asks which Wohnungen are to go, listing what each one holds, instead of silently
  deleting from the right — and **OG/UG HINZUFÜGEN** no longer drops every Wohnung above the
  building default. (#419)
- [#445](https://github.com/CodeForFire/lagebuch/pull/445) - Android: the published APK
  declares the `INTERNET` permission, without which "Einsatz beitreten" always failed on an
  installed Lagebuch — the SDK adds it to debug builds by itself, so it worked on every
  development device and no shipped one. (#381)

### Security

- [#386](https://github.com/CodeForFire/lagebuch/pull/386) - Attachment names are stripped of
  invisible formatting characters, not just control characters: U+202E and its relatives are
  `UnicodeCategory.Format` and survived the old filter, letting an executable render as
  "Lageplan exe.png". (#302)


## [0.5.0] - 2026-09-11

Attachment handling, PDF export control, more depth in CO-Messung and accessibility —
plus every P0/P1 fix from the 2026-09 architecture, security and performance review (#304).

### Added
- Drag and drop attachments onto the Dateien tab (#273)
- Delete an attachment, with a confirmation prompt (#271)
- PDF export: pick which sections go in, with progress and status feedback, and the incident
  remembers where it was last exported to (#269)
- CO-Messung: colour-coded ppm danger severity and a warning on implausible readings (#278)
- CO-Messung: per-floor unit counts and labels (#268)
- CO-Messung: an OG HINZUFÜGEN button to add upper floors (#263)
- A visible ÖFFNEN button on each recent-incidents row (#276)
- Search box on the Links tab, and ÖFFNEN now shows and says that it opens the system browser (#262, #275)
- Accessibility: AutomationProperties labelling and keyboard navigation for the Kräfte flyouts (#283)
- The project logo and slogan (#281)

### Changed
- CO-Messung: ABBRECHEN in the Wohnung editor now actually discards. Status, CO-Wert,
  Bezeichnung, Bewohnername and Schlüssel are buffered until FERTIG, so an intermediate or
  mistyped ppm reading no longer lands in the Einsatztagebuch. The tile previews the pending
  state while the sidebar is open. (#242)
- The PIN field on the join screen accepts digits only (#277)
- Removing a unit asks for confirmation first, and Kräfte/Stammdaten explain why a control is
  disabled or a label abbreviated (#257)
- Release notes are generated from this file's section for the tag being built (#261)
- Dependency updates across Avalonia, Microsoft.Data.Sqlite, the SignalR client and the test SDK

### Fixed
- Stop a Kraft's Stärke-Historie from duplicating on every save: each save was re-inserting
  the whole edit history on top of what was already on disk, so a file saved N times held N
  copies of every correction. Existing files are deduplicated the next time they are opened. (#279)
- Surface a failed background save (disk full, read-only, locked/corrupt DB) as a persistent
  red banner in the Einsatz workspace instead of silently leaving the incident unsaved (#280)
- Leaving an open Einsatz via the top command bar (ÜBERSICHT, STAMMDATEN, ÖFFNEN, NEUER EINSATZ,
  VERBINDEN) instead of the workspace's own "ZUR STARTSEITE" no longer leaks its subscription to
  the background save-failure store (#280)
- Navigating home no longer leaves the incident workspace running in the background: closing it
  now stops its once-a-second ticker (Atemschutz, Aufgaben, ILS-Erinnerung) and unsubscribes it
  from further changes, and a joined client also drops its host-connection event handlers. (#284)
- Funktionen: the Rolle suggestions now open on focus and on click, not only while typing (#266)
- Kräfte: the Zugführer flag no longer adds a seat on top of the vehicle's own capacity (#264)

### Security
- Harden CI workflows: `ci.yml` now runs with read-only `contents` permission and enforces
  `dotnet format` in CI; `claude.yml` only responds to `@claude` mentions from repo owners,
  members and collaborators; Dependabot now tracks the Android and acceptance-test projects'
  own `Directory.Packages.props` files in addition to the root one.
- Attachment names from a joined device can no longer escape the temp directory or smuggle in an
  executable type. A file name is reduced to its last path segment and capped at 255 bytes on the
  way into the domain (and on load, so a name already in a snapshot or file is neutralised too),
  its extension must match the declared file type, ÖFFNEN copies the bytes into a fresh private
  directory under the app's own temp root instead of the shared system temp directory, and the
  desktop launcher refuses anything that is not a regular image or PDF file inside that root. (#285)
- `JsonTrustStore` now takes its lock for reads too, closing a race where the TLS
  certificate-pin check on a join could read the trusted-thumbprint cache while another
  handshake was writing it; the trust file is also now written via a temp file plus rename, so
  a crash mid-write can no longer leave a corrupt `trust.json` behind. Joining another device's
  incident no longer has an "accept any certificate" fallback — a trust store is required, so a
  join is always TLS-pinned. (#286)
- Android: the `FileProvider` now grants access to only its `shared/` and `lagebuch/`
  subdirectories, not the whole cache directory — `import.json`, picked attachments and the
  synced-attachment cache are no longer reachable through a shared or opened URI. A malicious
  content provider's `DISPLAY_NAME` for a picked attachment can no longer escape the app's cache
  directory via `../` path traversal; it is now sanitised to a bare file name with a safe fallback. (#303)

## [0.4.1] - 2026-09-07

### Added
- Show the 25 MB per-file attachment limit in the Files view (#213)
- Create a task from an already-saved ETB entry via a row icon (#247)
- Add a Zugführer headcount to Kräfte (#233)
- Support Untergeschoss (UG) floors in CO-Messung (#235)
- Add a +5 minute snooze for the ILS reminder (#244)
- Add a Zugführer flag to vehicle master data (#248)
- Derive the Feuerwehr automatically from a single Fahrzeug pick (#231)
- Lock the brigade and call sign once a Fahrzeug is picked (#251)

### Changed
- Unify the "add entry" button placement across tabs (#236)

### Fixed
- Hide ETB system messages by default (#230)
- Keep the selected Haus after entering a ppm value in CO-Messung (#238)
- Make the apartment header field look editable in CO-Messung (#234)
- Make Truppnummer an internal, auto-assigned SCBA field (#226)
- Stop the Aufgabe alarm from clipping (#228)
- Disable HINZUFÜGEN until a Kraft row has counted personnel (#227)
- Stop bundling QuestPDF into the Android build (#252)
- Allow adding an Einsatznummer even without a Stichwort (#253)

## [0.4.0] - 2026-09-05

TLS/TOFU sync, .NET 10 & Avalonia 12 migration, host-driven Stammdaten sync,
stability and performance.

### Added
- TLS with trust-on-first-use (TOFU) and PIN rate limiting for network sync (#167, #177)
- The incident host can serve as the Stammdaten source for connected devices, with join validation and TOFU reset (#183, #191)
- Cancel a device join in progress (#194)
- Truppnummer, a 3-state Atemschutz lifecycle, and spoken Druckabfrage/Rückzugsalarm cues (#147, #149)

### Changed
- Migrated to .NET 10 and Avalonia 12 (#179)
- Sync file uploads are streamed instead of base64-embedded in JSON (#193)
- PDF attachments are merged from disk instead of loaded as byte arrays (#192)
- Vector PathIcons replace Unicode icon glyphs (#199)
- Reworked Stammdaten editor layout; fixed Fahrzeuge reordering (#201)

### Fixed
- Incident saves and file-store writes moved off the UI thread during sync (#178, #190)
- Users can reset TOFU trust from the join-error banner (#181, #186)
- Join dialog stays open across a failed join attempt (#195)
- Sidebar scrolls instead of wrapping into columns on short viewports (#151)
- Guard against confirming an empty building name in the CO-Messung protocol (#200)
- DispatcherTimerTicker subscriber list is synchronized (#202)
- Picked attachments are size-checked before being read into memory (#203)
- SystemAlarmService's temporary WAV files are cleaned up on exit (#204)
- BuildChildren disposes all its children, not just three (#206)

## [0.3.0] - 2026-08-27

### Added
- Refined Atemschutz/ILS defaults: LPA duration, 50-bar retreat pressure, second ILS interval (#80)
- Two-stage ILS Rückmeldung reminder with a spoken, cross-platform cue (#82)
- Reminder timer persists across close/reopen and crashes (#83)
- ILS-Nummer made optional; Stichwort leads the incident header (#84)
- Checkliste: Aufbau/Abbau tabs, mandatory items, colored tabs, ETB reporting (#85)
- Attach images and PDFs to an incident (#86)
- Stammdaten linked from a quick-access workspace tab (#90)
- ETB: Rufnamen suggestions on Von/An, edit an entry with full history (#91)
- Funktionen: transfer role, editable Handynummer, free-text Funktion, current-only filter (#93)
- About dialog with CodeForFire branding, license and repo link (#109)
- Strength tracked as GF/Mann/Gesamt with a correction log and master-data vehicles (#118)
- AUFGABEN task list with urgency timers, creation from ETB, and check-off (#136)
- Labels and example placeholders on every input field (#143)

### Changed
- **Breaking:** rebranded the product and solution from Feuerwehr to LageBuch (#117)
- Watermark placeholder replaced with PlaceholderText (#140)

### Fixed
- Kräfte/Dateien free-text edits commit on blur, not per keystroke (#94)
- Checkliste Abbau moved to the last tab (#95)
- Android file dialog service uses `global::` for `Android.*` references (#106)
- ILS reminder spoken cue repeats every 60s until acknowledged (#144)
- Funkrufname fields unified to AutoCompleteBox (#145)
- CO-Messung: editable headers, compact rows, layout fixes (#138)

## [0.2.0] - 2026-08-18

### Added
- Multi-device sync: share an incident on the network without Tailscale (localhost/LAN), a "connect to device" join flow, and a share PIN (#52, #55, #57, #58, #59, #60, #65)
- Android port: shared core (`Feuerwehr.App.Shared`) plus an Android app head (#54)
- Stammdaten via import/export instead of a compiled-in seed (#49)
- Complete, unified Bavarian Einsatznummer format (#50)
- Configurable timer/duration defaults in Stammdaten (#63)
- Flame app icon in the command bar, replacing the "L" badge (#51)
- Header reworked: Einsatznummer as the hero, app name shown only once (#66)

### Fixed
- Share PIN shown immediately on first share (#67)
- Network Changed events marshalled onto the UI thread (#61)

## [0.1.0] - 2026-07-24

First release (Windows + Linux prerelease).

### Added
- Initial incident (Einsatz) domain model, SQLite-backed persistence (`.fwincident` files), and PDF incident-report generation
- Avalonia desktop UI with acceptance tests and Windows/Linux release builds
- Read-only-by-default file opening with in-workspace continue-editing (#7)
- ILS reminder timer for Rückmeldung an ILS (#9)
- Einsatznummer and ILS-Nummer entry (#11)
- Atemschutzüberwachung (SCBA monitoring) module, registering Trupps as crews (#12, #34)
- ETB automatic lifecycle-transition logging, plus a System event type/filter (#19, #38)
- Funktionen list with Abschnitt, von/bis and Handynummer (#23)
- Kräfte list: brigade list, AGT count, editable status and Bemerkung (#24)
- Call-sign dropdown in the operator prompt (#39)
- Home screen: dated new-incident filenames, closed incidents marked (#42)
- In-app Stammdaten editor (#45)
- Keyboard-first entry and SCBA/close safety guards (#13)
- Dark command-console UI redesign (#10)

### Fixed
- Checklist checkbox state not persisting (#6)
- Shell aligned to a single 24px content gutter (#14)
- German direction labels in the ETB picker (#22)
- Refuse to open newer-schema incident files instead of crashing (#30)
- Flame app icon in place of the cross pattée (#37)
- AutoCompleteBox border matched to app inputs (#41)
- ILS countdown made the visual focus of the reminder bar (#44)

[Unreleased]: https://github.com/CodeForFire/lagebuch/compare/v0.6.0...HEAD
[0.6.0]: https://github.com/CodeForFire/lagebuch/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/CodeForFire/lagebuch/compare/v0.4.1...v0.5.0
[0.4.1]: https://github.com/CodeForFire/lagebuch/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/CodeForFire/lagebuch/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/CodeForFire/lagebuch/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/CodeForFire/lagebuch/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/CodeForFire/lagebuch/releases/tag/v0.1.0
