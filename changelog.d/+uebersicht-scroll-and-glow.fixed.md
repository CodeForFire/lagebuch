Übersicht: the page no longer jumps sideways while it is scrolled, the "Zuletzt verwendet" list
no longer has a scrollbar of its own, and the decorative glow in the top-right corner no longer
renders as a grey rectangle with hard edges. The content column was sized to its widest child,
and that child is the virtualized list, whose measured width changes as rows are realized and
recycled — so a recent entry with a long path made the column 720px wide and scrolling it out
of view snapped the whole page down to ~643px and back; the column is now pinned to the
viewport width (capped at 720) regardless of what the list happens to be showing. Nested inside
the page's scroller the list swallowed the wheel until it hit its own bottom, so it moved
several rows before the page moved at all; it now sizes to its (at most ten) entries and the
page is the only thing that scrolls. The glow faded to `Transparent` — which is transparent
*white*, so the gradient drifted through grey — and stopped fading at 70%, cutting the 320×320
box mid-gradient, with its bright end pointing into the middle of the page instead of at the
corner. It is now a radial fade anchored on the corner, ending on a fully transparent stop of
the same signal colour (a new `SignalFadedColor` token, so the two stops cannot drift apart).
(#376)
