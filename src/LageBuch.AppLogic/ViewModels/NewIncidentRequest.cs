using LageBuch.AppLogic.Services.Dispatch;
using LageBuch.Domain;

namespace LageBuch.AppLogic.ViewModels;

// Collected in the operator popup when creating a new incident: only who documents. Neither the
// Einsatznummer (#69) nor the Stichwort is asked for here -- every head datum (Stichwort,
// Einsatznummer, Adresse) is entered afterwards through the workspace's Einsatzdaten dialog,
// unless the incident is taken up from an alarm, which brings them along.
public sealed record NewIncidentRequest(SessionOperator Operator, DispatchImport? Dispatch = null);
