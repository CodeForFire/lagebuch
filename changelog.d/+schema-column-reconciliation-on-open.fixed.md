An Einsatzdatei that a build from another development branch had written can be opened again.
Each file records a schema version, and the app runs only the migrations numbered above it —
sound as long as that number means the same thing everywhere. A parallel branch numbers its own
migrations too, so a file it touched could come back carrying a version the released app had
never applied: the app then skipped a migration the file genuinely needed, and opening it failed
with "no such column: previous_zugfuehrer_count" and no way forward. Opening a file now also
compares its actual columns against the expected schema and adds whatever is missing, regardless
of the recorded version, so an affected file repairs itself the next time it is opened. Columns
and tables the app does not know are left untouched.
