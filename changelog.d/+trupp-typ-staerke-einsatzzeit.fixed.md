Atemschutz: a Trupp-Typ now carries its own Stärke and Einsatzzeit in the Stammdaten, instead
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
