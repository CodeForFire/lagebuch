using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #605: the task dialog said TASK where every other screen says AUFGABE, and the Atemschutz
// short labels "Si.", "akt." and "Abfrage" and the Stammdaten's "Rückzugsdruck" explained
// nothing to someone who has not used the app before.
public class AufgabeAndAtemschutzWordingTests
{
    [AvaloniaFact]
    public void Task_dialog_says_Aufgabe_not_Task()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var dialogVm = new TaskDialogViewModel(session, MasterDataSet.Empty, "Lagemeldung übermittelt", () => { });
        var view = new TaskDialogView { DataContext = dialogVm };
        var window = new Window { Content = view, Width = 560, Height = 360 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("AUFGABE ERSTELLEN", texts);
        Assert.Equal("SPEICHERN & WEITERE AUFGABE", view.GetControl<Button>("TaskDialogSaveMoreButton").Content);
        Assert.DoesNotContain(texts, t => t is not null && t.Contains("TASK", StringComparison.Ordinal));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Atemschutz_short_labels_spell_themselves_out(bool narrow)
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var scba = vm.Scba;
        scba.NewDesignation = "Angriffstrupp";
        scba.NewTruppfuehrer = "Müller";
        scba.NewTruppmann = "Maier";
        scba.NewZweiterTruppmann = string.Empty;
        scba.AddTruppCommand.Execute(null);

        // The grid at a desktop width, the cards at a phone's: the same "narrow" class
        // IncidentWorkspaceView sets up.
        vm.IsNarrow = narrow;
        var view = new ScbaView { DataContext = scba };
        var workspace = new Panel { Name = "WorkspaceRoot", Children = { view } };
        workspace.Classes.Set("narrow", narrow);
        var window = new Window { Content = workspace, Width = narrow ? 420 : 1400, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["akt."] = "Zuletzt gemeldeter Druck",
            ["Abfrage"] = "Zeit bis zur nächsten Druckabfrage",
        };
        var labels = view.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible)
            .Select(t => (Block: t, Text: t.Text ?? string.Empty))
            .Where(l => expected.ContainsKey(l.Text))
            .ToList();

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), labels.Select(l => l.Text).Distinct().Order(StringComparer.Ordinal));
        Assert.All(labels, l => Assert.Equal(expected[l.Text], ToolTip.GetTip(l.Block) as string));

        // "Si." needed a tooltip to be understood at all, so it is written out instead.
        var texts = view.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible)
            .Select(t => t.Text)
            .ToList();
        Assert.Contains("Sicherheitstrupp", texts);
        Assert.DoesNotContain("Si.", texts);
    }

    [AvaloniaFact]
    public void Rueckzugsdruck_setting_explains_when_the_alarm_fires()
    {
        var vm = new MasterDataEditorViewModel(new EmptyProvider(), new FakeDialogs(), new NoFiles());
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var label = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Rückzugsdruck");
        Assert.Equal(
            "Meldet ein Trupp diesen Druck oder weniger, löst der Rückzugsalarm aus.",
            ToolTip.GetTip(label) as string);
    }

    private sealed class EmptyProvider : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty;

        public void Save(MasterDataSet set)
        {
        }
    }

    private sealed class NoFiles : IMasterDataFileService
    {
        public MasterDataImportResult Read(string path) => new(MasterDataSet.Empty, Array.Empty<string>());

        public void Write(string path, MasterDataSet set)
        {
        }
    }
}
