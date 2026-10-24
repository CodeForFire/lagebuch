using LageBuch.AppLogic.ViewModels;

namespace LageBuch.AppLogic.Tests;

public class FireGameViewModelTests
{
    // Window 0 burns first, every spawn takes the first dark window and every spread the first
    // dark neighbour, so a test can name the window it expects to burn.
    private static FireGameViewModel New(out ManualTimeProvider time)
    {
        time = new ManualTimeProvider();
        return new FireGameViewModel(time, new FirstChoiceRandom());
    }

    [Fact]
    public void The_house_front_has_twelve_dark_windows_before_the_first_round()
    {
        var game = New(out var time);

        Assert.Equal(12, game.Windows.Count);
        Assert.All(game.Windows, w => Assert.Equal(0, w.Stage));
        Assert.False(game.IsRunning);
        Assert.Equal(0, time.ActiveTimers);
    }

    [Fact]
    public void Start_lights_the_first_fire_and_runs_the_clock()
    {
        var game = New(out var time);

        game.StartCommand.Execute(null);

        Assert.True(game.IsRunning);
        Assert.Equal(1, game.Windows[0].Stage);
        Assert.Equal(60, game.RemainingSeconds);
        Assert.Equal(1, time.ActiveTimers);
    }

    [Fact]
    public void A_fire_grows_a_stage_at_a_time()
    {
        var game = New(out var time);
        game.StartCommand.Execute(null);

        time.Tick(FireGameViewModel.StepsPerStage);
        Assert.Equal(2, game.Windows[0].Stage);

        time.Tick(FireGameViewModel.StepsPerStage);
        Assert.Equal(3, game.Windows[0].Stage);
    }

    [Fact]
    public void Extinguishing_an_early_fire_scores_more_than_a_late_one()
    {
        var early = New(out _);
        early.StartCommand.Execute(null);
        early.Windows[0].ExtinguishCommand.Execute(null);

        var late = New(out var lateTime);
        late.StartCommand.Execute(null);
        lateTime.Tick(2 * FireGameViewModel.StepsPerStage);
        late.Windows[0].ExtinguishCommand.Execute(null);

        Assert.Equal(3, early.Score);
        Assert.Equal(1, late.Score);
        Assert.Equal(0, early.Windows[0].Stage);
    }

    [Fact]
    public void Tapping_a_dark_window_scores_nothing()
    {
        var game = New(out _);
        game.StartCommand.Execute(null);

        game.Windows[11].ExtinguishCommand.Execute(null);

        Assert.Equal(0, game.Score);
    }

    [Fact]
    public void A_fire_left_at_full_stage_spreads_to_a_neighbour()
    {
        var game = New(out var time);
        game.StartCommand.Execute(null);

        // Window 0 reaches stage 3, then holds it until it spreads. Its neighbours are 1 (right)
        // and 3 (below); by now window 1 burns from a spawn, so the jump lands on 3, which no
        // spawn has reached yet.
        time.Tick((2 * FireGameViewModel.StepsPerStage) + FireGameViewModel.StepsBeforeSpread - 1);
        Assert.Equal(0, game.Windows[3].Stage);

        time.Tick();
        Assert.Equal(1, game.Windows[3].Stage);
    }

    [Fact]
    public void Six_burning_windows_end_the_round_as_Vollbrand()
    {
        var game = New(out var time);
        game.StartCommand.Execute(null);

        for (var step = 0; step < FireGameViewModel.RoundSteps && game.IsRunning; step++)
        {
            time.Tick();
        }

        Assert.True(game.IsOver);
        Assert.False(game.IsRunning);
        Assert.True(game.RemainingSeconds > 0);
        Assert.Contains("Vollbrand", game.ResultText, StringComparison.Ordinal);
        Assert.Equal(0, time.ActiveTimers);
    }

