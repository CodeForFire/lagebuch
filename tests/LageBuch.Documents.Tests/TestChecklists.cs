using LageBuch.Domain;

namespace LageBuch.Documents.Tests;

/// <summary>
/// Seeds the two Checklisten the app shipped with, for tests that predate user-defined lists and
/// are about something else (closing an Einsatz, the ETB entry on completion).
/// </summary>
/// <remarks>
/// Both lists are always produced, empty ones included, so an index into
/// <see cref="Incident.Checklists"/> means what it used to. The 0..n behaviour itself is covered
/// by the ChecklistListTests in LageBuch.Domain.Tests.
/// </remarks>
internal static class TestChecklists
{
    public static IReadOnlyList<ChecklistSeed> Pair(
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
