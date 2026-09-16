using LageBuch.Documents.Sections;
using LageBuch.Domain.Files;

namespace LageBuch.Documents;

/// <summary>
/// One attached file scheduled for the report appendix: the disk path of its PDF plus the file-row
/// metadata that <see cref="AttachmentCaptionSection"/> renders as the caption page merged in front
/// of it.
/// </summary>
public sealed record PdfAttachmentToMerge(string Path, IncidentFile File);