    [Fact]
    public void The_round_ends_after_sixty_seconds()
    {
        var game = New(out var time);
        game.StartCommand.Execute(null);

        for (var step = 0; step < FireGameViewModel.RoundSteps; step++)
        {
            ExtinguishAll(game);
            time.Tick();
        }

        Assert.True(game.IsOver);
        Assert.Equal(0, game.RemainingSeconds);
        Assert.Contains("Zeit", game.ResultText, StringComparison.Ordinal);
        Assert.Equal(0, time.ActiveTimers);
    }

    [Fact]
    public void Nothing_scores_once_the_round_is_over()
    {
        var game = New(out var time);
        game.StartCommand.Execute(null);
        time.Tick(FireGameViewModel.RoundSteps);
        var score = game.Score;

        ExtinguishAll(game);

        Assert.Equal(score, game.Score);
    }

    [Fact]
    public void Best_score_survives_a_restart()
    {
        var game = New(out var time);
        game.StartCommand.Execute(null);
        game.Windows[0].ExtinguishCommand.Execute(null);
        time.Tick(FireGameViewModel.RoundSteps);
        var best = game.Score;

        game.StartCommand.Execute(null);

        Assert.Equal(0, game.Score);
        Assert.Equal(best, game.BestScore);
        Assert.True(best > 0);
        Assert.Equal(1, time.ActiveTimers);
    }

    [Fact]
    public void Fires_flare_up_faster_late_in_the_round()
    {
        var game = New(out var time);
        game.StartCommand.Execute(null);
        var spawnSteps = new List<int>();

        for (var step = 1; step <= FireGameViewModel.RoundSteps; step++)
        {
            ExtinguishAll(game);
            time.Tick();
            if (game.Windows.Any(w => w.IsBurning))
            {
                spawnSteps.Add(step);
            }
        }

        var gaps = spawnSteps.Zip(spawnSteps.Skip(1), (a, b) => b - a).ToList();
        Assert.Equal(FireGameViewModel.FirstSpawnEvery, gaps[0]);
        Assert.Equal(FireGameViewModel.LastSpawnEvery, gaps[^1]);
    }

    [Fact]
    public void A_record_round_says_so_and_a_worse_one_does_not()
    {
        var game = New(out var time);
        game.StartCommand.Execute(null);
        game.Windows[0].ExtinguishCommand.Execute(null);
        time.Tick(FireGameViewModel.RoundSteps);

        Assert.Equal("Vollbrand! 3 Punkte – Anwärter:in. Neuer Bestwert!", game.ResultText);

        // Equalling the best is not beating it.
        game.StartCommand.Execute(null);
        game.Windows[0].ExtinguishCommand.Execute(null);
        time.Tick(FireGameViewModel.RoundSteps);

        Assert.Equal("Vollbrand! 3 Punkte – Anwärter:in.", game.ResultText);
        Assert.Equal(3, game.BestScore);
    }

    [Theory]
    [InlineData(19, "Anwärter:in")]
    [InlineData(20, "Truppführer:in")]
    [InlineData(49, "Truppführer:in")]
    [InlineData(50, "Gruppenführer:in")]
    [InlineData(89, "Gruppenführer:in")]
    [InlineData(90, "Zugführer:in")]
    public void The_rank_rises_with_the_score(int score, string rank) =>
        Assert.Equal(rank, FireGameViewModel.Rank(score));

    [Fact]
    public void Before_the_first_round_the_clock_shows_a_full_minute_and_the_start_panel()
    {
        var game = New(out _);

        Assert.Equal(60, game.RemainingSeconds);
        Assert.True(game.IsIdle);

        game.StartCommand.Execute(null);
        Assert.False(game.IsIdle);
    }

    [Fact]
    public void Vollbrand_strikes_at_exactly_six_burning_windows()
    {
        var game = New(out var time);
        game.StartCommand.Execute(null);

        while (game.IsRunning)
        {
            Assert.True(game.Windows.Count(w => w.IsBurning) < FireGameViewModel.VollbrandWindows);
            time.Tick();
        }

        Assert.True(game.Windows.Count(w => w.IsBurning) >= FireGameViewModel.VollbrandWindows);
    }

