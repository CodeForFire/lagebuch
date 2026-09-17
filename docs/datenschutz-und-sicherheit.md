# Datenschutz und Sicherheit

Diese Seite beschreibt, welche personenbezogenen Daten Lagebuch verarbeitet, wo
sie gespeichert werden, was ein Gerät verlässt und was die Feuerwehr selbst
regeln muss. Sie richtet sich an Kommandantinnen und Kommandanten,
Kreisbrandinspektionen und kommunale Datenschutzbeauftragte.

Stand: Version 0.5.0, September 2026. Lagebuch ist vor Version 1.0 und
entwickelt sich schnell; maßgeblich ist diese Seite im Stand der installierten
Version. Die Darstellung ist technisch und ausdrücklich **keine
Rechtsberatung**.

## Kurzfassung

- **Kein Konto, keine Cloud, kein Server.** Lagebuch meldet sich nirgends an und
  überträgt von sich aus nichts.
- **Ein Einsatz ist eine Datei.** Die `.fwincident`-Datei liegt dort, wo sie
  gespeichert wurde, und gehört der Wehr.
- **Keine Telemetrie, kein Absturzbericht, keine Updateprüfung.** Es gibt im
  gesamten Programm keinen Aufruf, der Nutzungsdaten irgendwohin sendet.
- **Keine Logdateien.** Das Programm schreibt heute kein Protokoll auf die
  Platte – ein Absturz hinterlässt nichts, was personenbezogene Daten enthält.
- **Die Stammdaten sind nach der Installation leer.** Namen und Telefonnummern
  kommen ausschließlich durch einen selbst ausgelösten Import ins Programm.
- **Die Mehrgeräte-Verbindung ist optional** und richtet sich immer an genau das
  Gerät, dessen Adresse eingegeben wurde – TLS-gesichert, mit PIN.
- **Der Hersteller erhält keine Daten.** CodeForFire betreibt keinen Dienst, an
  den etwas fließen könnte.

## Welche Daten verarbeitet Lagebuch?

Personenbezogene Daten entstehen an zwei Stellen: in den **Stammdaten** der
Wehr, die dauerhaft auf dem Gerät liegen, und im **Einsatz** selbst, der als
einzelne Datei gespeichert wird.

| Kategorie | Enthaltene Angaben | Wo gespeichert |
|---|---|---|
| Stammdaten: Personal | Nachname, Vorname, Funktion, Funkrufname, **Telefonnummer** | `masterdata.db` |
| Stammdaten: Fahrzeuge | Wache, Funkrufname, Sitzplätze – keine Personen | `masterdata.db` |
| Funktionen im Einsatz | Name, Funkrufname, Abschnitt, **Telefonnummer**, von/bis | Einsatzdatei |
| Atemschutzüberwachung | Name jedes Truppmitglieds mit Position (Truppführer, Truppmann, 2. Truppmann), Drücke und Zeiten | Einsatzdatei |
| Einsatztagebuch (ETB) | Freitext bis 4000 Zeichen, Von/An, Richtung, „erfasst von“ | Einsatzdatei |
| ETB-Korrekturen | **jede überschriebene Fassung bleibt erhalten**, mit Zeitpunkt und Bearbeiter | Einsatzdatei |
| Einsatzdaten | Stichwort, Einsatznummer, **Straße und Ortsteil**, „abgeschlossen von“ | Einsatzdatei |
| CO-Messprotokoll | **Name der Bewohnerin oder des Bewohners je Wohnung**, ppm-Wert, Status, Schlüssel vorhanden | Einsatzdatei |
| Kräfte | Wache, Funkrufname, Stärke als Zahlen (ZF/GF/Mann, AGT), Status, Bemerkung – **keine Namen** | Einsatzdatei |
| Aufgaben | Auftrag, Zuständiger (Freitext), „angelegt von“, „erledigt von“, Zeiten | Einsatzdatei |
| Dateien und Anhänge | Fotos und PDFs, deren Originaldateiname und „hinzugefügt von“ | Ordner neben der Einsatzdatei |
| Änderungsprotokoll | wer wann welche Aktion ausgeführt hat | Einsatzdatei |

Zwei Punkte verdienen besondere Aufmerksamkeit:

