# Play-Store-Eintrag — de-DE

Vorlage für den Haupteintrag im Play Console. Die Grenzen stehen dabei, weil die
Console nicht warnt, sondern abschneidet. `make play-listing-check` zählt nach.

Was hier **nicht** stehen darf, steht in
[`docs/releasing.md`](../releasing.md#what-the-listing-must-not-say) — kurz: nichts,
was amtlichen Status nahelegt, keine Alarmierung, keine Sicherheitsversprechen und
keine Desktop-Funktionen, die die Android-App nicht hat.

---

## App-Name (max. 30 Zeichen)

```
Lagebuch – Einsatztagebuch
```

## Kurzbeschreibung (max. 80 Zeichen)

```
Einsatzdokumentation für den ELW. Offline, ohne Cloud, ohne Konto. Open Source.
```

## Vollständige Beschreibung (max. 4000 Zeichen)

```
Lagebuch ist das digitale Einsatztagebuch für den Einsatzleitwagen: Einsatzdaten, ETB, Kräfte, Atemschutzüberwachung, Aufgaben und CO-Messprotokoll — offline, ohne Cloud, ohne Konto und ohne Abo.

WAS DIE ANDROID-APP KANN

Einen Einsatz vollständig anlegen und führen — und sich zusätzlich mit einem Einsatz verbinden, der auf einem Laptop im selben Netz freigegeben wurde. Zwei Dinge macht nur die Desktop-Version: einen Einsatz für andere Geräte freigeben und den PDF-Bericht erzeugen. Die Desktop-Version für Windows, Linux und macOS gibt es kostenlos auf GitHub.

FÜR WEN

• ELW-Besatzung und Führungsassistenten, die im Einsatz mitschreiben und Kräfte führen
• Atemschutzüberwachung mit Druckabfragen, Countdown und Erinnerung zum Rückzug
• Wehren ohne Lizenzgebühren, ohne Server und ohne IT-Abteilung

FUNKTIONEN

• Einsatzdaten — Stichwort, Einsatznummer, Adresse
• ETB — Einsatztagebuch mit Richtung, Systemeinträgen und nachvollziehbarer Korrekturhistorie
• Kräfte — Fahrzeuge, Stärke als ZF/GF/Mann, Zahl der Atemschutzgeräteträger, Status
• Aufgaben — mit Wichtigkeit, Dringlichkeit, Zuständigem und Timer
• Funktionen — Einsatzleitung, Abschnittsleitung und weitere Rollen mit Zeitraum und Übergabe
• Atemschutzüberwachung — Einstiegsdruck, Countdown, Druckabfrage, Erinnerung zum Rückzug
• CO-Messprotokoll — Haus, Stockwerk und Wohnung mit ppm-Wert und Status
• Kontakte — die Personal-Stammdaten durchsuchbar, mit Telefonnummer und E-Mail
• Checklisten — beliebig viele eigene, aus den Stammdaten
• Dateien und Links — Fotos und PDFs am Einsatz, Schnellzugriff auf Wetter und Karten
• Mehrere Geräte — TLS-gesichert und mit PIN, im lokalen Netz, ohne Server

DATENSCHUTZ

Kein Konto, keine Anmeldung, keine Cloud, kein Server. Keine Telemetrie, keine Absturzberichte, keine Updateprüfung, keine Werbe- oder Analysebibliotheken. Die Stammdaten sind nach der Installation leer; Namen und Telefonnummern kommen ausschließlich durch einen selbst ausgelösten Import ins Programm.

Alle Daten liegen im app-eigenen Bereich und werden beim Deinstallieren von Android entfernt. Der Hersteller erhält keinerlei Daten — CodeForFire betreibt keinen Dienst, an den etwas fließen könnte.

Die ausführliche Fassung für Kommandantinnen, Kommandanten und kommunale Datenschutzbeauftragte: https://codeforfire.github.io/datenschutz/

OPEN SOURCE

MIT-Lizenz. Quelltext, Änderungswünsche und alle Versionen:
https://github.com/CodeForFire/lagebuch

Lagebuch wird von CodeForFire entwickelt — Feuerwehrleuten aus Bayern, die im Einsatz selbst damit arbeiten. Rückmeldungen aus der Praxis sind ausdrücklich erwünscht.

Lagebuch ist eine private Anwendung zur Dokumentation und steht in keiner Verbindung zu einer Behörde. Es alarmiert nicht, stellt keine Verbindung zu einer Leitstelle her und ersetzt keine vorgeschriebene Dokumentation.
```

---

## Weitere Felder

| Feld | Wert |
|---|---|
| Kategorie | Produktivität |
| Kontakt-E-Mail | *(vom Play-Konto)* |
| Website | `https://codeforfire.github.io/` |
| Datenschutzerklärung | `https://codeforfire.github.io/datenschutz/` |

## Hinweise für die Prüfung (App-Zugriff → Anleitung)

Die App wird ohne Anmeldung geprüft; dieser Text gehört in das Feld
*App access instructions*, damit die Prüfung nicht am Verbindungsdialog hängen
bleibt und die App als funktionslos bewertet:

```
Die App benötigt kein Konto und keine Anmeldung. Sie startet auf der Startseite;
über "Neuer Einsatz" lässt sich sofort ein Einsatz anlegen und vollständig
bearbeiten (ETB, Kräfte, Atemschutz, Aufgaben, CO-Messung, Kontakte).

Die Funktion "Einsatz beitreten" verbindet die App optional mit einem Einsatz, der
auf einem Laptop im selben lokalen Netzwerk freigegeben wurde (TLS, vierstellige
PIN). Ohne einen solchen Gastgeber ist nur diese eine Funktion nicht prüfbar —
alle übrigen Funktionen sind ohne Netzwerk und ohne Gegenstelle vollständig
nutzbar.

Es werden keinerlei Daten an den Entwickler übertragen; es existiert kein Server.

Quelltext (MIT): https://github.com/CodeForFire/lagebuch
```
