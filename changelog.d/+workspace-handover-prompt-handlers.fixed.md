Handing the workspace view from one incident to another no longer lets the first incident's
"weiter bearbeiten" prompt act on the second. The view kept its handlers on the abandoned
prompt, and those handlers followed the view rather than the incident they belonged to, so
cancelling the old prompt closed the dialog the operator was actually looking at. Both the
workspace and the main view now detach from a prompt before wiring up the next one. (#302)
