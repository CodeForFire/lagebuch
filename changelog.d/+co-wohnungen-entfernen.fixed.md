CO-Messprotokoll: eine Wohnungszahl zu verkleinern löscht nicht mehr stumm von rechts weg. Wer
die Zahl eines Geschosses verringert, wird jetzt gefragt, *welche* Wohnungen entfallen sollen —
mit einer Liste, die zu jeder Wohnung nennt, was auf ihr steht: Messwert, Bewohner, Absuchstatus,
Schlüssel, Bezeichnung. Die übrigen rücken auf und nehmen ihre Bezeichnung, ihre Messreihe und
ihren Absuchstatus mit; im ETB steht anschließend nicht nur „3 Wohnungen entfernt", sondern welche
es waren und was sie trugen. Trägt keine der wegfallenden Wohnungen etwas, verkleinert sich das
Geschoss wie bisher ohne Rückfrage: eine frische Struktur zurechtzuziehen ist der Normalfall und
soll nicht nerven. Bisher verschwanden Bewohnername, Status, Schlüsselinfo und der komplette
Messverlauf mit einem einzigen Tastendruck — aus `14` wird getippt schnell `1` —, ohne Rückfrage
und ohne Weg zurück, während ein ganzes Haus zu entfernen längst abgesichert war (#419).

Im selben Zug ist ein zweiter, stiller Datenverlust behoben: **OG HINZUFÜGEN** und **UG
HINZUFÜGEN** — Knöpfe, die nur etwas hinzufügen — löschten auf jedem Geschoss alle Wohnungen
oberhalb der Gebäude-Vorgabe. Ein Haus mit 3 Wohnungen je Geschoss, dessen Keller auf 14 gesetzt
war, verlor beim Hinzufügen eines Obergeschosses die Wohnungen 4 bis 14 samt allen Messwerten;
das Geschoss zeigte danach „0/14" über drei Kacheln. Die geschossweisen Wohnungszahlen aus (#265)
werden jetzt überall berücksichtigt, auch beim Wiederanlegen — eine Datei, der ein früherer Stand
Wohnungen weggenommen hat, füllt sie beim nächsten Strukturwechsel wieder auf. Zusätzlich
übernimmt das Struktur-Eingabefeld `ClipValueToMinMax` wie das ppm-Feld, sodass eine Zahl über 30
nicht mehr wirkungslos verschluckt wird.
