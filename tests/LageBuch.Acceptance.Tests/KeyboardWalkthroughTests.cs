using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #545, Done when: an Übung from the Home screen to the PDF, by keyboard alone. Nothing here sets a
// view model property, executes a command or focuses a control; the only inputs are key presses,
// plus the clock moving on so a Druckabfrage falls due. If a step needs the mouse, this test is
// where that shows.
public class KeyboardWalkthroughTests
{
    [AvaloniaFact]
    public void An_Uebung_runs_from_Home_to_the_PDF_by_keyboard_alone()
    {
        var pdfPath = Path.Join(Path.GetTempPath(), $"lagebuch-walkthrough-{Guid.NewGuid():N}.pdf");
        try
        {
            var shell = ShellHarness.ShowHome(pdfPath);
            var window = shell.Window;
            var keys = new CountingKeyboard(window);

            // Home: NEUER EINSATZ, then the Lagebuchführer prompt.
            keys.TabTo(Named<Button>(window, "NewIncidentButton"));
            keys.Press(PhysicalKey.Enter);
            Assert.NotNull(shell.Main.PendingPrompt);
            window.AssertFocused(Named<AutoCompleteBox>(window, "OperatorNameBox"));
            keys.Type(AnonymizedExampleData.OperatorSurname);
            keys.Tab();
            keys.Type("Florian Testort 12/1");
            keys.Press(PhysicalKey.Enter);
            var vm = shell.Workspace;

            // Kräfte: one vehicle from a neighbouring Feuerwehr.
            keys.Press(PhysicalKey.Digit4, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            window.AssertFocused(Named<ComboBox>(window, "VehicleBox"));
            keys.Tab();
            keys.Type("FF Nachbarort");
            keys.Tab();
            keys.Type("Florian Nachbarort 40/1");
            keys.TabTo(Named<TextBox>(window, "MannschaftBox"));
            keys.Type("5");
            keys.Press(PhysicalKey.Enter);
            Assert.Equal("Florian Nachbarort 40/1", Assert.Single(vm.Forces.Forces).CallSign);

            // Atemschutz: register a Trupp and send it in.
            keys.Press(PhysicalKey.Digit6, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            window.AssertFocused(Named<AutoCompleteBox>(window, "CallSignBox"));
            keys.Type("Florian Testort 40/1");
            keys.TabTo(Named<ComboBox>(window, "TruppTypeBox"));
            keys.Press(PhysicalKey.ArrowDown);
            keys.TabTo(Named<AutoCompleteBox>(window, "TruppfuehrerBox"));
            keys.Type("Erika Testfrau");
            keys.TabTo(Named<AutoCompleteBox>(window, "TruppmannBox"));
            keys.Type("Max Testmann");
            keys.Press(PhysicalKey.Enter);
            var trupp = Assert.Single(vm.Scba.Trupps);
            keys.TabTo("IM EINSATZ", () => window.FocusedElement() is Button { Content: "IM EINSATZ" });
            keys.Press(PhysicalKey.Enter);
            Assert.True(trupp.IsActive);

            // The Druckabfrage falls due; F9 leads to the Trupp's Druck field.
            for (var minute = 0; !trupp.IsControlDue; minute++)
            {
                Assert.True(minute < 60, "The Druckabfrage never fell due.");
                shell.Clock.Now = shell.Clock.Now.AddMinutes(1);
                shell.Ticker.Pulse();
                Dispatcher.UIThread.RunJobs();
            }

            keys.Press(PhysicalKey.F9);
            Dispatcher.UIThread.RunJobs();
            keys.Type("270");
            keys.Press(PhysicalKey.Enter);
            Assert.Equal("zuletzt 270", trupp.PressurePlaceholder);

            // ETB: two entries, each started from anywhere with Strg+N.
            foreach (var (sender, text) in new[]
                     {
                         ("Florian Testort 40/1", "Erste Lagemeldung"),
                         ("Florian Nachbarort 40/1", "Wasserversorgung steht"),
                     })
            {
                keys.Press(PhysicalKey.N, RawInputModifiers.Control);
                Dispatcher.UIThread.RunJobs();
                window.AssertFocused(Named<AutoCompleteBox>(window, "FromBox"));
                keys.Type(sender);
                keys.TabTo(Named<TextBox>(window, "EtbTextBox"));
                keys.Type(text);
                keys.Press(PhysicalKey.Enter);
            }

            Assert.Contains(vm.Etb.Entries, e => e.Text == "Erste Lagemeldung");
            Assert.Contains(vm.Etb.Entries, e => e.Text == "Wasserversorgung steht");

            // Aufgaben: one task.
            keys.Press(PhysicalKey.Digit2, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            keys.TabTo(Named<TextBox>(window, "TaskNewTextBox"));
            keys.Type("Hydrantenplan holen");
            keys.Press(PhysicalKey.Enter);
            Assert.Equal("Hydrantenplan holen", Assert.Single(vm.Tasks.Rows).Text);

            // PDF: the header's PDF EXPORTIEREN, then Enter on the options dialog.
            keys.TabTo(Named<Button>(window, "ExportButton"));
            keys.Press(PhysicalKey.Enter);
            Assert.NotNull(vm.PendingPdfExportOptions);
            keys.Press(PhysicalKey.Enter);
            Assert.True(
                SpinWait.SpinUntil(
                    () =>
                    {
                        Dispatcher.UIThread.RunJobs();
                        return vm.ExportStatus is not null;
                    },
                    TimeSpan.FromSeconds(10)),
                $"The export never finished; focus is on {window.DescribeFocus()}.");

            Assert.StartsWith("PDF exportiert", vm.ExportStatus, StringComparison.Ordinal);
            Assert.True(File.Exists(pdfPath));
            Assert.Null(vm.PendingPdfExportOptions);
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name && c.IsEffectivelyVisible);
}
