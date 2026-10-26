# Master data (Stammdaten)

Dropdown contents (roles, vehicles, personnel, ...) are treated as PII and are
**never compiled into the application**. A fresh install starts with **empty**
master data; you populate it in the in-app **Stammdaten** editor by importing a
JSON file, and can write your own data back out again.

## Trupp-Typen carry their own rules

Each Trupp-Typ row holds the crew size (**Stärke**, 2 or 3) and the **Einsatzzeit** the
Atemschutz form suggests when that type is picked. A CSA-Trupp is three people on a
shorter clock because its Stammdaten row says so — not because the app recognises the
word "CSA". Rename it, add a `Chemietrupp` of your own, or give a `Sicherheitstrupp` a
longer Einsatzzeit, and the rules follow the row.

A Trupp is never fewer than two people and never more than three: the
Atemschutzüberwachung has exactly three positions (Truppführer, Truppmann,
2. Truppmann) and nowhere to write a fourth name.

There is no separate list of brigades (Wachen) or radio call signs
(Funkrufnamen): both are derived from the **vehicles** — every vehicle's Wache
becomes a brigade suggestion, and every vehicle's call sign (plus every roster
person's call sign) becomes a call-sign suggestion. Maintaining the vehicle
list is all that is needed; every field still accepts free text for anything
not in it (a Leitstelle, a mutual-aid unit).

## Textbausteine

`textSnippets` is one list, **Textbausteine** in the Stammdaten editor. Each
Textbaustein says which areas offer it, with one checkbox per area:

| Checkbox | JSON | Offered in |
|---|---|---|
| ETB | `etb` | the ETB entry, the ETB edit panel |
| AUFGABEN | `tasks` | the Aufgaben dock, the Aufgabe dialog, the Aufgabe edit panel |

