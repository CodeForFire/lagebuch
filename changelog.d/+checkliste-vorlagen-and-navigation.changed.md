Stammdaten hold 0..n named Checkliste-Vorlagen instead of a fixed Aufbau/Abbau pair, plus a
new **Navigation** category: the order of the Einsatz sidebar and which entries it shows, for
the built-in modules and every Checkliste alike. An existing `masterdata.db` is converted on
first open, and an existing Stammdaten-JSON still imports — both the `checklistTemplateAufbau`
/`checklistTemplateAbbau` keys and the older flat `checklistTemplate` array. Export now writes
the new `checklists` and `navigation` keys only, so a file exported here does not import its
Checklisten into an older build. `docs/master-data.example.json` shows the full schema.
