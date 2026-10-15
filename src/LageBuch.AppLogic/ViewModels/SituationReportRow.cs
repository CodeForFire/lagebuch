using LageBuch.Domain;
using LageBuch.Domain.SituationReports;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>One Lagemeldung in the list: "Nr. 2 · 14:32" once sent, "Entwurf" before.</summary>
public sealed class SituationReportRow
{
    public SituationReportRow(SituationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Id = report.Id;
        IsSent = report.IsSent;
        Title = report.Number is { } number ? $"Nr. {number}" : "Entwurf";
        TimeText = Formatting.TimeOfDay(report.SentAt ?? report.CreatedAt);
        Summary = report.Funkspruch.Length > 0 ? report.Funkspruch : "— noch leer —";
    }

    public Guid Id { get; }

    public bool IsSent { get; }

    public string Title { get; }

    public string TimeText { get; }

    /// <summary>The Funkspruch, for the list's second line.</summary>
    public string Summary { get; }
}
