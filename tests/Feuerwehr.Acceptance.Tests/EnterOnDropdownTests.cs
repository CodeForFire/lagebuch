using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Feuerwehr.App.Views;
using Feuerwehr.AppLogic;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.Acceptance.Tests;

// Enter has two jobs on an AutoCompleteBox: accept the highlighted suggestion, and (via the
// input dock's KeyBinding) submit the form. Pressing Enter to pick a suggestion must NOT also
// submit — on Atemschutz that was creating the Trupp mid-selection.
public class EnterOnDropdownTests
{
    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        ChecklistTemplate = new[] { "Blaulicht aus?" },
        TruppTypes = new[] { "Angriffstrupp" },
        Personnel = new[]
        {
            new Person("Mustermann", "Max", "ZF", "Land 1", null),
            new Person("Musterfrau", "Erika", "GF", null, null),
        },
    };

    private static ScbaViewModel BuildScba()
    {
        var clock = new FixedClock();
        var session = IncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", new[] { "Blaulicht aus?" });
        return new ScbaViewModel(session, Md(), clock, new NoopTicker(), new NoopAlarmService(), () => { });
    }

    [AvaloniaFact]
    public void Enter_on_an_open_suggestion_dropdown_does_not_add_the_trupp()
    {
        var vm = BuildScba();
        // All required fields are filled, so AddTrupp *could* execute — the user is only picking
        // the Truppmann from the suggestion list.
        vm.NewDesignation = "Angriffstrupp";
        vm.NewTruppfuehrer = "Müller";
        vm.NewTruppmann = "Must";

        var view = new ScbaView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var box = view.GetControl<AutoCompleteBox>("TruppmannBox");
        box.Focus();
        box.IsDropDownOpen = true; // the suggestion list is open
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(vm.Trupps); // picking a suggestion must not create the Trupp
    }

    [AvaloniaFact]
    public void Enter_with_no_dropdown_open_still_submits()
    {
        var vm = BuildScba();
        vm.NewDesignation = "Angriffstrupp";
        vm.NewTruppfuehrer = "Müller";
        vm.NewTruppmann = "Schmidt";

        var view = new ScbaView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var box = view.GetControl<AutoCompleteBox>("TruppmannBox");
        box.Focus();
        box.IsDropDownOpen = false; // nothing to pick — Enter should submit
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(vm.Trupps); // keyboard-first submit still works
    }

    [AvaloniaFact]
    public void Enter_on_the_operator_prompt_callsign_dropdown_does_not_confirm()
    {
        var vm = new OperatorPromptViewModel(callSignOptions: new[] { "FFB 1/40/1", "Aich 42/1" })
        {
            OperatorName = "Müller", // CanConfirm is satisfied — only the call sign is being picked
        };
        var view = new OperatorPromptView { DataContext = vm };
        var window = new Window { Content = view, Width = 520, Height = 420 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var box = view.GetControl<AutoCompleteBox>("CallSignBox");
        box.Focus();
        box.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(vm.Result); // picking a call sign must not confirm the dialog
    }
}
