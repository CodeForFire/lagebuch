# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project is pre-1.0 — breaking changes may occur in any release; see
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) for what that means
once we reach 1.0.

## [Unreleased]

### Added
- The shipped Stammdaten examples now include a **Strahlenschutztrupp**: three people like a
  CSA-Trupp, but the full 30 minutes. That pair is the proof that the rules hang on the
  Stammdaten row rather than on the name — two three-person types with *different*
  Einsatzzeiten is something the old logic, wired to the literal `CSA-Trupp`, could not express
  at all. A brigade that calls the Trupp something else, or runs it on a different Einsatzzeit,
  enters that in Stammdaten; it is no longer a code change. (#418)
- `docs/datenschutz-und-sicherheit.md`: the German data-protection page for Kommandanten,
  Kreisbrandinspektionen and kommunale Datenschutzbeauftragte. It names every category of
  personal data the app holds (including the CO-Messprotokoll's resident names and the roster's
  phone numbers), every storage location, everything the multi-device connection transmits,
  the split between what Lagebuch does and what the brigade has to arrange itself, the DSGVO
  classification — no Auftragsverarbeitung, so no AV-Vertrag — a complete deletion checklist,
  and the known limits, unsigned packages and unverified first connection included. Linked from
  the README, `SECURITY.md` and `docs/master-data.md`. `SECURITY.md`'s "Known limitations"
  section now carries that same list of limits in English, written for security researchers,
  so the two documents no longer disagree about what is known to be missing.
- Every release now ships `SHA256SUMS.txt` and a Sigstore-backed build attestation for each
  installer, so a download can be verified with `sha256sum -c` and
  `gh attestation verify <Datei> --repo CodeForFire/lagebuch` while the packages are still
  unsigned. The macOS `.dmg`, which is attached later, carries its own `.sha256` file.
- Einsatzdaten dialog: Stichwort, Einsatznummer, Straße and Ortsteil are edited together from a
  pencil in the workspace header (or "+ Einsatzdaten ergänzen" while nothing is known yet). The
  address had no UI at all before, so the PDF's "Adresse" line was always empty; it now shows in
  the header and the PDF, and a change made on one device reaches every joined device.
- The join dialog remembers the host address used for the last successful connection and
  prefills it (selected, ready to overwrite) instead of starting empty every time.
- Fictional sample data for a first try-out ("Probefahrt"): `docs/samples/demo-stammdaten.json`
  and a matching `docs/samples/uebung.fwincident`, generated and kept valid by a test. The README
  now opens in German for ELW crews, with a demo GIF and a sourced comparison table; the
  developer documentation follows in English.

### Changed
- Every path is now joined with `Path.Join` instead of `Path.Combine`. `Path.Combine`
  silently discards everything before an argument that turns out to be rooted, so a name
  that slipped through sanitisation could point outside the folder it was meant to land in;
  `Path.Join` always concatenates. Behaviour is unchanged for every existing call — each
  one passes a relative later argument — and untrusted names still go through
  `IncidentFile.SanitizeFileName` or `SafeFileName.Sanitize` first.
- The task list's high-priority and due text plus the home-view accent gradient now use the
  shared signal colour theme token instead of duplicating its hex value; rendered output is
  unchanged.
- Wachen and Funkrufnamen are derived from the Fahrzeuge (plus the Personal roster's call
  signs) instead of being maintained as separate Stammdaten lists. Their editor sections and
  the `brigades` / `radioCallSigns` JSON keys are gone; importing an older file names any
  entry that no vehicle or person covers.
- The new-incident dialog no longer asks for a Stichwort; it is entered afterwards in the
  Einsatzdaten dialog, like the Einsatznummer always was. New files are therefore named by date
  and time only (`20260819-2217.fwincident`), and the header's inline "+ ILS-Nr. hinzufügen"
  editor is replaced by that dialog.
- Kräfte tab: "Feuerwehr / Wache" and "Funkrufname" in the entry dock are plain text fields.
  They exist for vehicles that are not in the Stammdaten, so the suggestion dropdown they
  used to open on focus only re-offered what the Fahrzeug picker already lists. Enter in
  either field adds the row.
- The "ÖFFNEN" actions in the Links tab and the Über dialog share one URL-opening helper, so a
  link that is blocked or cannot be opened is validated and reported identically in both places.
  No change to what either shows. (#302)
- Prefix each attached PDF in the exported report with a caption page echoing its Files-list row (#262)

### Fixed
- Atemschutz: a Trupp-Typ now carries its own Stärke and Einsatzzeit in the Stammdaten, instead
  of the app recognising the names `CSA-Trupp` and `LPA-Trupp`. Those two strings decided whether
  a Trupp needed three people and which Einsatzzeit was suggested — but the Trupp-Typen are a
  list the brigade edits freely, so a Wehr writing `CSA Trupp`, `CSA-Trupp (Chemikalienschutz)`
  or `Chemietrupp` got a **two-person CSA-Trupp accepted without a word of complaint**, on the
  full 30 minutes instead of 20. There was no warning and nothing in the Einsatztagebuch; the
  crew under the suits would have been the ones to find out. Stammdaten → Trupp-Typen now has a
  Stärke (2 or 3) and an Einsatzzeit per row, so the rule follows the type however it is named,
  and a brigade can give a Sicherheitstrupp or a self-defined type the same treatment. Existing
  Stammdaten are migrated once, on first open, carrying over the Einsatzzeiten the brigade had
  configured rather than the shipped defaults, so nothing changes for anyone using the shipped
  spellings; the three global Einsatzzeit-Einstellungen (AGT/CSA/LPA) are gone, replaced by the
  per-type value. A Stammdaten file exported by an older version still imports, and a Stärke or
  Einsatzzeit edited into a file by hand is clamped to something the Atemschutz form can work
  with rather than taken literally.
- Stammdaten: every repairable column in `masterdata.db` is now restored on open, and a test
  sweeps all of them so the next one cannot be forgotten. The file has no schema version, so it
  reconciles itself on every open — but that reconciliation was three hand-written lines, and a
  column added to the schema without a matching line would have broken every *existing* store
  while a fresh one looked fine. `md_personnel`'s optional columns had no such line; no shipped
  version ever lacked them, so nothing was broken in practice, but nothing would have caught it
  either. (#397)
- Atemschutz: a Trupp whose positions had a gap — a Truppführer and a 2. Truppmann with nobody
  as Truppmann — was accepted, because only duplicate positions were rejected, not missing ones.
  The monitoring sheet has no way to show that, so it is now refused at registration.
- Seven dead local assignments, one of them in `EqualWidthWrapPanel`'s arrange pass, left over
  from earlier refactors. `IDE0059` ships at suggestion severity, below the threshold
  `TreatWarningsAsErrors` acts on, so they had accumulated unnoticed; it is now a warning and
  therefore a build error, so the next one cannot.
- Android: picking a file that cannot be read no longer takes the app down. The chosen file is
  streamed into the app's own storage before the picker returns, and a content provider that hands
  back nothing — or a full disk — threw from inside Android's result callback, where nothing was
  there to catch it. Both the Stammdaten import and the attachment picker now report it as an
  error line instead, and a provider that will not say what the file is called falls back to a
  generic name rather than abandoning the pick. (#302)
- Android: a file picked from another app is now cleaned up the same way an attachment name from a
  joined device already was. The two had grown apart — the picked-name path stripped only the
  characters the running OS rejects, so on Android a name could keep `< > : " | ? *`, invisible
  formatting characters, or run past the 255-byte limit every filesystem enforces, and only broke
  once the file reached a Windows peer. Both now share one sanitiser. (#302)
- A joined device's attachment cache is now capped at 500 MB instead of growing without limit.
  Every attachment pulled from the host was written to disk and nothing ever deleted it — not
  leaving the incident, not joining the next one — so a tablet used across many Einsätze kept
  every attachment of every one of them. Once a newly cached file pushes the cache over the cap,
  the oldest entries are removed until it is back under. (#302)
- A failing once-a-second update no longer takes the others down with it. The Atemschutz, Aufgaben
  and ILS-Erinnerung timers share one ticker, which called each subscriber without isolation — so
  one that threw skipped everything after it in that tick and surfaced as an unhandled UI
  exception. Each is now called on its own and a failure is reported instead of swallowed. The
  tick also no longer allocates a fresh subscriber array every second. (#302)
- A PDF export no longer fails outright because of a single character the report font cannot
  draw. Anything typed into an Einsatz reaches the export — ETB entries, Aufgaben, attachment
  names — and since the QuestPDF update an unrenderable character (an emoji, a name in another
  script) aborted the whole document, leaving only "Export fehlgeschlagen" and no report at all.
  Such a character is now simply left blank and the export completes. The text LageBuch itself
  writes is held to the stricter rule in the test suite, where a character the bundled font
  lacks still fails the build.
- Übersicht: the page no longer jumps sideways while it is scrolled. The content column was
  sized to its widest child, and that child is the virtualized "Zuletzt verwendet" list, whose
  measured width changes as rows are realized and recycled — so a recent entry with a long path
  made the column 720px wide and scrolling it out of view snapped the whole page down to ~643px
  and back. The column is now pinned to the viewport width (capped at 720) regardless of what
  the list happens to be showing.
- Übersicht: the "Zuletzt verwendet" list no longer has a scrollbar of its own. Nested inside
  the page's scroller it swallowed the wheel until it hit its own bottom, so the list moved
  several rows before the page moved at all. The list now sizes to its (at most ten) entries
  and the page is the only thing that scrolls.
- Übersicht: the decorative glow in the top-right corner no longer renders as a grey rectangle
  with hard edges. It faded to `Transparent` — which is transparent *white*, so the gradient
  drifted through grey — and stopped fading at 70%, cutting the 320×320 box mid-gradient; its
  bright end also pointed into the middle of the page instead of at the corner. It is now a
  radial fade anchored on the corner, ending on a fully transparent stop of the same signal
  colour (a new `SignalFadedColor` token, so the two stops cannot drift apart). (#376)
- Handing the workspace view from one incident to another no longer lets the first incident's
  "weiter bearbeiten" prompt act on the second. The view kept its handlers on the abandoned
  prompt, and those handlers followed the view rather than the incident they belonged to, so
  cancelling the old prompt closed the dialog the operator was actually looking at. Both the
  workspace and the main view now detach from a prompt before wiring up the next one. (#302)
- PDF export: the CO-Messprotokoll legend and the "Erledigt" marker in the Aufgaben table came
  out as empty boxes on some machines. Both used characters the bundled Lato font does not
  carry (■ U+25A0, ✔ U+2714), so they were quietly borrowed from whatever font the rendering
  machine happened to have installed — fine on a developer desktop, blank on a slim container
  or on Android, with nothing in the logs to say so. The legend now draws its three colour
  swatches as real rectangles (taking their colours from the same source as the floor grid, so
  the two can no longer drift apart) and a completed task is marked ● against ○ for an open
  one. The export no longer depends on any font beyond the one it ships with.
- Alarm cues could be delayed or silently skipped while the app was busy. Each cue ran on a
  thread-pool thread, and a pool saturated by other work hands out threads only as fast as it
  grows them — so a cue could sit unplayed for seconds, or outlast its own hung-player watchdog
  without ever having started. Alarms fire exactly when the app is busiest, which is precisely
  when this bit. Cues now get a dedicated thread each and no longer queue behind unrelated work.
- Kräfte: a unit's Status shows even when the Stammdaten no longer list it. The Status cell is a
  closed dropdown, so a status recorded in an older Einsatz, an imported file or by a joined
  device running different Stammdaten had nothing to select and rendered as an empty cell — the
  status was still in the file, just invisible. It is now carried as an extra entry in that row's
  dropdown. (#302, see #337)
- Funktionen: a Funktion that matches the Stammdaten apart from upper/lower case or stray spaces
  is recorded with the Stammdaten spelling, so "el" and "EL " no longer pile up next to the
  configured "EL". An unknown Funktion is still assigned exactly as typed — ad-hoc and
  überörtliche roles must stay enterable — but the dock now says it is not in the Stammdaten. (#302)
- The four small preference files — recent incidents, last save folder, last PDF export and last
  join host — are written atomically (to a temp file that is then renamed into place), the way
  `trust.json` already was. A crash or a full disk mid-write used to be able to leave a truncated
  file behind, which the next start silently read as "nothing remembered". (#302)
- The PDF's Aufgaben section marks a task "FÄLLIG" against the moment the export was taken,
  instead of reading the wall clock while the document renders. Exporting the same closed
  Einsatz twice now produces the same table, where before a task could be shown as due on one
  export and overdue on the next. (#302)
- A failed write to `trust.json` no longer leaves the running app trusting a host certificate the
  file does not record. Accepting or resetting a host's certificate updated memory first and wrote
  afterwards, so if the write failed (full disk, permissions) the session kept connecting happily
  while the next start re-prompted for the same host. The write now happens first and is only
  adopted once it lands, and a retry after a failed reset does the work instead of silently
  skipping it. (#302)
- The sync server only bound IPv4 (`0.0.0.0`), so a device reachable only over IPv6 could never
  join a hosted incident. It now binds dual-stack, accepting both IPv4 and IPv6 on the same
  socket, and falls back to IPv4-only itself if the platform doesn't support IPv6.
- Linux: the app icon now resolves in the menu and the dock. The `.deb` shipped a single
  1024px file filed under the theme's `512x512` directory and depended on nothing, so on a
  system without a desktop icon theme there was no theme to find it in at all, and everywhere
  else every size — panel, menu, dock, app grid — was scaled down from that one megapixel
  image. It now installs a real file for each hicolor size plus the scalable SVG, and depends
  on `hicolor-icon-theme`. (#310)
- Linux: complete the desktop entry — window-to-launcher matching (`StartupWMClass`), search
  keywords, a subtitle and an accurate description in `apt show` (#310)
- Linux: the `.deb` now declares the system libraries it actually needs. "Self-contained"
  covers the .NET runtime, not ICU, fontconfig and the X11 client libraries — so on a machine
  without a desktop environment already installed, `apt install ./lagebuch_*.deb` reported
  success and the app then died immediately with "Couldn't find a valid ICU package installed
  on the system". Install it with `apt` rather than `dpkg -i`, which cannot resolve
  dependencies. Verified on Debian 12/13 and Ubuntu 22.04/24.04.
- CO-Messung: corrected the empty-state message to use German typographic quotes and added the missing comma before "um zu beginnen".
- An Einsatzdatei that a build from another development branch had written can be opened again.
  Each file records a schema version, and the app runs only the migrations numbered above it —
  sound as long as that number means the same thing everywhere. A parallel branch numbers its own
  migrations too, so a file it touched could come back carrying a version the released app had
  never applied: the app then skipped a migration the file genuinely needed, and opening it failed
  with "no such column: previous_zugfuehrer_count" and no way forward. Opening a file now also
  compares its actual columns against the expected schema and adds whatever is missing, regardless
  of the recorded version, so an affected file repairs itself the next time it is opened. Columns
  and tables the app does not know are left untouched.
- The download-verification instructions no longer send macOS users to a file that cannot
  contain their download. `SHA256SUMS.txt` covers the `.msi`, the `.deb` and the `.apk`; the
  `.dmg` is built after the release exists and carries its own `.dmg.sha256`, so
  `shasum -a 256 -c SHA256SUMS.txt` — the only line the README and the release notes offered a
  Mac user — failed on every one of the three files it listed. Both now name the `.dmg.sha256`
  path as well.

### Security
- Attachment names are stripped of invisible formatting characters, not just control characters.
  U+202E and its relatives are `UnicodeCategory.Format`, so they survived the old filter: they
  render as nothing but reverse the text after them, which let `Lageplan‮gnp.exe` appear as
  "Lageplan exe.png" in the Dateien list and the PDF while still being an executable. Such a name
  is now rejected on the way in, and one already stored is neutralised on load. (#302)

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

[Unreleased]: https://github.com/CodeForFire/lagebuch/compare/v0.5.0...HEAD
[0.5.0]: https://github.com/CodeForFire/lagebuch/compare/v0.4.1...v0.5.0
[0.4.1]: https://github.com/CodeForFire/lagebuch/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/CodeForFire/lagebuch/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/CodeForFire/lagebuch/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/CodeForFire/lagebuch/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/CodeForFire/lagebuch/releases/tag/v0.1.0
