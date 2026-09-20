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

## Sample data

[`samples/demo-stammdaten.json`](samples/demo-stammdaten.json) is a complete,
fictional set (two Wachen, seven vehicles, a small roster, checklists, links)
for trying the app out. [`samples/uebung.fwincident`](samples/uebung.fwincident)
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

## PII

Any real master-data or personnel JSON — street lists, station and call-sign
names, and above all names and mobile numbers — is personal/identifying data and
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
