CO-Messprotokoll: every Wohnung now keeps a Messreihe instead of a single overwritten number.
Each reading is stored with its time and the crew that took it, the editor sidebar lists them
newest first with the same severity colouring the tiles use, the tile tooltip carries the run
from two readings up, and the CO section of the PDF prints the Verlauf under the Wohnung —
`Messreihe: 08:14 120 ppm · 08:41 40 ppm · 09:02 5 ppm`. Clearing a value is recorded as the
correction it is rather than erasing the reading that was there, and a changed reading is no
longer invisible: measurements used to go to the ETB as system messages, which it hides by
default, so the entry only existed behind a checkbox nobody knew about and crews reported the
change as undocumented. They now travel as their own kind of entry ("Messung") — shown by
default, still not hand-editable and still kept out of the direction picker — while the
bookkeeping around them stays hidden as intended. The WOHNUNG BEARBEITEN sidebar also scrolls
now, with ABBRECHEN/FERTIG anchored to its bottom edge: it had no scroll region at all, so at
the app's own minimum window height of 600 FERTIG was cut off by 15px and a Wohnung simply
could not be saved — a crew could type a ppm value and have no way to commit it, and Android,
where the minimum height does not apply, was worse. The Messreihe itself is capped at about six
rows and scrolls within itself, so a Wohnung measured all shift no longer pushes the
Bezeichnung and Bewohner fields out of view. Incident files written before this open unchanged
with an empty Messreihe; no history is invented for them.
