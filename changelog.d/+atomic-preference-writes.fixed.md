The four small preference files — recent incidents, last save folder, last PDF export and last
join host — are written atomically (to a temp file that is then renamed into place), the way
`trust.json` already was. A crash or a full disk mid-write used to be able to leave a truncated
file behind, which the next start silently read as "nothing remembered". (#302)