**Das CO-Messprotokoll enthält Daten Dritter.** Der Bewohnername je Wohnung
betrifft Personen, die nicht der Feuerwehr angehören und die der Erfassung nicht
zugestimmt haben. Die Verarbeitung stützt sich auf die Einsatzdurchführung, und
genau dieser Teil der Einsatzdatei sollte bei der Festlegung von Löschfristen
zuerst betrachtet werden. Der Name ist ein Freitextfeld und kann leer bleiben –
die Wohnung lässt sich auch ohne ihn eindeutig über Haus, Stockwerk und Lage
kennzeichnen.

**Fotos können Zusatzinformationen tragen.** Lagebuch entfernt keine
EXIF-Metadaten aus angehängten Bildern. Ein mit dem Diensthandy aufgenommenes
Foto behält damit unter Umständen GPS-Koordinaten, Aufnahmezeit und Gerätemodell
– auch im exportierten PDF-Bericht.

### Woher die Stammdaten kommen

Eine frische Installation startet mit **leeren** Stammdaten. Es sind keine
Namen, Funkrufnamen, Wachen oder Telefonnummern im Programm enthalten; die
einzigen mitgelieferten Beispieldaten sind frei erfunden. Personenbezogene Daten
gelangen nur durch einen ausdrücklichen Import in der Stammdatenverwaltung auf
das Gerät. Diese Regel ist im Quelltext festgeschrieben und nicht bloß
Konvention. Einzelheiten zur Stammdatenpflege stehen in
[docs/master-data.md](master-data.md).

## Wo liegen die Daten?

### Die Einsatzdatei

Eine `.fwincident`-Datei ist eine **SQLite-Datenbank ohne Verschlüsselung**. Sie
liegt dort, wo sie beim Speichern abgelegt wurde – Lagebuch gibt keinen Ort vor.
Neben der Datei können betriebsbedingt Hilfsdateien mit den Endungen `-wal` und
`-shm` auftauchen.

**Anhänge liegen nicht in der Einsatzdatei**, sondern in einem Ordner daneben,
der genauso heißt wie die Datei, mit der Endung `.files`:

```
Einsatz-20260917.fwincident     ← Einsatzdaten, ETB, Atemschutz, CO-Messung …
Einsatz-20260917.files/         ← die angehängten Fotos und PDFs
```

Wer einen Einsatz kopiert, archiviert oder weitergibt, muss **beides**
mitnehmen. Wer einen Einsatz löscht, muss beides löschen. Die Dateien im
`.files`-Ordner tragen technische Namen, nicht die ursprünglichen Dateinamen;
der Originalname steht in der Einsatzdatei.

### Die Programmdaten auf dem Gerät

Alles Weitere liegt in einem einzigen Ordner:

