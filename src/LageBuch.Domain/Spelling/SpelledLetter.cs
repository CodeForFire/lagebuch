namespace LageBuch.Domain.Spelling;

/// <summary>One spelled unit: the letters as written, and the word to say for them.</summary>
/// <param name="Source">The letters as they stand in the text, e.g. "Sch" or "B".</param>
/// <param name="Word">What is said over the radio, e.g. "Schule" or "Berta"; the source itself for
/// a character the table has no word for.</param>
public sealed record SpelledLetter(string Source, string Word)
{
    /// <summary>The marker <see cref="SpellingAlphabet.Spell"/> puts between two words of the text.</summary>
    public bool IsWordBreak => Source.Length == 0;
}
