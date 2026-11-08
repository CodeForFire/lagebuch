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

**VERWERFEN** legt keinen Einsatz an. Dieser Alarm wird danach nicht mehr angeboten.

Läuft gerade ein Einsatz, unterbricht ein neuer Alarm nicht. Oben in der Leiste steht nur
„Neuer Alarm: … – auf der Startseite anlegen“.

## Was Lagebuch liest – und was nicht

Lagebuch liest den `<einsatz>`-Block, den FF-Agent mit „Alarm Webhook und Mail“ verschickt. Er darf
als Mail-Text (`.html`/`.htm`) oder als reines XML (`.xml`) im Ordner liegen.

- Angeboten werden nur Alarme der letzten **2 Stunden**. Ältere Dateien bleiben unbeachtet.
- **Mitteiler** und **Patient** werden nie gelesen und landen nicht im Lagebuch.
- Die Dateien im Ordner werden nur gelesen. Lagebuch verschiebt und löscht nichts.
- Ein Probealarm ist als **PROBEALARM** gekennzeichnet.

## Einrichten

Der Ordner wird pro PC auf der Startseite unter **ALARMIERUNG → ORDNER WÄHLEN** festgelegt. Die
Funktion gibt es nur in der Desktop-App, nicht auf Android.

Lagebuch greift nicht selbst auf Outlook oder das Postfach zu. Etwas anderes muss die Alarm-Mail
in den Ordner legen. Mit Microsoft 365 geht das ohne Programmierung über einen
**Power-Automate-Flow**:

1. **Auslöser:** *Beim Eintreffen einer neuen E-Mail (V3)* im Postfach, das die Alarm-Mails
   bekommt. Bei einem freigegebenen Postfach heißt der Auslöser *Beim Eintreffen einer neuen
   E-Mail in einem freigegebenen Postfach (V2)*.
   - *Von*: `do-not-reply@ff-agent.com`
   - *Betrefffilter*: der Marker, den FF-Agent in den Betreff setzt, z. B. `#FFA-LB#`.
   - *Anlagen einschließen*: Nein
2. **Aktion:** *OneDrive for Business → Datei erstellen*.
   - *Ordnerpfad*: z. B. `/Alarmierung`.
   - *Dateiname*: als Ausdruck
     `concat(formatDateTime(utcNow(), 'yyyyMMdd-HHmmss'), '-', guid(), '.html')`.
     Die Nachrichten-ID der Mail taugt nicht: Sie enthält `<` und `>`, die OneDrive in
     Dateinamen ablehnt.
   - *Dateiinhalt*: der *Text* (Body) der Mail, also das HTML mit dem Alarm darin.
3. Auf dem ELW-PC mit demselben Konto bei OneDrive anmelden und den Ordner per Rechtsklick auf
   **Immer auf diesem Gerät behalten** stellen, damit die Dateien wirklich lokal liegen.
4. In Lagebuch diesen lokalen Ordner als Alarm-Ordner wählen, z. B.
   `C:\Users\<Name>\OneDrive - <Organisation>\Alarmierung`.

Die Aktionen *Office 365 Outlook* und *OneDrive for Business* sind Standard-Connectoren. In den
üblichen Microsoft-365-Plänen braucht der Flow weder eine Premium-Lizenz noch die Zustimmung eines
Administrators.

Weil OneDrive beim Hochfahren nachsynchronisiert, wird auch eine Alarm-Mail angeboten, die
eingetroffen ist, als der PC noch aus war.
