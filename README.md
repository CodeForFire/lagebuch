# Lagebuch

Desktop application for fire-brigade incident documentation (Einsatzdokumentation).
Replaces a legacy macro-enabled Excel template with a robust, offline, cross-platform
.NET 8 + Avalonia application.

## Status

Under construction. See plans under `docs/` (note: design/plan artifacts are not committed).

## Build & Test

```bash
dotnet build
dotnet test
```

## Run

```bash
dotnet run --project src/Feuerwehr.App/Feuerwehr.App.csproj
```

## Master data

Dropdown contents (roles, radio call signs, streets, ...) are seeded from
`src/Feuerwehr.Persistence/seed-source/master-data.json` into a local
`masterdata.db` on first start.

Seeding fills a category only while its table is still empty, so **changes to
the seed file do not reach an existing installation**. To pick them up, delete
the database and let it be recreated:

| Platform | Path |
|---|---|
| Windows | `%AppData%\Lagebuch\masterdata.db` |
| Linux   | `~/.config/Lagebuch/masterdata.db` |

This is a deliberate pre-release simplification — there is no master-data
versioning or in-app editor yet.

### Personnel roster

Names and mobile numbers are the only personal data in the seed, so they live in
a separate file that is **never committed**:

```
src/Feuerwehr.Persistence/seed-source/personnel.json   (gitignored)
```

Copy `personnel.example.json` next to it, rename it, and replace the entries
with the CLS export. The format is one array of people:

```json
{
  "personnel": [
    {
      "lastName": "Mustermann",
      "firstName": "Max",
      "role": "ZF",
      "callSign": "Land 1",
      "phone": "01 71 / 1 23 45 67"
    }
  ]
}
```

`lastName` and `firstName` must both be present — `firstName` may be `null`, but
leaving the key out entirely fails the load. `role`, `callSign` and `phone` may
each be `null` or omitted.

The file is embedded into the build **only if it exists**, so a fresh clone and
CI compile and run without it — the app simply starts with an empty roster. That
is a supported state, not a misconfiguration: the name field on the Funktionen
tab offers the roster as suggestions but always accepts free text, so off-roster
and mutual-aid personnel can be entered either way.

Because the roster is seeded like every other category, the same caveat above
applies: adding people only reaches an installation whose `masterdata.db` does
not yet have them.
