# Master data (Stammdaten)

Dropdown contents (roles, vehicles, personnel, ...) are treated as PII and are
**never compiled into the application**. A fresh install starts with **empty**
master data; you populate it in the in-app **Stammdaten** editor by importing a
JSON file, and can write your own data back out again.

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
call signs derived from them), its roster, and its Einsatzzeiten and
Rückzugsdruck settings. The host is the master, so both devices always agree:
an Atemschutz-Trupp registered from a joined tablet gets exactly the
Einsatzzeit the host would have used.

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

## PII

Any real master-data or personnel JSON — street lists, station and call-sign
names, and above all names and mobile numbers — is personal/identifying data and
must be kept **out of the repository**. `seed-source/` and `*.masterdata.json`
are gitignored for exactly this reason; only the anonymised example and demo
files under `docs/` are tracked. An empty roster is a fully supported state:
the name field on the Funktionen tab offers the roster as suggestions but
always accepts free text, so off-roster and mutual-aid personnel can be entered
either way.
