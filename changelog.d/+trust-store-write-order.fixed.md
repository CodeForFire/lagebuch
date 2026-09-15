A failed write to `trust.json` no longer leaves the running app trusting a host certificate the
file does not record. Accepting or resetting a host's certificate updated memory first and wrote
afterwards, so if the write failed (full disk, permissions) the session kept connecting happily
while the next start re-prompted for the same host. The write now happens first and is only
adopted once it lands, and a retry after a failed reset does the work instead of silently
skipping it. (#302)
