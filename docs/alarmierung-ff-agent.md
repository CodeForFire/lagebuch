# Einsatz aus der Alarm-Mail anlegen (FF-Agent)

Lagebuch kann eine Alarmierung aus FF-Agent übernehmen. Liegt die Alarm-Mail in einem Ordner auf dem
PC, bietet die Startseite den Einsatz an:

> **ALARM EMPFANGEN** · THL 2 T2917 · Hauptstraße 12, 12345 Musterstadt Nord · alarmiert 17:17

**EINSATZ ANLEGEN** fragt wie gewohnt nach dem Lagebuchführer und dem Speicherort. Danach ist der
Einsatz schon ausgefüllt:

- **Einsatzdaten:** Einsatznummer, Stichwort und Adresse. Kennen die Stammdaten die Straße, kommen
  Ortsteil, PLZ und Ort von dort.
- **Lagekarte:** der Einsatzort aus den Koordinaten der Leitstelle.
- **ETB:** ein erster Eintrag „Alarmierung 17:17: …“ mit Einsatztyp, Meldebild, Hinweis und den
  alarmierten Einsatzmitteln.
- **Kräfte:** die angehakten Einsatzmittel. Vorangehakt sind alle, deren Funkrufname einem Fahrzeug
  aus den Stammdaten entspricht; sie übernehmen Wache, Fahrzeugtyp und Sitzplätze von dort.
  Kürzen die Funkrufnamen den Ort ab („FFB 2/40/1“ statt „Florian Fürstenfeldbruck 2/40/1“), in
  den Stammdaten unter **Alarmierung** den Ort und sein Kürzel eintragen: `Fürstenfeldbruck` →
  `FFB`. Ein Eintrag genügt für alle Fahrzeuge dieses Orts.

**VERWERFEN** legt keinen Einsatz an. Dieser Alarm wird danach nicht mehr angeboten.

Läuft gerade ein Einsatz, unterbricht ein neuer Alarm nicht. Oben in der Leiste steht nur
„Neuer Alarm: … – auf der Startseite anlegen“.

## Was Lagebuch liest – und was nicht

Lagebuch liest den `<einsatz>`-Block, den FF-Agent mit „Alarm Webhook und Mail“ verschickt. Er darf
als gespeicherte Mail (`.eml`), als Mail-Text (`.html`/`.htm`) oder als reines XML (`.xml`) im
Ordner liegen.

- Aus dem Ordner angeboten werden nur Alarme der letzten **2 Stunden**, gemessen an der
  Alarmierungszeit in der Mail. Ältere Dateien bleiben unbeachtet. Fehlt die Alarmierungszeit,
  zählt, wann die Datei gespeichert wurde: Ein alter Alarm, der eben erst im Ordner gelandet ist,
  wird dann noch angeboten.
- **ALARM-DATEI ÖFFNEN** bietet eine einzelne gespeicherte Alarm-Mail an, egal wie alt. So lässt
  sich ein späterer Alarm von Hand übernehmen oder die Übernahme ausprobieren. Die Mail lässt sich
  dazu z. B. aus Outlook im Web als `.eml` speichern.
- Eine Datei darf höchstens **250 KB** groß sein, sonst meldet Lagebuch „Keine Alarmierung …
  gefunden“. Eine gespeicherte Mail mit Anhängen ist schnell größer; dann nur den Mail-Text als
  `.html` speichern.
- **Mitteiler** und **Patient** werden nie gelesen und landen nicht im Lagebuch.
- Die Dateien im Ordner werden nur gelesen. Lagebuch verschiebt und löscht nichts.
- Ein Probealarm ist als **PROBEALARM** gekennzeichnet.

## Einrichten

Der Ordner wird pro PC auf der Startseite unter **ALARMIERUNG → ORDNER WÄHLEN** festgelegt. Die
Funktion gibt es nur in der Desktop-App, nicht auf Android.

Lagebuch greift nicht selbst auf Outlook oder das Postfach zu. Etwas anderes muss die Alarm-Mail
in den Ordner legen. Mit Microsoft 365 geht das ohne Programmierung über einen
**Power-Automate-Flow**:

1. **Ordner anlegen:** in OneDrive (z. B. auf office.com → OneDrive → *Neu → Ordner*) einen
   Ordner `Alarmierung` anlegen. Erst dann lässt er sich im Flow auswählen und auf dem ELW-PC
   lokal halten.
2. **Auslöser:** *Beim Eintreffen einer neuen E-Mail (V3)* im Postfach, das die Alarm-Mails
   bekommt. Bei einem freigegebenen Postfach heißt der Auslöser *Beim Eintreffen einer neuen
   E-Mail in einem freigegebenen Postfach (V2)*.
   - *Von*: `do-not-reply@ff-agent.com`
   - *Betrefffilter*: der Marker, den FF-Agent in den Betreff setzt, z. B. `#FFA-LB#`.
   - *Anlagen einschließen*: Nein
3. **Aktion:** *OneDrive for Business → Datei erstellen*.
   - *Ordnerpfad*: z. B. `/Alarmierung`.
   - *Dateiname*: als Ausdruck
     `concat(formatDateTime(utcNow(), 'yyyyMMdd-HHmmss'), '-', guid(), '.html')`.
     Die Nachrichten-ID der Mail taugt nicht: Sie enthält `<` und `>`, die OneDrive in
     Dateinamen ablehnt.
   - *Dateiinhalt*: der *Text* (Body) der Mail, also das HTML mit dem Alarm darin.
4. Auf dem ELW-PC mit demselben Konto bei OneDrive anmelden und den Ordner per Rechtsklick auf
   **Immer auf diesem Gerät behalten** stellen, damit die Dateien wirklich lokal liegen.
5. In Lagebuch diesen lokalen Ordner als Alarm-Ordner wählen, z. B.
   `C:\Users\<Name>\OneDrive - <Organisation>\Alarmierung`.

Die Aktionen *Office 365 Outlook* und *OneDrive for Business* sind Standard-Connectoren. In den
üblichen Microsoft-365-Plänen braucht der Flow weder eine Premium-Lizenz noch die Zustimmung eines
Administrators.

Weil OneDrive beim Hochfahren nachsynchronisiert, wird auch eine Alarm-Mail angeboten, die
eingetroffen ist, als der PC noch aus war.
