using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

/// <summary>
/// #693: the search field sat beside the title on KONTAKTE and at the far end of the header on
/// LINKS. Every search now follows the title on its header line, on the left.
/// <para>
/// #694: on a wide window the call to action ended up a screen's width away from the fields it
/// submits. Action rows in editors and dialogs start at their own left edge, primary first, so
/// where the button sits never depends on how wide the window is.
/// </para>
/// Doubles as the PR screenshot capture (RENDER_OUT), the idiom of LinksTabRenderTests.
/// </summary>
public class HeaderSearchAndActionPlacementTests
{
    private const double Wide = 1920;
    private const double WideHeight = 1032;

    // How far the box may sit from the title it follows: its 16px lead margin plus slack.
    private const double MaxGapAfterTitle = 24;

    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace(
        double width = Wide, double height = WideHeight)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.lagebuch",
            new[] { ("Blaulicht aus?", false) },
            Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, session);
    }

    private static Window Show(Control content, double width = Wide, double height = WideHeight)
    {
        var window = new Window { Content = content, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }

    private static T Named<T>(Visual root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name && c.IsEffectivelyVisible);

    private static double Left(Visual v, Visual root) => v.TranslatePoint(default, root)!.Value.X;

    private static double Right(Visual v, Visual root) => Left(v, root) + v.Bounds.Width;

    private static Visual ParentOf(Visual v) =>
        v.GetVisualParent() ?? throw new InvalidOperationException($"{v} has no visual parent.");

    private static void AssertSearchFollowsTitle(Window window, string titleName, string boxName)
    {
        var title = Named<TextBlock>(window, titleName);
        var box = Named<TextBox>(window, boxName);
        var header = ParentOf(box);

        var gap = Left(box, window) - Right(title, window);
        Assert.True(
            gap >= 0 && gap <= MaxGapAfterTitle,
            $"{boxName} starts {gap:F0}px after the title; it should follow it directly.");

        var midpoint = Left(header, window) + (header.Bounds.Width / 2);
        Assert.True(
            Right(box, window) <= midpoint,
            $"{boxName} reaches {Right(box, window) - midpoint:F0}px past the middle of its header line.");
    }

    /// <summary>
    /// The primary action starts where its row's container starts, and every further button
    /// follows it to the right, in the order given.
    /// </summary>
    private static void AssertActionsAnchoredLeft(Visual root, Button primary, params Button[] following)
    {
        var row = ParentOf(primary);
        var container = ParentOf(row);

        var offset = Left(primary, root) - Left(container, root);
        Assert.True(
            Math.Abs(offset) <= 1,
            $"{primary.Name} starts {offset:F0}px in from the left edge of the panel it submits.");

        var previous = primary;
        foreach (var next in following)
        {
            Assert.True(
                Left(previous, root) < Left(next, root),
                $"{previous.Name} should come before {next.Name}.");
            previous = next;
        }
    }

    [AvaloniaFact]
    public void Links_search_follows_the_title_on_the_header_line()
    {
        var (window, _, _) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "LINKS");
        Capture(window, "links-wide.png");

        AssertSearchFollowsTitle(window, "LinksTitle", "LinkSearchBox");
    }

    [AvaloniaFact]
    public void Kontakte_search_follows_the_title_on_the_header_line()
    {
        var (window, _, _) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "KONTAKTE");
        Capture(window, "kontakte-wide.png");

        AssertSearchFollowsTitle(window, "ContactsTitle", "ContactSearchBox");
    }

    // The phone keeps its own rules: Kontakte drops the title, Links keeps it, and both boxes
    // stay inside a 412dp screen.
    [AvaloniaTheory]
    [InlineData("LINKS", "LinkSearchBox")]
    [InlineData("KONTAKTE", "ContactSearchBox")]
    public void Search_box_stays_on_screen_on_a_phone(string tab, string boxName)
    {
        var (window, _, _) = ShowWorkspace(412, 915);
        WorkspaceRenderHelper.SelectTab(window, tab);
        Capture(window, $"{boxName}-phone.png");

        var box = Named<TextBox>(window, boxName);
        Assert.True(Left(box, window) >= 0, $"{boxName} starts off the left edge.");
        Assert.True(Right(box, window) <= 412, $"{boxName} runs {Right(box, window) - 412:F0}px past the right edge.");
    }

    [AvaloniaFact]
    public void Wohnung_editor_puts_fertig_first_at_its_left_edge()
    {
        var (window, vm, session) = ShowWorkspace();
        session.AddCoBuilding("Mehrfamilienhaus A", 3, 4);
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "CO-MESSUNG");

        vm.CoMessprotokoll.MatrixRows.Single(r => r.Ordinal == 2).Cells[0].OpenEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Capture(window, "co-editor-wide.png");

        AssertActionsAnchoredLeft(
            window, Named<Button>(window, "EditorConfirmButton"), Named<Button>(window, "EditorCancelButton"));
    }

    [AvaloniaFact]
    public void Wohnung_discard_question_puts_verwerfen_first_at_its_left_edge()
    {
        var (window, vm, session) = ShowWorkspace();
        session.AddCoBuilding("Mehrfamilienhaus A", 3, 4);
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "CO-MESSUNG");

        var co = vm.CoMessprotokoll;
        co.MatrixRows.Single(r => r.Ordinal == 2).Cells[0].OpenEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        co.Editor!.CoValue = 120;
        co.CloseEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(co.IsDiscardEditorConfirmOpen);
        Capture(window, "co-discard-wide.png");

        AssertActionsAnchoredLeft(
            window,
            Named<Button>(window, "ConfirmDiscardEditorButton"),
            Named<Button>(window, "CancelDiscardEditorButton"));
    }

    [AvaloniaFact]
    public void Confirm_dialog_puts_the_confirm_button_first_at_its_left_edge()
    {
        var window = Show(new ConfirmDialogView
        {
            DataContext = new ConfirmDialogViewModel("Einheit entfernen?", "Die Einheit wird entfernt.", "ENTFERNEN", () => { }),
        });
        Capture(window, "confirm-dialog-wide.png");

        AssertActionsAnchoredLeft(window, Named<Button>(window, "ConfirmButton"), Named<Button>(window, "CancelButton"));
    }

    [AvaloniaFact]
    public void Confirm_dialog_keeps_its_buttons_on_a_phone_screen()
    {
        var window = Show(
            new ConfirmDialogView
            {
                DataContext = new ConfirmDialogViewModel("Einheit entfernen?", "Die Einheit wird entfernt.", "ENTFERNEN", () => { }),
            },
            412,
            915);
        Capture(window, "confirm-dialog-phone.png");

        var cancel = Named<Button>(window, "CancelButton");
        Assert.True(Right(cancel, window) <= 412, "ABBRECHEN runs off the right edge of a phone.");
        AssertActionsAnchoredLeft(window, Named<Button>(window, "ConfirmButton"), cancel);
    }

    [AvaloniaFact]
    public void Task_dialog_puts_speichern_first_at_its_left_edge()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var window = Show(new TaskDialogView
        {
            DataContext = new TaskDialogViewModel(session, MasterDataSet.Empty, "Lagemeldung übermittelt", () => { }),
        });
        Capture(window, "task-dialog-wide.png");

        AssertActionsAnchoredLeft(
            window,
            Named<Button>(window, "TaskDialogSaveButton"),
            Named<Button>(window, "TaskDialogSaveMoreButton"),
            Named<Button>(window, "TaskDialogCancelButton"));
    }

    [AvaloniaFact]
    public void Einsatzdaten_dialog_puts_speichern_first_and_verwerfen_first_at_their_left_edge()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var window = Show(new IncidentDataDialogView { DataContext = new IncidentDataDialogViewModel(session, () => { }) });
        Capture(window, "incident-data-wide.png");

        AssertActionsAnchoredLeft(
            window, Named<Button>(window, "IncidentDataSaveButton"), Named<Button>(window, "IncidentDataCancelButton"));

        var street = Named<AutoCompleteBox>(window, "IncidentDataStreetBox");
        street.Focus();
        street.Text = "Nebenstr. 1";
        window.Press(PhysicalKey.Escape);
        Dispatcher.UIThread.RunJobs();
        Capture(window, "incident-data-discard-wide.png");

        AssertActionsAnchoredLeft(
            window,
            Named<Button>(window, "IncidentDataDiscardButton"),
            Named<Button>(window, "IncidentDataKeepEditingButton"));
    }

    [AvaloniaFact]
    public void Pdf_export_dialog_puts_the_export_button_first_at_its_left_edge()
    {
        var window = Show(new PdfExportOptionsView { DataContext = new PdfExportOptionsViewModel(_ => Task.CompletedTask) });
        Capture(window, "pdf-export-wide.png");

        AssertActionsAnchoredLeft(window, Named<Button>(window, "ExportButton"), Named<Button>(window, "CancelButton"));
    }

    [AvaloniaFact]
    public void Operator_prompt_puts_the_confirm_button_first_at_its_left_edge()
    {
        var window = Show(new OperatorPromptView { DataContext = new OperatorPromptViewModel() });
        Capture(window, "operator-prompt-wide.png");

        AssertActionsAnchoredLeft(window, Named<Button>(window, "ConfirmButton"), Named<Button>(window, "CancelButton"));
    }

    [AvaloniaFact]
    public void About_closes_from_its_left_edge()
    {
        var about = Show(new AboutView { DataContext = new AboutViewModel(new FakeDialogs(), "0.1.0") });
        Capture(about, "about-wide.png");
        AssertStartsAtItsColumnsLeftEdge(about, Named<Button>(about, "CloseButton"));
    }

    [AvaloniaFact]
    public void Shortcut_overview_closes_from_its_left_edge()
    {
        var shortcuts = Show(new ShortcutOverviewView { DataContext = new ShortcutOverviewViewModel() });
        Capture(shortcuts, "shortcuts-wide.png");
        AssertStartsAtItsColumnsLeftEdge(shortcuts, Named<Button>(shortcuts, "CloseButton"));
    }

    // A lone button has no row of its own: it sits straight in the card's content column.
    private static void AssertStartsAtItsColumnsLeftEdge(Visual root, Button button)
    {
        var column = ParentOf(button);
        var offset = Left(button, root) - Left(column, root);
        Assert.True(Math.Abs(offset) <= 1, $"{button.Name} starts {offset:F0}px in from the left edge of its card.");
    }
}
