using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Domain.Time;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.Acceptance.Tests;

/// <summary>
/// Starts an Einsatz seeded with the two Checklisten the app shipped with, for the many tests
/// that need an incident but are not about Checklisten at all.
/// </summary>
/// <remarks>
/// <see cref="LocalIncidentSession.StartNew"/> takes <see cref="ChecklistSeed"/>s now, because
/// Checklisten are 0..n user-defined Stammdaten and nothing in the app may assume otherwise. This
/// convenience therefore lives here rather than on the session: a production caller must say
/// which lists it means, while a test about the CO-Messprotokoll should not have to.
/// </remarks>
internal static class TestSession
{
    public static LocalIncidentSession StartNew(
        IIncidentStore store,
        IClock clock,
        SessionOperator op,
        string path,
        IEnumerable<(string Text, bool IsMandatory)> aufbau,
        IEnumerable<(string Text, bool IsMandatory)> abbau,
        IncidentNumber? incidentNumber = null,
        string? keyword = null) =>
        LocalIncidentSession.StartNew(
            store, clock, op, path, Seeds(aufbau, abbau), incidentNumber, keyword);

    public static IReadOnlyList<ChecklistSeed> Seeds(
        IEnumerable<(string Text, bool IsMandatory)> aufbau,
        IEnumerable<(string Text, bool IsMandatory)> abbau) =>
        new[]
        {
            new ChecklistSeed(
                ChecklistDefaults.AufbauListId, ChecklistDefaults.AufbauTitle, aufbau.ToList()),
            new ChecklistSeed(
                ChecklistDefaults.AbbauListId, ChecklistDefaults.AbbauTitle, abbau.ToList()),
        };
}