    [Fact]
    public void Starting_again_mid_round_clears_the_house_and_keeps_one_clock()
    {
        var game = New(out var time);
        game.StartCommand.Execute(null);
        time.Tick(3 * FireGameViewModel.StepsPerStage);
        game.Windows[0].ExtinguishCommand.Execute(null);

        game.StartCommand.Execute(null);

        Assert.Equal(1, time.ActiveTimers);
        Assert.Equal(0, game.Score);
        Assert.Equal(1, game.Windows.Count(w => w.IsBurning));
        Assert.Equal(1, game.Windows[0].Stage);
    }

    [Fact]
    public void Starting_again_after_a_round_clears_the_result()
    {
        var game = New(out var time);
        game.StartCommand.Execute(null);
        time.Tick(FireGameViewModel.RoundSteps);
        Assert.True(game.IsOver);

        game.StartCommand.Execute(null);

        Assert.False(game.IsOver);
    }

    [Fact]
    public void A_fire_in_the_middle_spreads_left_right_up_or_down_only()
    {
        // The first fire lands in window 5, the middle of the second floor. Spawns take windows
        // 1 and 2, so of its neighbours 2, 4, 6 and 8 the first dark one is 4.
        var time = new ManualTimeProvider();
        var game = new FireGameViewModel(time, new FirstChoiceRandom(first: 4));
        game.StartCommand.Execute(null);

        time.Tick((2 * FireGameViewModel.StepsPerStage) + FireGameViewModel.StepsBeforeSpread);

        Assert.Equal(1, game.Windows[3].Stage);
        Assert.Equal(0, game.Windows[5].Stage);
        Assert.Equal(0, game.Windows[7].Stage);
    }

    [Fact]
    public void A_fire_in_the_corner_spreads_inside_the_house()
    {
        // Window 12, bottom right: its only neighbours are 9 above and 11 to the left.
        var time = new ManualTimeProvider();
        var game = new FireGameViewModel(time, new FirstChoiceRandom(first: 11));
        game.StartCommand.Execute(null);

        time.Tick((2 * FireGameViewModel.StepsPerStage) + FireGameViewModel.StepsBeforeSpread);

        Assert.Equal(1, game.Windows[8].Stage);
        Assert.Equal(0, game.Windows[10].Stage);
    }

    [Fact]
    public void A_window_shows_one_size_of_fire_at_a_time()
    {
        var game = New(out var time);
        var window = game.Windows[0];
        Assert.False(window.IsSmall || window.IsMedium || window.IsLarge);

        game.StartCommand.Execute(null);
        Assert.True(window.IsSmall);
        Assert.False(window.IsMedium || window.IsLarge);

        time.Tick(FireGameViewModel.StepsPerStage);
        Assert.True(window.IsMedium);
        Assert.False(window.IsSmall || window.IsLarge);

        time.Tick(FireGameViewModel.StepsPerStage);
        Assert.True(window.IsLarge);
        Assert.False(window.IsSmall || window.IsMedium);
    }

    [Fact]
    public void Close_stops_the_clock_and_raises_Closed()
    {
        var game = New(out var time);
        var closed = false;
        game.Closed += (_, _) => closed = true;
        game.StartCommand.Execute(null);

        game.CloseCommand.Execute(null);

        Assert.True(closed);
        Assert.False(game.IsRunning);
        Assert.Equal(0, time.ActiveTimers);
    }

    [Fact]
    public void Each_window_names_its_number_and_whether_it_burns()
    {
        var game = New(out _);
        game.StartCommand.Execute(null);

        Assert.Equal("Fenster 1, brennt", game.Windows[0].AccessibleName);
        Assert.Equal("Fenster 2, dunkel", game.Windows[1].AccessibleName);
    }

    private static void ExtinguishAll(FireGameViewModel game)
    {
        foreach (var window in game.Windows)
        {
            window.ExtinguishCommand.Execute(null);
        }
    }

    // Always the first choice, except optionally for the very first pick.
    private sealed class FirstChoiceRandom(int first = 0) : Random
    {
        private int _first = first;

        public override int Next(int maxValue)
        {
            var pick = _first;
            _first = 0;
            return pick;
        }
    }
}
