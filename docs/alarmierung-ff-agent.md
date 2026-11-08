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
   bekommt.
   - Filter *Betreff*: der Marker, den FF-Agent in den Betreff setzt, z. B. `#FFA-LB#`.
2. **Aktion:** *OneDrive for Business → Datei erstellen*.
   - *Ordnerpfad*: z. B. `/Alarmierung`.
   - *Dateiname*: die *Nachrichten-ID* der Mail mit der Endung `.html`.
   - *Dateiinhalt*: der *Text* (Body) der Mail.
3. Den OneDrive-Ordner auf dem ELW-PC synchronisieren und in Lagebuch als Alarm-Ordner wählen.

Weil OneDrive beim Hochfahren nachsynchronisiert, wird auch eine Alarm-Mail angeboten, die
eingetroffen ist, als der PC noch aus war.
