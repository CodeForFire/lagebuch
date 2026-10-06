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

## [0.8.0] - 2026-10-06

Keyboard control. A Lagebuchführer at the ELW laptop can now run an Einsatz without reaching
for the mouse: every dialog and inline panel takes focus, keeps Tab inside, closes on Esc and
gives focus back; Enter submits every entry form and returns to its first field; the suggestion
fields follow one keyboard model; a Druckkontrolle is recorded from the keyboard; the lists answer
to row keys; global shortcuts open modules and jump to the most urgent warning, with F1 listing
them all; and keyboard focus is visible everywhere. An Aufgabe can now be edited after it was
created. That edit travels over sync, so the sync protocol rises to 7 and every device in a shared
Einsatz needs 0.8.0.

### Added

- [#558](https://github.com/CodeForFire/lagebuch/pull/558) - A Druckkontrolle can now be recorded from the keyboard: the Druckabfrage and Rückzugsalarm bars put the caret in that Trupp's Druck field, Enter records the value and moves on to the next due Trupp, an empty field says so instead of recording the last reading again, and a Druck that rises or falls faster than air can (27 typed for 270) is asked about once before it is recorded (#539).
- [#564](https://github.com/CodeForFire/lagebuch/pull/564) - The ETB, Kräfte, Funktionen and Aufgaben lists now answer to row keys: Enter or F2 opens a row's edit, Stärke or transfer, Del asks before removing a Kräfte unit and does nothing on an ETB entry, and Space ticks an Aufgabe off, which a RÜCKGÄNGIG notice or Ctrl+Z takes back for a few seconds (#543).
- [#565](https://github.com/CodeForFire/lagebuch/pull/565) - Enter now opens the selected recent file on the start screen, and the arrow keys move across the CO-Messung matrix — left and right between the Wohnungen of a floor, up and down between floors — so a Wohnung is reached and opened without tabbing through every tile before it (#543).
- [#567](https://github.com/CodeForFire/lagebuch/pull/567) - Global keyboard shortcuts: Ctrl+1 … Ctrl+0 open a module whatever its place on the rail, Ctrl+Tab and Ctrl+Shift+Tab walk the rail, Ctrl+N starts an ETB entry in VON, F9 goes to the most urgent warning (Rückzugsalarm, Druckabfrage, Aufgabe, ILS Rückmeldung), and F1 or the ? button in the command bar lists them all, with each module's key shown on the rail (#544).
- [#569](https://github.com/CodeForFire/lagebuch/pull/569) - An Aufgabe can now be corrected after it was created — Wichtigkeit, Dringlichkeit, Zugeteilt an and the text, in a panel below the list opened with the pencil, Enter or F2, while the list keeps its priority colours — and +5 MIN, in that panel and on the Aufgabe-fällig bar, puts a task off by five minutes; Wichtigkeit and Dringlichkeit are now picked on a coloured Niedrig | Mittel | Hoch scale everywhere, and because a joined device can send these edits the sync protocol rises to 7, so every device in a shared Einsatz needs this version (#246).

### Changed

- [#554](https://github.com/CodeForFire/lagebuch/pull/554) - Keyboard focus is now visible on every control as a cyan edge of its own, shown only when moving by keyboard, and text fields no longer use the emergency red for focus (#541).
- [#559](https://github.com/CodeForFire/lagebuch/pull/559) - Enter now submits every entry form from any of its fields, including Kräfte with a FAHRZEUG picked; after the entry is added, focus goes back to the form's first field (VON in the ETB, WICHTIGKEIT in Aufgaben), and when an entry is refused, it goes to the first field that is missing; Kräfte keeps FEUERWEHR for the next unit (#540).
- [#562](https://github.com/CodeForFire/lagebuch/pull/562) - Suggestion boxes follow one keyboard model: Enter submits in one press while the list is open with nothing picked, a single match that starts with what was typed is completed inline with the added text selected so Tab or Enter takes it and Backspace drops it, and Shift+Tab never takes a match (#466).

### Fixed

- [#555](https://github.com/CodeForFire/lagebuch/pull/555) - Every dialog now takes keyboard focus when it opens, keeps Tab inside, closes on Esc and returns focus to where it was opened; the task dialog no longer leaves typing going into the ETB field behind it, and Enter on a remove confirmation cancels instead of confirming (#538).
- [#557](https://github.com/CodeForFire/lagebuch/pull/557) - The ETB edit panel, the Funktionen handover and the CO-Messung panels (add or remove a Haus, remove a Geschoss, the Wohnung editor) now take keyboard focus when they open, close on Esc and return focus to the button that opened them, and Esc on a Wohnungen spinner cancels the removal it asked about (#538).
- [#563](https://github.com/CodeForFire/lagebuch/pull/563) - Focus now stays where the Lagebuchführer left it: the rail can be arrowed through without a module taking the caret, a module's first field is focused only when it is opened by click or from a warning bar (now also in Aufgaben, Beteiligte and Links), a read-only flip keeps the open module, focus returns after a reconnect, and the ETB filter keeps the selected row (#542).
- [#566](https://github.com/CodeForFire/lagebuch/pull/566) - In the Stammdaten editor, Entfernen now asks before removing a filled row, as every other remove in the app does, and "+ HINZUFÜGEN" puts the caret in the row it adds instead of leaving focus on the button (#543).
- [#568](https://github.com/CodeForFire/lagebuch/pull/568) - The PDF export dialog opens with focus on EXPORTIEREN, so Enter exports instead of unticking the first section (#545).


## [0.7.1] - 2026-10-02

A patch release so the 0.7 line can be published on Google Play: Play automatic protection
refused the 0.7.0 bundle for its Android 6.0 floor. Nothing else changes from 0.7.0.

### Changed

- [#551](https://github.com/CodeForFire/lagebuch/pull/551) - The Android app now requires Android 7.0 (API 24), the minimum Google Play accepts for a new release, so the 0.7 line can be published there (#550).


## [0.7.0] - 2026-10-01

Stability under a large incident and the rest of the Übung feedback. The Kräfte, Funktionen,
Aufgaben, ETB and CO-Messung lists now update in place instead of being rebuilt, so a change
from anywhere in the Einsatz no longer throws away the selection, the focus or an open editor;
saves are cheaper and the Übersicht no longer probes every recent file on the UI thread.
Devices sync whenever their protocol versions overlap rather than only on identical app
versions, and joining is harder to attack and easier to use: a global PIN budget, one host
identity per install and a two-step join. Alongside that come the Kontakte module and the
BETEILIGTE tab, a Lagebuchführer handover without leaving the incident, required-field
markers, a Rückmelde timer that can restart from the ETB, a phone layout, and closing an
Einsatz straight into the PDF protocol.

### Added

- [#452](https://github.com/CodeForFire/lagebuch/pull/452) - A new Kontakte module lists the Personal Stammdaten as a searchable directory that finds a person by name, Funktion, Funkrufname, number or the new free-text Notiz, highlights why each entry matched, and hands the number or the new e-mail field to the device's phone or mail app, with both fields edited and validated in the Stammdaten editor (#451).
- [#472](https://github.com/CodeForFire/lagebuch/pull/472) - The incident header now shows who documents on this device, and a new Lagebuchführer can take over without leaving the incident, with the handover logged in the ETB and the name picked from own personnel (#469).
- [#480](https://github.com/CodeForFire/lagebuch/pull/480) - A joined device's home screen shows where it was last connected — host, Stichwort and time — with a Neu verbinden button that reopens the connect dialog with host and PIN pre-filled and a button that forgets the connection again, and while connected the incident header names the host (#464).
- [#484](https://github.com/CodeForFire/lagebuch/pull/484) - When an Aufgabe falls due, the app now says "Aufgabe, fällig" instead of playing a plain tone, and a header bar visible from every tab names the task and who it is assigned to, opens it in the Aufgaben tab on a tap, and marks it done with ERLEDIGT (#460).
- [#486](https://github.com/CodeForFire/lagebuch/pull/486) - Stammdaten vehicles and personnel can be marked as own or foreign with a new EIGEN checkbox, and the Lagebuchführer name suggestions now list own personnel only (#458).
- [#496](https://github.com/CodeForFire/lagebuch/pull/496) - A new BETEILIGTE tab keeps a per-Einsatz list of people involved who are not forces, such as the house owner, the vehicle owner or the police contact, with name, phone number and a note that sync to joined devices and appear as their own section in the PDF report, without writing an ETB line.
- [#498](https://github.com/CodeForFire/lagebuch/pull/498) - Closing an Einsatz on the desktop can now go straight on to the final PDF and open the mail program with it attached and a subject prefilled from the Einsatzdaten.
- [#499](https://github.com/CodeForFire/lagebuch/pull/499) - Required fields carry a red asterisk next to their caption, so a missing entry is visible before pressing the button, and screen readers announce them as required (#414).
- [#500](https://github.com/CodeForFire/lagebuch/pull/500) - An incident can be taken off the Home screen's ZULETZT VERWENDET list, from its row or from the banner shown when it fails to open, without touching the file (#481).
- [#504](https://github.com/CodeForFire/lagebuch/pull/504) - A Funktion can be marked in the Stammdaten as unique per Einsatz or per Abschnitt (#470), so assigning one that is already held offers the Übergabe instead of a second holder; a duplicate that arrives from an older Einsatzdatei, an import or another device is marked in the grid, with the Übergabe live on that row.
- [#506](https://github.com/CodeForFire/lagebuch/pull/506) - The Atemschutz table in the PDF report shows each Trupp's status, the time of its last Druckabfrage and its Messreihe (#426).
- [#512](https://github.com/CodeForFire/lagebuch/pull/512) - The Einsatz abschließen dialog now offers to export the PDF protocol right away, with sending it by e-mail as an optional follow-up, so a closed incident no longer leaves without a protocol (#425).
- [#519](https://github.com/CodeForFire/lagebuch/pull/519) - Links in the Stammdaten can carry an optional group such as Gefahrgut or Karten, and the LINKS tab shows them under collapsible group headers with buttons to expand or collapse them all, puts ÖFFNEN next to each link's name instead of at the far edge of the row, and tints every other row (#518).
- [#520](https://github.com/CodeForFire/lagebuch/pull/520) - The CO-Messprotokoll Struktur toolbar gains OG ENTFERNEN and UG ENTFERNEN, which remove the highest Obergeschoss or lowest Untergeschoss straight away when it is empty, ask first and name each Wohnung with recorded data when it is not, and log the removed floor with what it carried in the ETB (#443).
- [#521](https://github.com/CodeForFire/lagebuch/pull/521) - An ETB entry addressed to the Leitstelle now offers to restart the Rückmelde timer without writing the Rückmeldung a second time (#415), and the Leitstelle's name — "ILS" by default — is configurable in the Stammdaten settings and used throughout the Rückmeldung header, while the spoken reminder now says „Rückmeldung an die Leitstelle" (#400).

### Changed

- [#473](https://github.com/CodeForFire/lagebuch/pull/473) - While an incident is shared, the header shows only the PIN, and the address other devices dial now opens with a click on it; on a narrower window the header moves the Lagebuchführer, sharing and status onto a second line instead of drawing them on top of the Stichwort.
- [#476](https://github.com/CodeForFire/lagebuch/pull/476) - Leaving an open, editable incident through ÜBERSICHT, STAMMDATEN, ÖFFNEN, NEUER EINSATZ or VERBINDEN now asks first, and leaving an incident always ends its sharing, so clients no longer stay attached to a session the host has left and a new share gets a fresh PIN (#463).
- [#483](https://github.com/CodeForFire/lagebuch/pull/483) - Two devices now sync whenever their wire contracts are compatible instead of only when their app versions match exactly, so a phone still waiting on a Play Store update can join a newer desktop host, and a genuinely incompatible pair is refused with a message naming which device to update.
- [#485](https://github.com/CodeForFire/lagebuch/pull/485) - The header notifications share one design: a countdown that is only running (ILS-Rückmeldung, next Druckabfrage) is a quiet readout on one strip, and anything due or alarming becomes a row whose tile lights up amber when it needs action and red only for the Rückzugsalarm and failed saves, with the tiles aligned in one column and reduced to icons on a phone.
- [#487](https://github.com/CodeForFire/lagebuch/pull/487) - On a phone the app now lays itself out for one: the module rail becomes a bottom navigation bar with the rest behind MEHR, the command bar keeps NEUER EINSATZ and folds its other actions into an overflow, the Einsatztagebuch, Kräfte, Atemschutz, Aufgaben, Funktionen and Dateien read as one card per row instead of a grid built for the ELW monitor, each add-entry dock opens as a stacked sheet that closes again once the entry is saved, the Stammdaten editor drills into a category instead of squeezing its rail beside it, and the soft keyboard no longer covers the field being filled.
- [#495](https://github.com/CodeForFire/lagebuch/pull/495) - The ETB no longer asks for a RICHTUNG: the field is gone from the entry dock, the ETB grid and the PDF, and manually added entries are recorded as internal.
- [#501](https://github.com/CodeForFire/lagebuch/pull/501) - Joining a shared Einsatz now asks for the device and PIN first and, once the host is reached, names the Einsatz being joined and suggests the host's own personnel and Funkrufnamen for the Lagebuchführer (#459).
- [#507](https://github.com/CodeForFire/lagebuch/pull/507) - The Aufgaben field Zugeteilt suggests only Funkrufnamen and names, written "Nachname, Vorname" as everywhere else, and no longer Funktionen (#468).
- [#515](https://github.com/CodeForFire/lagebuch/pull/515) - The Übersicht no longer opens every recently used Einsatz file before it first appears; the list shows up at once and the lock marker for closed Einsätze fills in right after, without the list reordering (#291).
- [#523](https://github.com/CodeForFire/lagebuch/pull/523) - The spoken "Aufgabe fällig" cue uses a new, clearer voice recording.
- [#525](https://github.com/CodeForFire/lagebuch/pull/525) - Saving a long Einsatz is cheaper: a burst of edits writes only the latest state, and each write reuses its SQL statements and skips re-migrating a file already migrated this session (#290).
- [#528](https://github.com/CodeForFire/lagebuch/pull/528) - Checklisten sync their items from one incident change handler per list instead of one per item (#293).

### Fixed

- [#455](https://github.com/CodeForFire/lagebuch/pull/455) - A joined device no longer drops back to the start page after a brief network outage and no longer sits unnoticed on a stale Stand: it reconnects, discards out-of-order updates, checks itself against the host every ten seconds, shows in the footer whether its Stand is confirmed, and reports a change the host did not take with the host's reason, which needs host and joined devices on sync protocol 4 (#295).
- [#457](https://github.com/CodeForFire/lagebuch/pull/457) - On Windows 10 the window's title bar is now dark as well instead of white.
- [#477](https://github.com/CodeForFire/lagebuch/pull/477) - A joined device can now export the PDF from the synced Einsatzdaten but can no longer close the host's incident, which the host now refuses, and a PDF of a still-open incident is marked as Zwischenstand (#465).
- [#478](https://github.com/CodeForFire/lagebuch/pull/478) - The Android app now reports the version it was built from — `versionCode` and `versionName` were pinned in `AndroidManifest.xml`, which silently overrode the release workflow and left every published APK identifying itself as 0.1.0, build 1 — and the build now also produces the signed Android App Bundle that Google Play requires, with the APK extracted from that same bundle.
- [#488](https://github.com/CodeForFire/lagebuch/pull/488) - The Android app can now be installed on devices with a 32-bit ARM system, such as the Samsung Galaxy Tab A, which Google Play had listed as incompatible.
- [#503](https://github.com/CodeForFire/lagebuch/pull/503) - Joined devices no longer warn about a possible man-in-the-middle every time the host restarts its share or starts a new incident, because the host now keeps one key per install; a genuinely different host shows a Kennung to compare against the host's screen before it is trusted.
- [#510](https://github.com/CodeForFire/lagebuch/pull/510) - The Stammdaten editor's IMPORTIEREN button no longer stays greyed out once Stammdaten already exist; importing over existing data now asks for confirmation first, since the file replaces every category (#509).
- [#514](https://github.com/CodeForFire/lagebuch/pull/514) - Saving an edited ETB entry works again — the Speichern button in the edit panel stayed greyed out and Enter did nothing, because opening the panel never told the button to re-check (#467).
- [#516](https://github.com/CodeForFire/lagebuch/pull/516) - The Übersicht's last-connection row now matches the recent files in spacing, type and remove icon, and the intro mentions reconnecting to an Einsatz on another device.
- [#524](https://github.com/CodeForFire/lagebuch/pull/524) - The copies an attachment leaves in the system temp directory when it is opened are now deleted the next time the desktop app starts, instead of piling up there indefinitely (#383).
- [#530](https://github.com/CodeForFire/lagebuch/pull/530) - CO-Messung: an open WOHNUNG BEARBEITEN sidebar, and the value typed into it, now survives an unrelated change elsewhere in the incident, such as a new ETB line or an edit arriving from a joined device (#241).
- [#531](https://github.com/CodeForFire/lagebuch/pull/531) - Kräfte: a change saved anywhere in the Einsatz no longer clears the selected row or takes keyboard focus out of its STATUS box (#294).
- [#532](https://github.com/CodeForFire/lagebuch/pull/532) - Funktionen: a change saved anywhere in the Einsatz, or switching between current and all assignments, no longer clears the selected row in the grid (#294).
- [#533](https://github.com/CodeForFire/lagebuch/pull/533) - Aufgaben: a change saved anywhere in the Einsatz, checking off a task or switching the filter no longer clears the selected row in the list (#294).
- [#535](https://github.com/CodeForFire/lagebuch/pull/535) - Editing an ETB entry, on this device or another, no longer clears the row's selection in the journal or leaves its open history stale (#529).

### Security

- [#502](https://github.com/CodeForFire/lagebuch/pull/502) - After ten wrong share PINs across all devices the host stops accepting new joins until the Lagebuchführer draws a new PIN, which closes the way a peer with many addresses could guess the four-digit PIN, and it replaces the per-address backoff, so every wrong PIN a joining device enters counts and none is answered with a wait any more, while devices already joined keep working (#288).


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

[Unreleased]: https://github.com/CodeForFire/lagebuch/compare/v0.8.0...HEAD
[0.8.0]: https://github.com/CodeForFire/lagebuch/compare/v0.7.1...v0.8.0
[0.7.1]: https://github.com/CodeForFire/lagebuch/compare/v0.7.0...v0.7.1
[0.7.0]: https://github.com/CodeForFire/lagebuch/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/CodeForFire/lagebuch/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/CodeForFire/lagebuch/compare/v0.4.1...v0.5.0
[0.4.1]: https://github.com/CodeForFire/lagebuch/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/CodeForFire/lagebuch/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/CodeForFire/lagebuch/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/CodeForFire/lagebuch/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/CodeForFire/lagebuch/releases/tag/v0.1.0
