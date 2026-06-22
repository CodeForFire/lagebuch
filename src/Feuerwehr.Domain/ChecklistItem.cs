namespace Feuerwehr.Domain;

public sealed class ChecklistItem
{
    public ChecklistItem(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Checklistentext darf nicht leer sein.", nameof(text));
        Id = Guid.NewGuid();
        Text = text.Trim();
    }

    public Guid Id { get; }
    public string Text { get; }
    public bool IsDone { get; private set; }
    public string? Note { get; private set; }

    public void Toggle() => IsDone = !IsDone;

    public void SetNote(string? note) =>
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
