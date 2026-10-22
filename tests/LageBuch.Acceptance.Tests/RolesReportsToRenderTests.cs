using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

// #703: the Funktionen grid as a tree of who reports to whom, re-linked by dragging a row onto
// another or through the Unterstellen menu. Doubles as the PR screenshots; a flyout is a popup, a
// TopLevel of its own, so its frame is drawn over the window's.
public class RolesReportsToRenderTests
{
    private static (Window Window, RolesView View, LocalIncidentSession Session, RolesViewModel Vm) Show(bool narrow)
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Huber", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AssignRole("EL", "Mayr", callSign: "FFB 1/1", from: clock.Now, phone: "0171 2345678");
        session.AssignRole("Abschnittsleiter", "Bauer", callSign: "FFB 40/1", from: clock.Now, section: "Abschnitt Nord");
        session.AssignRole("Abschnittsleiter", "Lang", callSign: "FFB 21/1", from: clock.Now, section: "Abschnitt Süd");
        session.AssignRole("GF", "Wagner", callSign: "FFB 44/1", from: clock.Now, section: "Abschnitt Nord");
        session.AssignRole("Pressesprecher", "Kurz", from: clock.Now);
        var roles = session.Incident.Roles;
        session.SetRoleReportsTo(roles[1].Id, roles[0].Id);
        session.SetRoleReportsTo(session.Incident.Roles[2].Id, roles[0].Id);
        session.SetRoleReportsTo(session.Incident.Roles[3].Id, roles[1].Id);
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
        var root = new Panel { Name = "WorkspaceRoot", Children = { view } };
        root.Classes.Set("narrow", narrow);
        var window = new Window { Content = root, Width = narrow ? 412 : 1600, Height = narrow ? 915 : 520 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, session, vm.Roles);
    }

    private static List<string?> GridTree(RolesView view) =>
        view.GetControl<DataGrid>("RolesGrid").GetVisualDescendants().OfType<DataGridRow>()
            .OrderBy(r => r.Bounds.Top)
            .Select(r => r.DataContext is RoleAssignmentRow row ? row.TreeGuide + row.Role + " · " + row.PersonName : null)
            .ToList();

    // The order the grid is bound to. Read from the rows, not the containers: right after a move the
    // DataGrid has not laid its recycled rows out again yet.
    private static List<string> VmTree(RolesViewModel vm) =>
        vm.Roles.Select(r => r.TreeGuide + r.Role + " · " + r.PersonName).ToList();

    // A control inside the grid row showing this person: the drop's source, as the pointer would hit it.
    private static TextBlock CellOf(RolesView view, string personName) =>
        view.GetControl<DataGrid>("RolesGrid").GetVisualDescendants().OfType<DataGridRow>()
            .Single(r => r.DataContext is RoleAssignmentRow { PersonName: var name } && name == personName)
            .GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == personName);

    private static DataTransfer Dragging(Guid id)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(RolesView.RoleDragFormat, id.ToString()));
        return transfer;
    }

    [AvaloniaFact]
    public void The_grid_lists_each_funktion_under_the_one_it_reports_to()
    {
        var (window, view, _, _) = Show(narrow: false);

        Assert.Equal(
            [
                "EL · Mayr",
                "├─ Abschnittsleiter · Bauer",
                "│  └─ GF · Wagner",
                "└─ Abschnittsleiter · Lang",
                "Pressesprecher · Kurz",
            ],
            GridTree(view));
        var guides = view.GetControl<DataGrid>("RolesGrid").GetVisualDescendants().OfType<ItemsControl>()
            .Where(t => t.Name == "RoleTreeGuide").ToList();
        Assert.Equal([0, 1, 2, 1, 0], guides.Select(g => g.ItemCount));
        Capture(window, null, "funktionen-baum.png");
    }

    [AvaloniaFact]
    public void Dropping_a_row_on_another_puts_it_under_that_one()
    {
        var (window, view, session, vm) = Show(narrow: false);
        var kurz = session.Incident.Roles.Single(r => r.PersonName == "Kurz").Id;
        var target = CellOf(view, "Mayr");
        using var transfer = Dragging(kurz);
        vm.BeginDrag(kurz);
        Dispatcher.UIThread.RunJobs();

        var over = new DragEventArgs(DragDrop.DragOverEvent, transfer, target, new Point(4, 4), KeyModifiers.None);
        target.RaiseEvent(over);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(DragDropEffects.Move, over.DragEffects);
        var targetRow = target.FindAncestorOfType<DataGridRow>();
        Assert.NotNull(targetRow);
        Assert.Contains("drop-target", targetRow.Classes);
        Assert.True(view.GetControl<Border>("TopLevelDropZone").IsVisible);
        Capture(window, null, "funktionen-baum-ziehen.png");

        target.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, target, new Point(4, 4), KeyModifiers.None));
        vm.EndDrag();
        Dispatcher.UIThread.RunJobs();

        var el = session.Incident.Roles.Single(r => r.PersonName == "Mayr").Id;
        Assert.Equal(el, session.Incident.Roles.Single(r => r.PersonName == "Kurz").ReportsToId);
        Assert.Equal("└─ Pressesprecher · Kurz", VmTree(vm)[4]);
        Assert.DoesNotContain(view.GetControl<DataGrid>("RolesGrid").GetVisualDescendants().OfType<DataGridRow>(), r => r.Classes.Contains("drop-target"));
        Assert.False(view.GetControl<Border>("TopLevelDropZone").IsVisible);
    }

    [AvaloniaFact]
    public void A_row_is_not_dropped_under_its_own_subordinate()
    {
        var (_, view, session, _) = Show(narrow: false);
        var bauer = session.Incident.Roles.Single(r => r.PersonName == "Bauer");
        var target = CellOf(view, "Wagner");
        using var transfer = Dragging(bauer.Id);

        var over = new DragEventArgs(DragDrop.DragOverEvent, transfer, target, new Point(4, 4), KeyModifiers.None);
        target.RaiseEvent(over);
        target.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, target, new Point(4, 4), KeyModifiers.None));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(DragDropEffects.None, over.DragEffects);
        Assert.Equal(bauer.ReportsToId, session.Incident.Roles.Single(r => r.PersonName == "Bauer").ReportsToId);
    }

    [AvaloniaFact]
    public void Dropping_on_the_top_zone_takes_the_row_out_from_under_its_superior()
    {
        var (_, view, session, vm) = Show(narrow: false);
        var wagner = session.Incident.Roles.Single(r => r.PersonName == "Wagner").Id;
        vm.BeginDrag(wagner);
        Dispatcher.UIThread.RunJobs();
        var zone = view.GetControl<Border>("TopLevelDropZone");
        using var transfer = Dragging(wagner);

        var over = new DragEventArgs(DragDrop.DragOverEvent, transfer, zone, new Point(4, 4), KeyModifiers.None);
        zone.RaiseEvent(over);
        Assert.Equal(DragDropEffects.Move, over.DragEffects);
        Assert.Contains("drag-over", zone.Classes);

        zone.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, zone, new Point(4, 4), KeyModifiers.None));
        Dispatcher.UIThread.RunJobs();

        Assert.Null(session.Incident.Roles.Single(r => r.PersonName == "Wagner").ReportsToId);
        Assert.DoesNotContain("drag-over", zone.Classes);
        Assert.Equal("GF · Wagner", VmTree(vm)[3]); // top level, in the order the incident lists it
    }

    [AvaloniaFact]
    public void The_unterstellen_menu_picks_a_new_superior()
    {
        var (window, view, session, vm) = Show(narrow: false);
        var buttons = view.GetControl<DataGrid>("RolesGrid").GetVisualDescendants().OfType<Button>()
            .Where(b => b.Name == "ReportsToButton").ToList();
        Assert.All(buttons, b => Assert.Equal("Unterstellen unter", Avalonia.Automation.AutomationProperties.GetName(b)));

        var kurzButton = buttons.Single(b => b.DataContext is RoleAssignmentRow { PersonName: "Kurz" });
        kurzButton.Flyout!.ShowAt(kurzButton);
        Dispatcher.UIThread.RunJobs();
        Capture(window, kurzButton, "funktionen-unterstellen-menu.png");

        var content = ((Flyout)kurzButton.Flyout).Content as Control;
        var pick = content!.GetVisualDescendants().OfType<Button>()
            .Single(b => b.DataContext is ReportsToChoice { Display: "Abschnittsleiter (Lang)" });
        pick.Command!.Execute(null);
        kurzButton.Flyout.Hide();
        Dispatcher.UIThread.RunJobs();

        var lang = session.Incident.Roles.Single(r => r.PersonName == "Lang").Id;
        Assert.Equal(lang, session.Incident.Roles.Single(r => r.PersonName == "Kurz").ReportsToId);
        Assert.Equal("   └─ Pressesprecher · Kurz", VmTree(vm)[4]);
    }

    [AvaloniaFact]
    public void A_phone_card_is_indented_by_its_depth_and_names_its_superior()
    {
        var (window, view, _, _) = Show(narrow: true);
        var cards = view.GetControl<ItemsControl>("RolesCards");

        var indents = cards.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "RoleCardIndent").Select(b => b.Width).ToList();
        Assert.Equal([0d, 14d, 28d, 14d, 0d], indents);
        var cardButtons = cards.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "ReportsToCardButton").ToList();
        Assert.Equal("EL (Mayr)", cardButtons[1].Content);
        Assert.All(cardButtons, b => Assert.Equal("Meldet an wählen", Avalonia.Automation.AutomationProperties.GetName(b)));
        Capture(window, null, "funktionen-baum-phone.png");
    }

    private static void Capture(Window window, Button? flyoutOwner, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (dir is null)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var windowFrame = window.CaptureRenderedFrame()!;
        using var composite = new RenderTargetBitmap(windowFrame.PixelSize);
        using (var context = composite.CreateDrawingContext())
        {
            context.DrawImage(windowFrame, new Rect(windowFrame.Size));
            if (flyoutOwner?.Flyout is Flyout { Content: Control content } && TopLevel.GetTopLevel(content) is { } popupRoot
                && !ReferenceEquals(popupRoot, window))
            {
                using var popupFrame = popupRoot.CaptureRenderedFrame()!;
                var offset = popupRoot.PointToScreen(default) - window.PointToScreen(default);
                context.DrawImage(popupFrame, new Rect(new Point(offset.X, offset.Y), popupFrame.Size));
            }
        }

        composite.Save(Path.Join(dir, name), PngBitmapEncoderOptions.Default);
    }
}
