Groundwork for gesprochene Ansagen: Lagebuch can now turn its own German into German a speech
engine reads correctly, and there is a way to pick the voice by ear. Funkrufnamen go digit by digit
the way they do on the radio — „Florian Musterstadt 40/1“ wird zu „vier null, eins“, mit „zwo“ für
die Zwei — while a Stärke like `0/1/8/9` stays four counts, because those digits are quantities and
not an identifier. Uhrzeiten, Datumsangaben, Geschosse (`EG–2. OG`), Wohnungen und die Abkürzungen
aus dem Einsatzalltag (ILS, CSA, LPA, PA, AGT, DLK, EL, ZF, GF …) werden ausgeschrieben oder
buchstabiert. `make audition` rendert die Kandidatenstimmen über echte Sätze aus dem
Einsatztagebuch und schreibt eine Seite zum Anhören; die Stimmen selbst liegen nicht im Repository,
sondern holt `packaging/speech/fetch-voices.sh` anhand fester SHA-256-Prüfsummen. Es spricht noch
nichts — das ist der nächste Schritt.
