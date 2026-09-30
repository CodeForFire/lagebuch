Saving a long Einsatz is cheaper: a burst of edits writes only the latest state, and each write reuses its SQL statements and skips re-migrating a file already migrated this session (#290).
