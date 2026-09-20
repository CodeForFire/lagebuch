Stammdaten hold 0..n named Checkliste-Vorlagen instead of a fixed Aufbau/Abbau pair, and a new
**Navigation** category sets the order of the Einsatz sidebar and which entries it shows —
built-in modules and Checklisten in one list. Vorlagen are created, renamed, reordered and
deleted in the Stammdaten editor, under a **CHECKLISTEN** heading that separates the brigade's
own lists from Lagebuch's fixed categories. The ETB cannot be switched off: it is the legally
relevant record every Systemmeldung lands in. Deleting a Vorlage asks first and leaves Einsätze
already started from it untouched — an Einsatz keeps its own lists and their titles, which its
tabs and headings now carry instead of the generic "CHECKLISTE", and opening an older Einsatz
still shows every Checkliste that file holds. Einsatzdateien store their Checklisten themselves
(schema V23); an existing file is upgraded on open keeping each item's list, order and ticks,
and one whose Abbau-Checkliste was never filled no longer shows a permanently empty
ABBAU-Reiter. An existing `masterdata.db` is converted on first open, and a Stammdaten-JSON
written by an older version still imports — both the `checklistTemplateAufbau`
/`checklistTemplateAbbau` keys and the older flat `checklistTemplate` array. Export now writes
the new `checklists` and `navigation` keys only, so a file exported here does not import its
Checklisten into an older build. The Checkliste name field also moves off Avalonia's deprecated
`TextBox.Watermark` to `PlaceholderText`, so the build now fails on an obsolete API in XAML the
way it already did in C#.
