using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// "Löschangriff", the easter egg behind the Über card's logo: fires flare up in the windows of a
/// house front and grow, and a tap puts one out — the earlier, the more points. A fire left at
/// full size jumps to a neighbouring window; six burning at once is Vollbrand and ends the round,
/// otherwise it ends after a minute. Nothing here is saved, and nothing touches an Einsatz.
/// </summary>
/// <remarks>
/// The game runs in steps of <see cref="StepInterval"/>, driven by a <see cref="TimeProvider"/>
/// timer marshalled onto the UI thread. <c>ITicker</c> ticks once a second, which is too
/// coarse to play. All rules live in <see cref="Step"/>, so a test drives the round step by step.
/// </remarks>
public sealed partial class FireGameViewModel : ObservableObject, IDisposable
{
    public const int Columns = 3;
    public const int Rows = 4;

    internal const int RoundSteps = 240;
    internal const int StepsPerStage = 6;
    internal const int StepsBeforeSpread = 8;
    internal const int VollbrandWindows = 6;
    internal const int FirstSpawnEvery = 8;
    internal const int LastSpawnEvery = 3;

    internal static readonly TimeSpan StepInterval = TimeSpan.FromMilliseconds(250);

    private readonly TimeProvider _time;
    private readonly Random _random;
    private readonly IUiDispatcher _dispatcher;
    private ITimer? _timer;
    private int _step;
    private int _lastSpawn;

    public FireGameViewModel(TimeProvider time, Random random, IUiDispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(random);
        _time = time;
        _random = random;
        _dispatcher = dispatcher ?? new ImmediateUiDispatcher();
        Windows = Enumerable.Range(0, Columns * Rows).Select(i => new FireWindowViewModel(i + 1, Extinguish)).ToList();
    }

    /// <summary>The house front, row by row from the top left.</summary>
    public IReadOnlyList<FireWindowViewModel> Windows { get; }

    [ObservableProperty]
    private int _score;

    [ObservableProperty]
    private int _bestScore;

    [ObservableProperty]
    private int _remainingSeconds = RoundSteps * (int)StepInterval.TotalMilliseconds / 1000;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isOver;

    /// <summary>How the last round ended, in German, or null before the first.</summary>
    [ObservableProperty]
    private string? _resultText;

    /// <summary>True whenever no round is running: the start or result panel shows.</summary>
    public bool IsIdle => !IsRunning;

    /// <summary>Raised after Close so the host removes the overlay.</summary>
    public event EventHandler? Closed;

    [RelayCommand]
    private void Start()
    {
        StopClock();
        foreach (var window in Windows)
        {
            window.Reset();
        }

        _step = 0;
        _lastSpawn = 0;
        Score = 0;
        RemainingSeconds = SecondsLeft();
        IsOver = false;
        IsRunning = true;
        Ignite();
        _timer = _time.CreateTimer(_ => _dispatcher.Post(Step), null, StepInterval, StepInterval);
    }

    [RelayCommand]
    private void Close()
    {
        StopClock();
        IsRunning = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => StopClock();

    private void Step()
    {
        // A step the timer posted just before the round ended may still arrive.
        if (!IsRunning)
        {
            return;
        }

        _step++;
        foreach (var window in Windows.Where(w => w.IsBurning).ToList())
        {
            if (window.Grow(StepsPerStage, StepsBeforeSpread))
            {
                Spread(window);
            }
        }

        // The gap between new fires shrinks over the round, from FirstSpawnEvery to LastSpawnEvery.
        var spawnEvery = FirstSpawnEvery - ((FirstSpawnEvery - LastSpawnEvery) * _step / RoundSteps);
        if (_step - _lastSpawn >= spawnEvery)
        {
            Ignite();
            _lastSpawn = _step;
        }

        RemainingSeconds = SecondsLeft();
        if (Windows.Count(w => w.IsBurning) >= VollbrandWindows)
        {
            End("Vollbrand!");
        }
        else if (_step >= RoundSteps)
        {
            End("Zeit um.");
        }
    }

    private void Extinguish(FireWindowViewModel window)
    {
        if (!IsRunning || !window.IsBurning)
        {
            return;
        }

        // Stage 1 scores 3, a full-size fire 1.
        Score += FireWindowViewModel.MaxStage + 1 - window.Stage;
        window.Reset();
    }

    [SuppressMessage("Security", "CA5394", Justification = "Picks a window for a game's next fire; nothing about it is secret.")]
    private void Ignite()
    {
        var dark = Windows.Where(w => !w.IsBurning).ToList();
        if (dark.Count > 0)
        {
            dark[_random.Next(dark.Count)].Ignite();
        }
    }

    [SuppressMessage("Security", "CA5394", Justification = "Picks the window a game's fire jumps to; nothing about it is secret.")]
    private void Spread(FireWindowViewModel from)
    {
        var index = from.Number - 1;
        var (row, column) = Math.DivRem(index, Columns);
        var neighbours = new[] { (row, column - 1), (row, column + 1), (row - 1, column), (row + 1, column) }
            .Where(p => p.Item1 is >= 0 and < Rows && p.Item2 is >= 0 and < Columns)
            .Select(p => Windows[(p.Item1 * Columns) + p.Item2])
            .Where(w => !w.IsBurning)
            .OrderBy(w => w.Number)
            .ToList();
        if (neighbours.Count > 0)
        {
            neighbours[_random.Next(neighbours.Count)].Ignite();
        }
    }

    private void End(string reason)
    {
        StopClock();
        IsRunning = false;
        IsOver = true;
        var record = Score > BestScore;
        BestScore = Math.Max(BestScore, Score);
        ResultText = $"{reason} {Score} Punkte – {Rank(Score)}." + (record ? " Neuer Bestwert!" : string.Empty);
    }

    private static string Rank(int score) => score switch
    {
        < 20 => "Anwärter:in",
        < 50 => "Truppführer:in",
        < 90 => "Gruppenführer:in",
        _ => "Zugführer:in",
    };

    private int SecondsLeft() => (int)Math.Ceiling((RoundSteps - _step) * StepInterval.TotalSeconds);

    private void StopClock()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
