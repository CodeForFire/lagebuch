A failing once-a-second update no longer takes the others down with it. The Atemschutz, Aufgaben
and ILS-Erinnerung timers share one ticker, which called each subscriber without isolation — so
one that threw skipped everything after it in that tick and surfaced as an unhandled UI
exception. Each is now called on its own and a failure is reported instead of swallowed. The
tick also no longer allocates a fresh subscriber array every second. (#302)
