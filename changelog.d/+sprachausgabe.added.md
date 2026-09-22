Die Einsatz-Cues sagen jetzt, **wen** sie meinen. Statt nur „Rückzugsalarm“ kommt „Rückzugsalarm.
Florian Musterstadt 40 1, Trupp 1 (Angriffstrupp). Rückzugsdruck erreicht, 45 bar“ — der
Funkrufname zuerst, weil jedes Fahrzeug seinen eigenen „Trupp 1“ stellt. Ebenso bei der
Druckabfrage; eine fällige Aufgabe nennt die Aufgabe. Gesprochen wird offline auf dem Gerät, ohne
Netz und ohne dass Einsatzdaten das Gerät verlassen. Ist keine Stimme installiert oder scheitert
die Erzeugung, ertönen weiterhin die mitgelieferten Ansagen — ein Cue geht nie verloren.

Damit das verständlich klingt, wird der geschriebene Text fürs Sprechen aufbereitet: Funkrufnamen
werden als Zahlen zusammenhängend gesprochen („40/1“ → „vierzig eins“, führende Nullen entfallen,
die Zwei bleibt das Funk-„zwo“), Abkürzungen aus dem Einsatzalltag buchstabiert (ILS, CSA, LPA, PA,
AGT, DLK, ELW, LF, FFB, CO, ppm), Geschosse als Ordinalzahl gebeugt („im zweiten Obergeschoss“,
aber „…, zweites Obergeschoss, Wohnung 1“), Uhrzeiten und Datumsangaben ausgeschrieben. Fugen-s
werden getrennt, damit aus „Angriffstrupp“ kein „Angriffschtrupp“ wird. Fürs Ohr darf die Ansage
kürzer sein als der Eintrag — „Stärke 0/1/8/9“ wird zu „mit 9 Mann“, ein doppelter Wachenname
entfällt. Das Einsatztagebuch selbst bleibt unverändert: es ist der Nachweis.

Die Stimme (Thorsten, CC0) liegt beim Programm und lädt erst beim ersten Cue, damit der Start nicht
wartet. `make audition` rendert Kandidatenstimmen über echte Sätze aus dem Einsatztagebuch und
schreibt eine Seite zum Anhören, samt Synthesezeit je Zeile — die Wartezeit zwischen Alarm und
erstem Ton ist das, was bei einem Alarm zählt.
