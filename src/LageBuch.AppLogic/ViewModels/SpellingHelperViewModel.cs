using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain.Spelling;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Buchstabier-Helfer (#592): a word in, its spelling for the radio out, plus the whole table
/// for the alphabet chosen. Docked in the Lagemeldung module and opened as an overlay from the ETB;
/// both use this one view model.
/// </summary>
public sealed partial class SpellingHelperViewModel : ObservableObject
{
    public const int MaxInputLength = 200;

    public SpellingHelperViewModel()
    {
        TableRows = SpellingAlphabet.Table(Alphabet);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Spelled))]
    [NotifyPropertyChangedFor(nameof(HasSpelling))]
    private string? _input;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Spelled))]
    [NotifyPropertyChangedFor(nameof(IsTraditional))]
    [NotifyPropertyChangedFor(nameof(IsDin5009))]
    [NotifyPropertyChangedFor(nameof(IsNato))]
    private SpellingAlphabetKind _alphabet = SpellingAlphabetKind.Traditional;

    /// <summary>The spelling as it is read out: "Berta – Ludwig – Ulrich / eins – drei".</summary>
    public string Spelled => SpellingAlphabet.Format(SpellingAlphabet.Spell(Input, Alphabet));

    public bool HasSpelling => !string.IsNullOrWhiteSpace(Input);

    /// <summary>The chosen alphabet's table, the way a Buchstabierkarte prints it.</summary>
    [ObservableProperty]
    private IReadOnlyList<SpelledLetter> _tableRows;

    // One flag per alphabet for the view's radio buttons; setting one to true selects it.
    public bool IsTraditional
    {
        get => Alphabet == SpellingAlphabetKind.Traditional;
        set => Select(value, SpellingAlphabetKind.Traditional);
    }

    public bool IsDin5009
    {
        get => Alphabet == SpellingAlphabetKind.Din5009;
        set => Select(value, SpellingAlphabetKind.Din5009);
    }

    public bool IsNato
    {
        get => Alphabet == SpellingAlphabetKind.Nato;
        set => Select(value, SpellingAlphabetKind.Nato);
    }

    partial void OnAlphabetChanged(SpellingAlphabetKind value) => TableRows = SpellingAlphabet.Table(value);

    /// <summary>Spells a text taken from elsewhere — a field of the Lagemeldung.</summary>
    [RelayCommand]
    private void Spell(string? text) =>
        Input = text is { Length: > MaxInputLength } ? text[..MaxInputLength] : text;

    [RelayCommand]
    private void Clear() => Input = null;

    private void Select(bool selected, SpellingAlphabetKind kind)
    {
        if (selected)
        {
            Alphabet = kind;
        }
    }
}
