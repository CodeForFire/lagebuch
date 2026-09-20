Übersicht: the decorative glow in the top-right corner no longer renders as a grey rectangle
with hard edges. It faded to `Transparent` — which is transparent *white*, so the gradient
drifted through grey — and stopped fading at 70%, cutting the 320×320 box mid-gradient; its
bright end also pointed into the middle of the page instead of at the corner. It is now a
radial fade anchored on the corner, ending on a fully transparent stop of the same signal
colour (a new `SignalFadedColor` token, so the two stops cannot drift apart). (#376)
