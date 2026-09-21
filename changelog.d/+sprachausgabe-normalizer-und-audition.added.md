Groundwork for gesprochene Ansagen: Lagebuch can now turn its own German into German a speech
engine reads correctly, and there is a way to pick the voice by ear. Funkrufnamen werden als Zahlen
gesprochen, so wie sie über Funk angesagt werden — „Florian Musterstadt 40/1“ wird zu „vierzig,
eins“, und eine alleinstehende Zwei bleibt das Funk-„zwo“. Eine Stärke wie `0/1/8/9` liest sich
genauso, Gruppe für Gruppe. Uhrzeiten, Datumsangaben, Geschosse (`EG–2. OG`), Wohnungen und die Abkürzungen
aus dem Einsatzalltag (ILS, CSA, LPA, PA, AGT, DLK, EL, ZF, GF …) werden ausgeschrieben oder mit
deutschen Buchstabennamen buchstabiert — „Ih Ell Ess“ statt eines Kürzels, das die Engine sonst
ins Englische zieht. `make audition` rendert die Kandidatenstimmen über echte Sätze aus dem
Einsatztagebuch und schreibt eine Seite zum Anhören; die Stimmen selbst liegen nicht im Repository,
sondern holt `packaging/speech/fetch-voices.sh` anhand fester SHA-256-Prüfsummen. Es spricht noch
nichts — das ist der nächste Schritt.
