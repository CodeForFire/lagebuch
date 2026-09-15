A joined device's attachment cache is now capped at 500 MB instead of growing without limit.
Every attachment pulled from the host was written to disk and nothing ever deleted it — not
leaving the incident, not joining the next one — so a tablet used across many Einsätze kept
every attachment of every one of them. Once a newly cached file pushes the cache over the cap,
the oldest entries are removed until it is back under. (#302)
