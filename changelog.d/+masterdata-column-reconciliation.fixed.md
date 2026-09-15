Stammdaten: every repairable column in `masterdata.db` is now restored on open, and a test
sweeps all of them so the next one cannot be forgotten. The file has no schema version, so it
reconciles itself on every open — but that reconciliation was three hand-written lines, and a
column added to the schema without a matching line would have broken every *existing* store
while a fresh one looked fine. `md_personnel`'s optional columns had no such line; no shipped
version ever lacked them, so nothing was broken in practice, but nothing would have caught it
either. (#397)
