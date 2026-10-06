using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #545, Done when: reachable is not the same as fast. Three everyday flows, counted in keys spent
// getting there (text typed is the same in every version and not counted). Before v0.8 (3b4f24d)
// the ETB entry and the vehicle needed a mouse click on the rail, which Tab never reached, plus 5 and
// 11 keys; the Druckkontrolle took 26. The table is in #545. A budget only goes down: a change that
// makes one dearer fails here and has to argue for it.
public class KeystrokeBudgetTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public void One_ETB_entry_from_another_module()
    {
        var shell = ShellHarness.ShowIncident();
        var vm = shell.Workspace;
        StartIn(shell, NavModules.Forces, "VehicleBox");
        var keys = new CountingKeyboard(shell.Window);

        keys.Press(PhysicalKey.N, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        keys.Type("Florian Testort 40/1");
        keys.TabTo(Named<TextBox>(shell.Window, "EtbTextBox"));
        keys.Type("Wasserversorgung steht");
        keys.Press(PhysicalKey.Enter);

        Assert.Contains(vm.Etb.Entries, e => e.Text == "Wasserversorgung steht" && e.From == "Florian Testort 40/1");
        AssertWithin(keys, budget: 4);
    }

    [AvaloniaFact]
    public void One_Druckkontrolle_from_the_warning_bar()
    {
        var shell = ShellHarness.ShowIncident();
        var vm = shell.Workspace;
        vm.Scba.NewDesignation = "Angriffstrupp";
        vm.Scba.NewCallSign = "Florian Testort 40/1";
        vm.Scba.NewTruppfuehrer = "Erika Testfrau";
        vm.Scba.NewTruppmann = "Max Testmann";
        vm.Scba.AddTruppCommand.Execute(null);
        var trupp = vm.Scba.Trupps[^1];
        trupp.StartCommand.Execute(null);
        while (!trupp.IsControlDue)
        {
            shell.Clock.Now = shell.Clock.Now.AddMinutes(1);
            shell.Ticker.Pulse();
        }

        Dispatcher.UIThread.RunJobs();
        StartIn(shell, NavModules.Etb, "EtbTextBox");
        var keys = new CountingKeyboard(shell.Window);

        keys.Press(PhysicalKey.F9);
        Dispatcher.UIThread.RunJobs();
        keys.Type("270");
        keys.Press(PhysicalKey.Enter);

        Assert.Equal("zuletzt 270", trupp.PressurePlaceholder);
        AssertWithin(keys, budget: 2);
    }

    [AvaloniaFact]
    public void One_vehicle_in_Kraefte()
    {
        var shell = ShellHarness.ShowIncident();
        var vm = shell.Workspace;
        StartIn(shell, NavModules.Etb, "EtbTextBox");
        var keys = new CountingKeyboard(shell.Window);

        keys.Press(PhysicalKey.Digit4, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        keys.Press(PhysicalKey.ArrowDown); // the first Stammdaten vehicle
        keys.Press(PhysicalKey.Enter);

        Assert.Single(vm.Forces.Forces);
        AssertWithin(keys, budget: 3);
    }

    // Where the Lagebuchführer's hands are when the flow starts: in another module's form.
    private static void StartIn(ShellHarness shell, string module, string field)
    {
        var vm = shell.Workspace;
        vm.SelectedNavItem = vm.NavItems.Single(i => i.ModuleKey == module);
        Dispatcher.UIThread.RunJobs();
        Named<Control>(shell.Window, field).Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
    }

    private void AssertWithin(CountingKeyboard keys, int budget)
    {
        output.WriteLine($"{keys.Navigation} keys to get there, {keys.Typed} typed, {keys.Presses} in all");
        Assert.True(keys.Navigation <= budget, $"{keys.Navigation} keys to get there; the budget is {budget}.");
    }

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name && c.IsEffectivelyVisible);
}
