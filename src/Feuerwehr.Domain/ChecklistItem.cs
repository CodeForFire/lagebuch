namespace Feuerwehr.Domain;

public sealed class ChecklistItem
{
    public ChecklistItem(string text, bool isMandatory)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Checklistentext darf nicht leer sein.", nameof(text));
        Id = Guid.NewGuid();
        Text = text.Trim();
        IsMandatory = isMandatory;
    }

    private ChecklistItem(Guid id, string text, bool isDone, string? note, bool isMandatory)
    {
        Id = id;
        Text = text;
        IsDone = isDone;
        Note = note;
        IsMandatory = isMandatory;
    }

    public static ChecklistItem Rehydrate(Guid id, string text, bool isDone, string? note, bool isMandatory)
        => new(id, text, isDone, note, isMandatory);

    public Guid Id { get; }
    public string Text { get; }
    public bool IsDone { get; private set; }
    public string? Note { get; private set; }
    public bool IsMandatory { get; }

    public void Toggle() => IsDone = !IsDone;

    public void SetNote(string? note) =>
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
