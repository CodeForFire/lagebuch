namespace LageBuch.Domain;

/// <summary>
/// The optional PDF report body sections an operator can include on export (#262). Lives in
/// Domain (not Documents, which owns QuestPDF) so <c>LageBuch.AppLogic</c>'s
/// <c>IIncidentPdfExporter</c> can reference it without taking a QuestPDF dependency (Android has
/// no QuestPDF support — QuestPDF/QuestPDF#1432).
/// </summary>
[Flags]
public enum IncidentPdfSections
{
    None = 0,
    Checklist = 1 << 0,
    Etb = 1 << 1,
    Roles = 1 << 2,
    Forces = 1 << 3,
    Tasks = 1 << 4,
    Atemschutz = 1 << 5,
    CoMessprotokoll = 1 << 6,
    Files = 1 << 7,
    All = Checklist | Etb | Roles | Forces | Tasks | Atemschutz | CoMessprotokoll | Files,
}
