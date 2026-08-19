using Feuerwehr.Domain;

namespace Feuerwehr.AppLogic.ViewModels;

// Collected in the operator popup when creating a new incident. The Einsatznummer is unknown at
// this point (#69) — only the Stichwort (dispatch's short keyword) is captured, and only if given;
// the Einsatznummer can be added later, from the workspace header.
public sealed record NewIncidentRequest(SessionOperator Operator, string? Keyword);
