Android: picking a file that cannot be read no longer takes the app down. The chosen file is
streamed into the app's own storage before the picker returns, and a content provider that hands
back nothing — or a full disk — threw from inside Android's result callback, where nothing was
there to catch it. Both the Stammdaten import and the attachment picker now report it as an
error line instead, and a provider that will not say what the file is called falls back to a
generic name rather than abandoning the pick. (#302)
