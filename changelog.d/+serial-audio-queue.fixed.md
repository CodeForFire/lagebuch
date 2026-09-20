Alarm cues could be delayed or silently skipped while the app was busy. Each cue ran on a
thread-pool thread, and a pool saturated by other work hands out threads only as fast as it
grows them — so a cue could sit unplayed for seconds, or outlast its own hung-player watchdog
without ever having started. Alarms fire exactly when the app is busiest, which is precisely
when this bit. Cues now get a dedicated thread each and no longer queue behind unrelated work.
