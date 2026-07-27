using Feuerwehr.Domain;
using Feuerwehr.Domain.ValueObjects;

namespace Feuerwehr.AppLogic.ViewModels;

// Collected in the operator popup when creating a new incident. The Einsatznummer (complete
// format) is optional and captured once at creation time.
public sealed record NewIncidentRequest(SessionOperator Operator, IncidentNumber? IncidentNumber);
