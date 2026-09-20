A PDF export no longer fails outright because of a single character the report font cannot
draw. Anything typed into an Einsatz reaches the export — ETB entries, Aufgaben, attachment
names — and since the QuestPDF update an unrenderable character (an emoji, a name in another
script) aborted the whole document, leaving only "Export fehlgeschlagen" and no report at all.
Such a character is now simply left blank and the export completes. The text LageBuch itself
writes is held to the stricter rule in the test suite, where a character the bundled font
lacks still fails the build.
