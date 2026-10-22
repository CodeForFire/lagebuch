using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

// #696: Funktionen read Funktion, Abschnitt, Name, Funkrufname, Handynummer, then the rest -- in
// the grid, on the phone's cards and in the add form, whose Tab order follows the same line.
public class RolesColumnOrderTests
{
    [AvaloniaFact]
    public void The_grid_orders_Funktion_Abschnitt_Name_Funkrufname_Handynummer_then_the_rest()
    {
        var (_, view, _) = Show(narrow: false);

        var headers = view.GetControl<DataGrid>("RolesGrid").Columns.Select(c => c.Header as string).ToList();

        Assert.Equal(
            new[] { "FUNKTION", "ABSCHNITT", "NAME", "FUNKRUFNAME", "HANDYNUMMER", "VON", "BIS", "AKTION" },
            headers);
    }

    [AvaloniaFact]
    public void Tab_walks_the_add_form_in_the_same_order_as_the_grid()
    {
        var (window, view, _) = Show(narrow: false);
        var role = view.GetControl<AutoCompleteBox>("RoleBox");
        role.Focus();
        Dispatcher.UIThread.RunJobs();
        window.AssertFocused(role);

        foreach (var next in new Control[]
        {
            view.GetControl<TextBox>("SectionBox"),
            view.GetControl<AutoCompleteBox>("PersonNameBox"),
            view.GetControl<AutoCompleteBox>("CallSignBox"),
            view.GetControl<TextBox>("PhoneBox"),
        })
        {
            window.Tab();
            window.AssertFocused(next);
        }
    }

    [AvaloniaFact]
    public void A_phone_card_reads_Funktion_Abschnitt_Name_Funkrufname_Handynummer_then_the_times()
    {
        var (_, view, _) = Show(narrow: true);

        var card = view.GetControl<ItemsControl>("RolesCards").GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("row-card"));
        var sequence = card.GetVisualDescendants()
            .Select(v => v switch
            {
                TextBox => "[Handynummer]",
                TextBlock { Text: { } text } when v.FindAncestorOfType<TextBox>() is null => text,
                _ => null,
            })
            .OfType<string>()
            .ToList();

        var order = new[] { "EL", "Abschnitt Nord", "Mayr", "FFB 12/1", "[Handynummer]", "VON" }
            .Select(s => sequence.IndexOf(s))
            .ToList();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.Order().ToList(), order);
    }

    private static (Window Window, RolesView View, IncidentWorkspaceViewModel Vm) Show(bool narrow)
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Huber", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AssignRole("EL", "Mayr", callSign: "FFB 12/1", section: "Abschnitt Nord", phone: "0171 2345678");
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        vm.Roles.IsNarrow = narrow;
        var view = new RolesView { DataContext = vm.Roles };

        // The phone layout is keyed off the workspace root's "narrow" class (#685).
        var root = new Panel { Name = "WorkspaceRoot", Children = { view } };
        root.Classes.Set("narrow", narrow);
        var window = new Window
        {
            Content = root,
            Width = narrow ? 412 : 1920,
            Height = narrow ? 915 : 1032,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }
}
