using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>One window of the house front: dark (stage 0) or burning at stage 1 to 3.</summary>
public sealed partial class FireWindowViewModel : ObservableObject
{
    public const int MaxStage = 3;

    private readonly Action<FireWindowViewModel> _extinguish;
    private int _age;

    internal FireWindowViewModel(int number, Action<FireWindowViewModel> extinguish)
    {
        Number = number;
        _extinguish = extinguish;
    }

    /// <summary>1-based, row by row from the top left; what the screen reader says.</summary>
    public int Number { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBurning), nameof(AccessibleName), nameof(IsSmall), nameof(IsMedium), nameof(IsLarge))]
    private int _stage;

    public bool IsBurning => Stage > 0;

    public bool IsSmall => Stage == 1;

    public bool IsMedium => Stage == 2;

    public bool IsLarge => Stage == MaxStage;

    public string AccessibleName => $"Fenster {Number}, {(IsBurning ? "brennt" : "dunkel")}";

    [RelayCommand]
    private void Extinguish() => _extinguish(this);

    internal void Ignite()
    {
        Stage = 1;
        _age = 0;
    }

    internal void Reset()
    {
        Stage = 0;
        _age = 0;
    }

    /// <summary>Ages the fire one step; true when it has burnt at full size long enough to spread.</summary>
    internal bool Grow(int stepsPerStage, int stepsBeforeSpread)
    {
        _age++;
        if (Stage < MaxStage && _age >= stepsPerStage)
        {
            Stage++;
            _age = 0;
        }
        else if (Stage == MaxStage && _age >= stepsBeforeSpread)
        {
            _age = 0;
            return true;
        }

        return false;
    }
}