| Plattform | Ordner |
|---|---|
| Windows | `%AppData%\Lagebuch\` |
| Linux | `~/.config/Lagebuch/` |
| macOS | `~/.config/Lagebuch/` |

Unter macOS ist das bewusst `~/.config` und nicht `~/Library/Application
Support`; dorthin löst der verwendete Standardpfad unter Unix auf.

| Datei oder Ordner | Inhalt |
|---|---|
| `masterdata.db` | die Stammdaten der Wehr, **einschließlich Namen und Telefonnummern** |
| `recent.json` | die zuletzt geöffneten Einsatzdateien mit vollständigem Pfad |
| `last-save-folder.json` | der zuletzt zum Speichern verwendete Ordner |
| `last-pdf-export.json` | der zuletzt für den PDF-Export verwendete Ordner |
| `last-join-host.json` | die zuletzt erfolgreich verbundene Gegenstelle |
| `trust.json` | die gemerkten Zertifikat-Fingerabdrücke der Gegenstellen |
| `attachment-cache/` | Kopien der Anhänge, die ein **verbundenes** Gerät vom Gastgeber geladen hat |

Beim Öffnen eines Anhangs legt Lagebuch zusätzlich eine Arbeitskopie im
temporären Verzeichnis des Systems an, unterhalb von `lagebuch/`.

### Android

Die Android-App speichert ausschließlich im app-eigenen Bereich: nichts auf der
SD-Karte, nichts in frei zugänglichen Ordnern. Einsatzdateien, Stammdaten, der
Anhang-Zwischenspeicher und die Exportdateien liegen dort. **Beim
Deinstallieren der App entfernt Android alles davon.**

## Was das Gerät verlässt

Von selbst: nichts. Es gibt im gesamten Programm genau drei Wege, auf denen
Daten das Gerät verlassen können, und alle drei werden von der Bedienerin oder
dem Bediener ausgelöst:

1. **Die Mehrgeräte-Verbindung.** Sie richtet sich an genau die Adresse, die im
   Verbindungsdialog eingegeben wurde. Einzelheiten im nächsten Abschnitt.
2. **Angeklickte Links.** Wetter, Karten oder Hydrantenplan aus den Stammdaten
   werden an den Standardbrowser des Systems übergeben. Lagebuch selbst ruft
   die Seite nicht ab.
3. **Der PDF-Bericht.** Er wird dorthin geschrieben, wo der Speicherdialog es
   vorgibt. Was danach mit ihm geschieht, liegt bei der Wehr – er enthält den
   vollständigen Einsatz einschließlich der eingebetteten Anhänge.

Nicht vorhanden sind: Nutzungsstatistik, Absturzberichte, Updateprüfung,
Werbe- oder Analysebibliotheken, Schriftarten oder Karten von fremden Servern.

**Lagebuch schreibt heute keine Protokolldateien.** Das ist für den Datenschutz
angenehm – es entsteht kein zweiter Ort mit personenbezogenen Daten –, hat aber
eine Kehrseite: Nach einem Absturz gibt es nichts, was man zur Fehlersuche
einreichen könnte. Sollte sich das in einer späteren Version ändern, wird es
hier beschrieben.

## Die Mehrgeräte-Verbindung im Detail

Ein Gerät kann einen Einsatz **gastgeben**, andere Geräte treten ihm bei. Der
Gastgeber ist die Quelle der Wahrheit; es gibt keinen dritten Rechner und keinen
Dienst dazwischen.

**Transport.** Fester Port 5859, ausschließlich HTTPS (TLS 1.2 oder 1.3, je nach
Betriebssystem). Einen unverschlüsselten Zugang gibt es nicht.

**Zertifikat.** Für jede Freigabe wird ein eigenes Zertifikat erzeugt: ECDSA
P-256, SHA-256, rund 24 Stunden gültig. Es wird nie auf die Festplatte
geschrieben und beim Beenden der Freigabe verworfen.

**Vertrauen.** Beim ersten Verbinden merkt sich das beitretende Gerät den
SHA-256-Fingerabdruck der Gegenstelle in `trust.json`. Weicht der Fingerabdruck
später ab, bricht die Verbindung mit einer Meldung ab. Eine Möglichkeit, ein
beliebiges Zertifikat zu akzeptieren, gibt es nicht.

**PIN.** Der Beitritt verlangt eine vierstellige PIN, die für jede Freigabe neu
und kryptografisch zufällig erzeugt, nur im Arbeitsspeicher gehalten und nie
gespeichert wird. Sie wird bei jeder Anfrage mitgeschickt. Falsche Eingaben
bremst der Gastgeber je Gegenstelle exponentiell aus, bis zu einer Minute
Wartezeit.

**Was übertragen wird.** Nach jeder Änderung erhält jedes verbundene Gerät den
vollständigen Stand des Einsatzes: Einsatzdaten, das gesamte ETB einschließlich
der aufbewahrten Korrekturfassungen, Funktionen **mit Namen und
Telefonnummern**, Kräfte, Atemschutztrupps mit den Namen der Truppmitglieder,
Aufgaben, CO-Messprotokoll, Änderungsprotokoll und die Liste der Anhänge.
Anhänge selbst werden nur auf Anforderung übertragen (höchstens 25 MB je Datei).

**Der Gastgeber gibt seine Stammdaten weiter.** Damit beide Geräte dieselben
Fahrzeuge, Funkrufnamen und Einsatzzeiten verwenden, überträgt der Gastgeber
seinen **vollständigen Stammdatensatz – einschließlich der Personalliste mit
Namen und Telefonnummern** – an jedes beitretende Gerät. Das beitretende Gerät
speichert diesen Satz **nicht** dauerhaft; er gilt nur für die Dauer der
Sitzung, und danach arbeitet das Gerät wieder mit seinen eigenen Stammdaten.
Wichtig für die Bewertung: Die Personalliste ist ein dauerhafter Datenbestand
der ganzen Wehr, kein einzelnes Ereignis – eine weitergegebene PIN öffnet also
mehr als den Einsatz, für den sie ausgegeben wurde.

**Anhänge auf dem beitretenden Gerät.** Geladene Anhänge bleiben im Ordner
`attachment-cache/` liegen, auch nachdem die Verbindung beendet wurde. Er wird
derzeit nicht automatisch aufgeräumt und gehört deshalb auf die Löschliste
weiter unten.

**Reichweite.** Der Gastgeber nimmt Verbindungen auf allen Netzwerkschnittstellen
des Geräts an; eine Einschränkung auf bestimmte Adressbereiche findet nicht
statt. Die im Programm angezeigte Adresse ist nur ein Vorschlag zum Weitersagen.
Praktisch heißt das: **Erreichbarkeit im Netz plus PIN ist die gesamte
Zugangskontrolle.** Im gedachten Einsatzfall – die eigenen Geräte im eigenen
WLAN oder über Tailscale – ist das angemessen; in einem offenen Netz ist es das
nicht.

**Android** kann beitreten, aber nicht gastgeben. Die App ist ein Begleitgerät.

## Technische und organisatorische Maßnahmen

### Was Lagebuch mitbringt

- Datenhaltung ausschließlich lokal, ohne Konto und ohne Serverdienst.
- Transportverschlüsselung mit TLS und Fingerabdruck-Bindung an die
  Gegenstelle, ohne Rückfallebene auf eine ungeprüfte Verbindung.
- PIN-Pflicht beim Beitritt, mit Bremse gegen systematisches Durchprobieren.
- Anhänge nur als JPEG, PNG, GIF, WebP oder PDF; Dateiname und Dateityp werden
  geprüft, damit sich keine ausführbare Datei als Bild ausgeben kann.
  Dateinamen werden entschärft, Pfadangaben entfernt, die Länge begrenzt.
- Beim Öffnen eines Anhangs prüft das Programm, dass es sich um eine echte
  Datei im eigenen Arbeitsverzeichnis handelt, bevor es sie an das
  Betriebssystem übergibt.
- Nachvollziehbare Änderungshistorie: Korrekturen im ETB überschreiben nichts.
- Keine Telemetrie, keine Protokolldateien, keine Drittanbieter-Dienste.
- Überprüfbare Installationsdateien: zu jedem Release gehören SHA-256-Prüfsummen
  und ein Sigstore-Herkunftsnachweis, siehe
  [Downloads prüfen](../README.md#downloads-prüfen).

### Was die Wehr selbst regeln muss

- **Festplattenverschlüsselung** des ELW-Laptops und der Tablets. Die
  Einsatzdatei und `masterdata.db` sind unverschlüsselt; der Schutz der Daten im
  Ruhezustand ist Aufgabe des Betriebssystems (BitLocker, LUKS, FileVault,
  Android-Geräteverschlüsselung).
- **Bildschirmsperre und Zugang zum Gerät.** Wer am Laptop sitzt, sieht die
  Stammdaten.
- **Ablage und Archivierung** der Einsatzdateien – immer zusammen mit dem
  zugehörigen `.files`-Ordner –, einschließlich Sicherung und Zugriffsschutz auf
  dem Ablageort.
- **Löschfristen** für Einsatzdateien, PDF-Berichte und die Personalliste
  festlegen und umsetzen; siehe [Aufbewahren und Löschen](#aufbewahren-und-löschen).
- **Umgang mit dem PDF-Bericht**, der den vollständigen Einsatz enthält:
  Versandweg, Empfängerkreis, Ablage.
- **Weitergabe der PIN** im Einsatz: an wen, und dass sie nicht über den
  Einsatz hinaus verwendet wird.
- **Auswahl der Netzverbindung** für den Mehrgerätebetrieb: eigenes WLAN oder
  Tailscale, nicht das offene Gäste-WLAN der Einsatzstelle.

## Einordnung nach DSGVO

Die folgenden Punkte sollen die Bewertung erleichtern; sie ersetzen die Prüfung
durch die oder den zuständigen Datenschutzbeauftragten nicht.

**Verantwortlicher** im Sinne von Art. 4 Nr. 7 DSGVO ist die Feuerwehr
beziehungsweise ihr Träger, in der Regel die Kommune. Die Entscheidung über
Zwecke und Mittel der Verarbeitung trifft die Wehr, nicht CodeForFire.

**Keine Auftragsverarbeitung.** CodeForFire stellt ein Programm zur Verfügung
und betreibt keinen Dienst. Es gibt keine Schnittstelle, über die
personenbezogene Daten an CodeForFire gelangen könnten, und folglich keine
Verarbeitung im Auftrag nach Art. 28 DSGVO. **Ein
Auftragsverarbeitungsvertrag ist daher nicht erforderlich** – es gibt keinen
Auftragsverarbeiter. Auch eine Drittlandsübermittlung findet nicht statt.

**Rechtsgrundlage.** Die Einsatzdokumentation ist für Feuerwehren regelmäßig
eine gesetzlich vorgesehene Aufgabe; in Bayern ergibt sie sich aus dem
Bayerischen Feuerwehrgesetz und dem Satzungsrecht des Trägers. Welche
Rechtsgrundlage konkret herangezogen wird, bestimmt der Träger.

**Verzeichnis von Verarbeitungstätigkeiten (Art. 30 DSGVO).** Die Bausteine
dafür stehen auf dieser Seite:

| Angabe im Verzeichnis | Abschnitt hier |
|---|---|
| Kategorien betroffener Personen und Daten | [Welche Daten verarbeitet Lagebuch?](#welche-daten-verarbeitet-lagebuch) |
| Speicherorte | [Wo liegen die Daten?](#wo-liegen-die-daten) |
| Empfänger und Übermittlungen | [Was das Gerät verlässt](#was-das-gerät-verlässt) |
| Technische und organisatorische Maßnahmen | [Technische und organisatorische Maßnahmen](#technische-und-organisatorische-maßnahmen) |
| Löschfristen | [Aufbewahren und Löschen](#aufbewahren-und-löschen) – die Fristen selbst legt der Träger fest |

**Betroffenenrechte in der Praxis.** Auskunft und Berichtigung erfolgen über die
Einsatzdatei und die Stammdaten; Löschung bedeutet, die betreffenden Einträge
oder die Datei zu entfernen. Eine Besonderheit ist ausdrücklich zu benennen:
**Korrekturen im Einsatztagebuch überschreiben die Vorfassung nicht**, sondern
bewahren sie mit Zeitpunkt und Bearbeiter auf. Das ist für die
Dokumentationsintegrität gewollt – ein Einsatztagebuch, dessen Einträge sich
spurlos ändern lassen, wäre als Nachweis wertlos. Berichtigung heißt dort also
Ergänzung, nicht Überschreiben. Wer eine Vorfassung tatsächlich entfernen muss,
entfernt den Eintrag.

## Aufbewahren und Löschen

Lagebuch hat **keine Funktion „alle Daten löschen“**, und eine Einsatzdatei
lässt sich auch nicht aus dem Programm heraus löschen. Das Entfernen geschieht
im Dateimanager des Systems. Vollständig ist es erst, wenn folgende Punkte
abgearbeitet sind.

**Windows, Linux, macOS**

1. Jede `.fwincident`-Datei, an allen Orten, an denen gespeichert wurde
   (Anhaltspunkte liefern `recent.json` und `last-save-folder.json`), samt
   etwaiger `-wal`- und `-shm`-Dateien.
2. Zu jeder Einsatzdatei den gleichnamigen `.files`-Ordner.
3. Aus dem Programmordner (`%AppData%\Lagebuch\` beziehungsweise
   `~/.config/Lagebuch/`):
   `masterdata.db`, `recent.json`, `last-save-folder.json`,
   `last-pdf-export.json`, `last-join-host.json`, `trust.json` und den Ordner
   `attachment-cache/`.
4. Exportierte PDF-Berichte an ihren Ablageorten (den zuletzt verwendeten nennt
   `last-pdf-export.json`).
5. Den Ordner `lagebuch` im temporären Verzeichnis des Systems.

Danach ist der Programmordner selbst entbehrlich; Lagebuch legt ihn beim
nächsten Start leer wieder an.

**Android**

Die App deinstallieren. Damit entfernt Android sämtliche oben genannten Daten,
weil alles im app-eigenen Bereich liegt.

**Wichtig:** Sicherungskopien, Netzlaufwerke und USB-Sticks, auf denen
Einsatzdateien abgelegt wurden, erfasst das nicht. Sie gehören in das
Löschkonzept der Wehr.

## Bekannte Grenzen

Vollständigkeit ist hier wichtiger als ein guter Eindruck.

- **Die Installationspakete sind noch nicht signiert.** Betriebssysteme warnen
  deshalb beim ersten Start. Die SignPath Foundation hat das Projekt abgelehnt;
  eine Bewerbung bei OSSign ist frühestens im Februar 2027 möglich, weil dort
  sechs Monate Projektbestand verlangt werden. Bis dahin sind Prüfsumme und
  Herkunftsnachweis der belastbare Ersatz. Einzelheiten in der
  [Roadmap](../ROADMAP.md).
- **Die erste Verbindung zu einer Gegenstelle ist ungeprüft.** Der Fingerabdruck
  wird beim ersten Kontakt übernommen und ab dann erzwungen; er wird im Programm
  bislang nicht angezeigt, kann also beim ersten Mal nicht mit dem Gastgeber
  abgeglichen werden. Wer in diesem Moment im selben Netz mitspielt, könnte sich
  dazwischenschalten. Verbesserung ist geplant
  ([Issue #288](https://github.com/CodeForFire/lagebuch/issues/288)).
- **Die PIN hat vier Stellen.** Zusammen mit der Bremse gegen Durchprobieren
  reicht das gegen zufälliges Raten, nicht gegen einen entschlossenen Angreifer
  mit Zeit und Netzzugang. Sechs Stellen sind Teil desselben Issues.
- **Erreichbarkeit plus PIN ist die gesamte Zugangskontrolle.** Wer beides hat,
  darf am Einsatz alles ändern.
- **Der Name in „Wer dokumentiert?“ wird nicht überprüft.** Er dient der
  Zuordnung in der Anzeige, nicht der Berechtigung. Ein verbundenes Gerät kann
  einen beliebigen Namen angeben.
- **EXIF-Daten in Fotos werden nicht entfernt** – siehe oben. Das Entfernen
  beim Anhängen ist geplant
  ([Issue #384](https://github.com/CodeForFire/lagebuch/issues/384)).
- **Der Anhang-Zwischenspeicher eines beitretenden Geräts wird nicht
  automatisch geleert**
  ([Issue #382](https://github.com/CodeForFire/lagebuch/issues/382)); dasselbe
  gilt für die Arbeitskopien im temporären Verzeichnis
  ([Issue #383](https://github.com/CodeForFire/lagebuch/issues/383)). Beide
  stehen deshalb auf der Löschliste oben.
- **Das Dateiformat ist vor Version 1.0 nicht eingefroren.** Ältere Dateien
  werden beim Öffnen migriert; ab 1.0 gilt die Zusage, dass eine Datei dauerhaft
  lesbar bleibt.
- **Es gibt keine Protokolldateien** – zum Schutz der Daten gut, zur Fehlersuche
  nach einem Absturz schlecht.

Diese Punkte stehen in englischer Sprache und aus der Sicht eines
Sicherheitsforschers auch in [SECURITY.md](../SECURITY.md).

## Fragen und Meldungen

- Fragen zum Datenschutz, zur Installation oder zum Betrieb im ELW gerne unter
  [Fragen & Antworten](https://github.com/CodeForFire/lagebuch/discussions/categories/fragen-antworten).
- **Sicherheitslücken bitte nicht öffentlich melden**, sondern vertraulich über
  [SECURITY.md](../SECURITY.md).
- Fehler oder Lücken in dieser Seite: bitte
  [ein Issue öffnen](https://github.com/CodeForFire/lagebuch/issues/new/choose).
  Eine Seite, der ein Datenschutzbeauftragter nicht vertrauen kann, nützt
  niemandem.
