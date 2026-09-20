Einsatzdateien now store their Checklisten themselves (schema V23): a new `checklist_lists`
table holds each list's name, and every item points at the list it belongs to. An existing
file is upgraded on open — its Aufbau and Abbau items keep their list, their order and their
ticks. One visible change: a file whose Abbau-Checkliste was never filled no longer shows a
permanently empty ABBAU-Reiter, because the upgrade only creates a list where items exist.
