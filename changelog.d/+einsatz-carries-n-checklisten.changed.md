An Einsatz now carries 0..n Checklisten instead of exactly two. Internally each list is its
own object with an id and a title, taken from the Stammdaten template it was seeded from, and
the incident keeps that title itself — so a list can be named anything, and renaming or
deleting a template never disturbs an Einsatz already under way. The ETB entry logged when a
list's Pflichtpunkte are all ticked now names the list ("Checkliste Aufbau abgeschlossen: alle
Pflichtpunkte erledigt" is unchanged for the two lists that came before). No user-visible
change yet: the app still seeds exactly Aufbau and Abbau.
