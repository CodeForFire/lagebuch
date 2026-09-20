Übersicht: the page no longer jumps sideways while it is scrolled. The content column was
sized to its widest child, and that child is the virtualized "Zuletzt verwendet" list, whose
measured width changes as rows are realized and recycled — so a recent entry with a long path
made the column 720px wide and scrolling it out of view snapped the whole page down to ~643px
and back. The column is now pinned to the viewport width (capped at 720) regardless of what
the list happens to be showing.
