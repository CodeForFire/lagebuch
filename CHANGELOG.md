# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project is pre-1.0 — breaking changes may occur in any release; see
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) for what that means
once we reach 1.0.

## [Unreleased]

### Added
- Show the 25 MB per-file attachment limit in the Files view (#213)

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

[Unreleased]: https://github.com/CodeForFire/lagebuch/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/CodeForFire/lagebuch/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/CodeForFire/lagebuch/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/CodeForFire/lagebuch/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/CodeForFire/lagebuch/releases/tag/v0.1.0
