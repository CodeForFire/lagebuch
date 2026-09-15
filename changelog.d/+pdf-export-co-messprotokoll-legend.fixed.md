PDF export: the CO-Messprotokoll legend and the "Erledigt" marker in the Aufgaben table came
out as empty boxes on some machines. Both used characters the bundled Lato font does not
carry (■ U+25A0, ✔ U+2714), so they were quietly borrowed from whatever font the rendering
machine happened to have installed — fine on a developer desktop, blank on a slim container
or on Android, with nothing in the logs to say so. The legend now draws its three colour
swatches as real rectangles (taking their colours from the same source as the floor grid, so
the two can no longer drift apart) and a completed task is marked ● against ○ for an open
one. The export no longer depends on any font beyond the one it ships with.
