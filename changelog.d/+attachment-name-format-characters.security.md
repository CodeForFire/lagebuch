Attachment names are stripped of invisible formatting characters, not just control characters.
U+202E and its relatives are `UnicodeCategory.Format`, so they survived the old filter: they
render as nothing but reverse the text after them, which let `Lageplan‮gnp.exe` appear as
"Lageplan exe.png" in the Dateien list and the PDF while still being an executable. Such a name
is now rejected on the way in, and one already stored is neutralised on load. (#302)