What a Funker reports ("Einsatzstelle erreicht, Erkundung läuft") is usually
ticked for the ETB only, and what the Lagebuchführer gives out ("Energieversorger
informieren") for Aufgaben only. A phrase both use ("Polizei verständigen") is
ticked for both and kept once. A missing `etb` or `tasks` reads as `true`, so a
hand-written entry that names no area is offered everywhere. One ticked for
neither is kept but offered nowhere. More areas will come as further keys.

Each entry has a `shortcut` (the Kürzel), a `text` and an optional `category`.
Typing `::` in a field lists the Textbausteine ticked for its area; the letters
after `::` narrow the list — Kürzel first, then any whose text or Kategorie
contains them. ↑/↓ choose, Enter or Tab insert, Esc closes the list and leaves
the typed text alone. A tap inserts on a phone.

`[…]` in a text marks a gap: after inserting, the first one is selected, so
`::lgb` followed by `Zimmerbrand 2. OG` reads `Lage bestätigt: Zimmerbrand 2. OG`.
(On Android the caret simply follows the text, because the soft keyboard and
a programmatic selection do not get along.)

A Kürzel is stored lower case, letters and digits only, at most 12 characters;
`::STL`, `stl` and `s-t-l` are the same Kürzel. Each Kürzel exists once in the
list, whichever areas it is ticked for. The editor names a duplicate instead of
saving it, and an import keeps the first.
A text is cut to 1000 characters, what an Aufgabe may hold. The inserted text
is plain text in the entry: changing a Textbaustein later changes no entry.

## Objekte

`premises` is the list **Objekte** in the Stammdaten editor: buildings in the
Ausrückebereich whose structure is known long before the call, such as a
recurring Wohnanlage. Each Objekt has a `name`, an optional address (`street`,
`houseNumber`, `postalCode`, `place`) and the structure a Haus in the
CO-Messprotokoll starts with:

| Field | JSON | Range | Missing reads as |
|---|---|---|---|
| Geschosse | `floors` | 1–50 | 8 |
| Wohnungen/Geschoss | `apartmentsPerFloor` | 1–30 | 10 |
| Untergeschosse | `undergroundFloors` | 0–3 | 1 |

`apartmentLabels` names Wohnungen, keyed `"floor:apartment"`: `"0:1"` is the
first Wohnung in the EG, `"2:3"` the third in the 2. OG and `"-1:1"` the first
in the 1. UG. The editor shows them as a grid of every floor and Wohnung under
**WOHNUNGEN BENENNEN**. A Wohnung without a label keeps the name the CO matrix
gives it (`Links`/`Mitte`/`Rechts` on a floor of three, `Whg. N` otherwise). A
label is cut to 60 characters, and a key that names no floor or Wohnung of the
Objekt is dropped.

Typing in **BEZEICHNUNG** in the CO-Messprotokoll's **HAUS HINZUFÜGEN** dialog
suggests the Objekte. Picking one fills Geschosse, Wohnungen/Geschoss and
Untergeschosse, unless one of them was already changed by hand, and the Haus is
created with the Objekt's Wohnungsbezeichnungen. These values are **copied**
into the Einsatz. Editing or deleting the Objekt later changes no Haus already
in an Einsatzdatei. A name that matches no Objekt adds a Haus exactly as before.

The address is not copied, because a Haus in the CO-Messprotokoll has none. It
tells two Objekte apart in the list. Out-of-range numbers are clamped, rows
without a name are skipped, a second Objekt of the same name (ignoring case) is
dropped, and a list holds at most 500 Objekte.
## Funkgruppen and the Führungstafel slot of a Funktion

`talkGroups` lists the Funkgruppen a Führungsstelle works on: each has a `name`, an
optional `number`, a `kind` (`"tmo"` or `"dmo"`, TMO when missing or unknown) and an
optional `note`. A name is required and at most 200 characters. The editor shows them in
a **Funkgruppen** section, and an Einsatz copies the text it uses, so renaming or
deleting a group here never changes a past Einsatz.

A role may carry an optional `slot` — `leiter`, `melder`, `fuehrungsassistent` or
`fachberater` — the post on the Führungstafel its holder fills. A missing or unknown
slot means none.

## Kontakte: die Notiz ist das Suchfeld

A personnel row carries an optional `email` and an optional `note` besides the
name, role, call sign and phone number. The **Kontakte** tab in the Einsatz rail
lists the roster and searches all of them at once.

`note` is free text the brigade maintains, and in practice it holds one of two
things: a Fachgebiet (`KBM Gefahrgut, Fachberater Arbeits- und
Gesundheitsschutz`, `Fachberater EDV`, `PSNV-E Team`) or the Feuerwehren someone
covers (`Alling, Biburg, Eichenau, Emmering`). That is what makes the tab able
to answer "wer ist zuständig für ABC?" — the search splits the query on spaces
and keeps a person when every term matches somewhere, so `gefahrgut`,
`kbm gefahrgut` and `gefahrgut kbm` all find the same person.

Telefonnummer and E-Mail-Adresse open the device's dialer and mail app.
Lagebuch never places a call itself: the dialer comes up prefilled and the
operator still presses call. Both values are validated before they reach the
platform, because they arrive from an imported file rather than from the
operator's keyboard — an address carrying a line break or a `?bcc=`, or a number
carrying a USSD code, is refused rather than launched.

Both fields are optional everywhere. A roster that has neither still works, and
a person without an address simply shows no mail button.

The Stammdaten editor checks an address against the same rules the launcher
applies, and names anyone whose address could not be opened instead of letting
SPEICHERN write it — the operator who typed it is the one who can still fix it.
The same check runs right after an import, which is where an unusable address
most often comes from.

## Straßen und Ortsteile aus OpenStreetMap

The **Ortsteile** (`districts`) and **Straßen** (`streets`) are offered as
suggestions in the STRASSE and ORTSTEIL fields of the Einsatzdaten. Picking a
street fills in its Ortsteil, PLZ and Ort, but only into fields that are still
empty, and only where every row of that street agrees. A street that runs through
two Ortsteile is two rows, and picking it leaves ORTSTEIL for the
Lagebuchführer. Both lists are suggestions, never a closed set: an address that
is not listed is still recorded as typed.

A Gemeinde has hundreds of streets, so nobody types them in. `make osm-streets`
fetches them from OpenStreetMap through the Overpass API:

```bash
# districts and streets as a JSON fragment
make osm-streets AGS=09179121 OUT=strassen.json

# or replace them inside an exported Stammdaten file, then IMPORTIEREN it
make osm-streets AGS=09179121 MERGE=meine.masterdata.json
```

`AGS` is the eight-digit *amtlicher Gemeindeschlüssel* (09179121 is
Fürstenfeldbruck). It identifies the Gemeinde unambiguously, where a name would
not: several Gemeinden are called Puch. Outside Germany, pass the boundary's OSM
relation id as `RELATION=` instead. `MERGE` rewrites only `districts` and
`streets` and leaves the rest of the file as it was. Two runs over unchanged map
data give a byte-identical file, so a regenerated list only shows a diff where
the map changed.

What the list contains is decided by what OpenStreetMap holds:

- **Street names** come from named roads. An address only confirms a name that a
  road already has, because `addr:street` tags carry typos. A Weiler that is
  addressed by its own name („Rothschwaig 1") is listed as a street too.
- **PLZ and Ort** come from the street's own addresses, and otherwise from the
  Gemeinde, if it has only one PLZ.
- **Ortsteile** are the Gemeinde's `place` nodes (village, hamlet, suburb …).
  Which Ortsteil a *street* is in is filled in only where its addresses say so
  (`addr:suburb`, `addr:hamlet`). That is rare: in Fürstenfeldbruck it is the
  streets of Puch and Aich. OpenStreetMap has no Ortsteil boundaries to test a
  street against, and a nearest-point guess would put wrong Ortsteile into an
  Einsatz, so the tool does not guess. Fill in the rest in the **Straßen**
  editor, or in your own generator.

The data is © OpenStreetMap contributors, available under the
[ODbL 1.0](https://www.openstreetmap.org/copyright). Keep that attribution with
any file you pass on.

## Sample data

[`samples/demo-stammdaten.json`](samples/demo-stammdaten.json) is a complete,
fictional set (two Wachen, seven vehicles, a small roster, checklists, links)
for trying the app out. [`samples/uebung.lagebuch`](samples/uebung.lagebuch)
is a matching fictional incident. Both are regenerated with `make samples`;
the round-trip test in `tests/LageBuch.Persistence.Tests/DemoIncidentTests.cs`
keeps them valid as the schema evolves.

## Checklisten und die Navigation

Checklisten sind Stammdaten: Sie legen beliebig viele an — keine, zwei, fünf —
geben jeder einen Namen, und jeder neue Einsatz startet mit einer eigenen Kopie
davon. Ein späteres Umbenennen oder Löschen einer Vorlage rührt einen laufenden
oder abgeschlossenen Einsatz nicht an; der trägt seine Listen selbst.

Die Kategorie **Navigation** bestimmt, was die Seitenleiste eines Einsatzes
zeigt und in welcher Reihenfolge: die eingebauten Module und jede Checkliste in
einer Liste, jeweils ein- oder ausschaltbar. Das **ETB ist immer sichtbar** und
lässt sich nicht abschalten — es ist die rechtlich relevante Aufzeichnung, und
jede Systemmeldung landet dort. Verschieben lässt es sich sehr wohl.

Eine leere Navigation bedeutet „Standard": die Reihenfolge, mit der Lagebuch
ausgeliefert wird. Das ist Absicht — so taucht ein Modul, das eine spätere
Version ergänzt, bei allen auf, die die Liste nie angefasst haben.

**Funktionen** und **Kräfte** sind keine eigenen Einträge mehr, sondern Reiter
von **Führung** — neben der Führungsorganisation. Eine früher gespeicherte
Navigation, die sie noch einzeln nennt, zeigt Führung an der Stelle des ersten
der drei Einträge; eingeblendet ist es, wenn einer von ihnen eingeblendet war.
Beim nächsten Speichern der Navigation steht es so auch in den Stammdaten.

Beim Öffnen eines älteren Einsatzes gilt: Was die Navigation nennt und der
Einsatz hat, wird gezeigt; was sie nennt, der Einsatz aber nicht hat, entfällt;
und eine Checkliste, die der Einsatz hat, die Navigation aber nicht kennt, wird
hinten angehängt. Eine archivierte Einsatzdatei zeigt damit immer alle ihre
Listen.

## Where it is stored

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

## Joined devices use the host's master data

When you join another device's incident ("Mit Gerät verbinden"), that device's
master data is used for the whole session — its vehicles (and the brigades and
call signs derived from them), its roster, its Trupp-Typen with their
Stärke and Einsatzzeit, and its Rückzugsdruck. The host is the master, so both
devices always agree: an Atemschutz-Trupp registered from a joined tablet gets
exactly the crew size and Einsatzzeit the host would have used.

The join flow itself never reads or writes your own `masterdata.db` — it just
isn't consulted while you're joined. (You can still open **Stammdaten** and
edit it deliberately; that has no effect on the joined session, which keeps
using the host's set.) Leaving the incident returns the device to its own
master data — there is nothing to back up and nothing to restore. If the host
has no master data at all, the joined device shows empty dropdowns too; every
field still accepts free text.

## Import and export

Open **Stammdaten** and use the header buttons:

- **IMPORTIEREN** — offered only while the data is still empty (a first-run
  bootstrap, not a merge). Pick a JSON file; its contents load into the editor as
  unsaved changes for review, and reach `masterdata.db` only when you press
  **SPEICHERN** (or **VERWERFEN** to discard).
- **EXPORTIEREN** — writes the current master data (including unsaved edits) to a
  JSON file you can back up or hand to another install.

The file is one JSON object; every top-level key is optional, so a file may hold
the whole set, only the roster, or anything in between. See
[`master-data.example.json`](master-data.example.json) for the full schema.
A file exported by an older version may still carry `brigades` and
`radioCallSigns` lists; those keys are ignored, and any entry in them that no
vehicle or roster person covers is listed in a notice after the import so you
can add a vehicle for it before saving.

Such a file also has `truppTypes` as a plain list of names, from before the
Stärke and Einsatzzeit moved onto the row. It still imports: each name becomes a
two-person Trupp-Typ on the standard Einsatzzeit, except `CSA-Trupp` and
`LPA-Trupp`, which get the values the old hard-coded rule gave them (3 / 20 min
and 2 / 60 min). Your existing `masterdata.db` is migrated the same way, once,
the first time this version opens it — so nothing changes for a brigade using
the shipped spellings, and a brigade that had renamed the type gets the row it
can now correct itself.

`roles` carries how often a Funktion may be held: `multiple` (the default, so
any number of people may hold it), `uniquePerIncident` (one holder for the whole
Einsatz, whatever Abschnitt either of them is in) or `uniquePerSection` (one per
Abschnitt; an empty Abschnitt counts as a bucket of its own). A file exported
before this version has `roles` as a plain list of names, from before the
setting moved onto the row. It still imports, and every name comes
back as `multiple`. Your existing `masterdata.db` is widened the same way, the
first time this version opens it — the new column is simply absent before, and
absent reads as `multiple` too. Nothing is marked for you: a Funktion that may
not be held twice has to be marked in the editor afterwards.

## PII

Any real master-data or personnel JSON — street lists, station and call-sign
names, and above all names, mobile numbers and e-mail addresses — is
personal/identifying data and
must be kept **out of the repository**. `seed-source/` and `*.masterdata.json`
are gitignored for exactly this reason; only the anonymised example and demo
files under `docs/` are tracked. An empty roster is a fully supported state:
the name field on the Funktionen tab offers the roster as suggestions but
always accepts free text, so off-roster and mutual-aid personnel can be entered
either way.

[`datenschutz-und-sicherheit.md`](datenschutz-und-sicherheit.md) is the German
page for the people who have to sign this off — it covers every category of
personal data the app holds, all of the storage locations above, what the
multi-device connection transmits, and the full deletion checklist.
