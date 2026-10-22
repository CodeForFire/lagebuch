using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The "Einsatzdaten" overlay (Stichwort, Einsatznummer, Straße, Ortsteil). Hosted directly, same
// idiom as TaskDialogRenderTests; the saved PNG is the PR's "after" screenshot.
public class IncidentDataDialogRenderTests
{
    private static LocalIncidentSession Session()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>(),
            new IncidentNumber("B 1.2 260715 123"),
            keyword: "B3P");
        session.SetAddress("Hauptstr.", "Puch", houseNumber: "12", postalCode: "82256", place: "Fürstenfeldbruck");
        return session;
    }

    private static T Find<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [AvaloniaFact]
    public void Dialog_renders_with_every_field_prefilled()
    {
        var dialogVm = new IncidentDataDialogViewModel(Session(), () => { });

        var view = new IncidentDataDialogView { DataContext = dialogVm };
        var window = new Window { Content = view, Width = 560, Height = 480 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("B3P", Find<AutoCompleteBox>(window, "IncidentDataKeywordBox").Text);
        Assert.Equal("B 1.2 260715 123", Find<TextBox>(window, "IncidentDataNumberBox").Text);
        Assert.Equal("Hauptstr.", Find<TextBox>(window, "IncidentDataStreetBox").Text);
        Assert.Equal("Puch", Find<TextBox>(window, "IncidentDataDistrictBox").Text);
        Assert.Equal("12", Find<TextBox>(window, "IncidentDataHouseNumberBox").Text);
        Assert.Equal("82256", Find<TextBox>(window, "IncidentDataPostalCodeBox").Text);
        Assert.Equal("Fürstenfeldbruck", Find<TextBox>(window, "IncidentDataPlaceBox").Text);
        Assert.True(Find<Button>(window, "IncidentDataSaveButton").IsEnabled);

        var dir = Environment.GetEnvironmentVariable("LAGEBUCH_SHOT_DIR") ?? Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, "incident-data-dialog.png"));
    }

    [AvaloniaFact]
    public void Dialog_focuses_the_stichwort_field_on_open()
    {
        var dialogVm = new IncidentDataDialogViewModel(Session(), () => { });
        var window = new Window { Content = new IncidentDataDialogView { DataContext = dialogVm }, Width = 560, Height = 480 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.AssertFocused(Find<AutoCompleteBox>(window, "IncidentDataKeywordBox"));
    }

    [AvaloniaFact]
    public void Escape_cancels_and_enter_saves()
    {
        var closed = 0;
        var session = Session();
        var dialogVm = new IncidentDataDialogViewModel(session, () => { });
        dialogVm.Closed += (_, _) => closed++;
        var window = new Window { Content = new IncidentDataDialogView { DataContext = dialogVm }, Width = 560, Height = 480 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var street = Find<TextBox>(window, "IncidentDataStreetBox");
        street.Focus();
        window.Press(PhysicalKey.Escape); // nothing typed: closes in one key
        Assert.Equal(1, closed);

        street.Text = "Nebenstr.";
        window.Press(PhysicalKey.Enter);
        Assert.Equal(2, closed);
        Assert.Equal("Nebenstr.", session.Incident.Street);
    }

    // #652: Esc with typed Einsatzdaten asks first, focus on ABBRECHEN, so a reflex Enter keeps
    // editing; VERWERFEN then closes without saving.
    [AvaloniaFact]
    public void Escape_after_typing_asks_before_discarding()
    {
        var closed = 0;
        var session = Session();
        var dialogVm = new IncidentDataDialogViewModel(session, () => { });
        dialogVm.Closed += (_, _) => closed++;
        var window = new Window { Content = new IncidentDataDialogView { DataContext = dialogVm }, Width = 560, Height = 560 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var street = Find<TextBox>(window, "IncidentDataStreetBox");
        street.Focus();
        street.Text = "Nebenstr.";

        window.Press(PhysicalKey.Escape);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, closed);
        Assert.True(Find<Border>(window, "IncidentDataDiscardPanel").IsEffectivelyVisible);
        window.AssertFocused(Find<Button>(window, "IncidentDataKeepEditingButton"));
        Capture(window, "incident-data-discard-confirm.png");

        window.Press(PhysicalKey.Enter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, closed);
        Assert.False(Find<Border>(window, "IncidentDataDiscardPanel").IsEffectivelyVisible);
        Assert.Equal("Nebenstr.", street.Text);

        window.Press(PhysicalKey.Escape);
        Dispatcher.UIThread.RunJobs();
        Find<Button>(window, "IncidentDataDiscardButton").Command!.Execute(null);

        Assert.Equal(1, closed);
        Assert.Equal("Hauptstr.", session.Incident.Street);
    }

    // Opt-in PNG capture for the PR's before/after pair; a plain test run writes nothing.
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

    // #704: opening STICHWORT lists the Stammdaten's Stichwörter. The list is a popup, a TopLevel
    // of its own that the window's frame does not contain, so the PR screenshot draws the popup's
    // frame over the window's at the popup's offset.
    [AvaloniaFact]
    public void Opening_the_stichwort_field_lists_the_stammdaten_stichwoerter()
    {
        var dialogVm = new IncidentDataDialogViewModel(Session(), () => { }, ["B2", "B3", "B3 Person", "THL 1"]);
        var window = new Window { Content = new IncidentDataDialogView { DataContext = dialogVm }, Width = 560, Height = 480 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var keyword = Find<AutoCompleteBox>(window, "IncidentDataKeywordBox");
        keyword.Focus();
        keyword.Text = string.Empty;
        keyword.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();

        var popup = keyword.GetVisualDescendants().OfType<Popup>().Single();
        var list = popup.Child!.GetVisualDescendants().OfType<ListBox>().Single();
        Assert.True(popup.IsOpen);
        Assert.Equal(["B2", "B3", "B3 Person", "THL 1"], list.Items.Cast<string>());

        var popupRoot = TopLevel.GetTopLevel(popup.Child)!;
        Dispatcher.UIThread.RunJobs();
        using var windowFrame = window.CaptureRenderedFrame()!;
        using var popupFrame = popupRoot.CaptureRenderedFrame()!;
        var offset = popupRoot.PointToScreen(default) - window.PointToScreen(default);
        using var composite = new RenderTargetBitmap(windowFrame.PixelSize);
        using (var context = composite.CreateDrawingContext())
        {
            context.DrawImage(windowFrame, new Rect(windowFrame.Size));
            context.DrawImage(popupFrame, new Rect(new Point(offset.X, offset.Y), popupFrame.Size));
        }

        var dir = Environment.GetEnvironmentVariable("LAGEBUCH_SHOT_DIR") ?? Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        composite.Save(Path.Join(dir, "incident-data-dialog-stichwoerter.png"), PngBitmapEncoderOptions.Default);
    }

    // #704: the Stichwörter from the Stammdaten are offered in the STICHWORT field, and picking one
    // with Enter takes the suggestion without saving the dialog past the operator.
    [AvaloniaFact]
    public void Enter_on_a_stichwort_suggestion_picks_it_and_a_second_enter_saves()
    {
        var closed = 0;
        var session = Session();
        var dialogVm = new IncidentDataDialogViewModel(session, () => { }, ["B3 Person", "THL 1"]);
        dialogVm.Closed += (_, _) => closed++;
        var window = new Window { Content = new IncidentDataDialogView { DataContext = dialogVm }, Width = 560, Height = 480 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var keyword = Find<AutoCompleteBox>(window, "IncidentDataKeywordBox");
        keyword.Focus();
        keyword.Text = string.Empty;
        keyword.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        window.Press(PhysicalKey.ArrowDown); // onto "B3 Person"
        window.Press(PhysicalKey.Enter);

        Assert.Equal(0, closed);
        Assert.Equal("B3 Person", dialogVm.Keyword);

        window.Press(PhysicalKey.Enter);

        Assert.Equal(1, closed);
        Assert.Equal("B3 Person", session.Incident.Keyword);
    }
}
