Every path is now joined with `Path.Join` instead of `Path.Combine`. `Path.Combine` silently
discards everything before an argument that turns out to be rooted, so a name that slipped
through sanitisation could point outside the folder it was meant to land in; `Path.Join` always
concatenates. Behaviour is unchanged for every existing call — each one passes a relative later
argument — and untrusted names still go through `IncidentFile.SanitizeFileName` or
`SafeFileName.Sanitize` first. Seven dead local assignments went with it, one of them in
`EqualWidthWrapPanel`'s arrange pass: `IDE0059` ships at suggestion severity, below the
threshold `TreatWarningsAsErrors` acts on, so they had accumulated unnoticed; it is now a
warning and therefore a build error, so the next one cannot.
