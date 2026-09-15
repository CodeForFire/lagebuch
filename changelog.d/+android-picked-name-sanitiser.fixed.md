Android: a file picked from another app is now cleaned up the same way an attachment name from a
joined device already was. The two had grown apart — the picked-name path stripped only the
characters the running OS rejects, so on Android a name could keep `< > : " | ? *`, invisible
formatting characters, or run past the 255-byte limit every filesystem enforces, and only broke
once the file reached a Windows peer. Both now share one sanitiser. (#302)
