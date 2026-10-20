using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using LageBuch.App.Shared.Views;

namespace LageBuch.Acceptance.Tests;

// A ContainerQuery attaches to every element in the scope of the Styles that hold it, not only to
// what its own styles select: each one subscribes to the container's size events, and detaching N
// of them from that one event costs O(N²). The Stammdaten editor's 40 Fahrzeuge are ~6,000
// elements, and with the shell's three queries on MainView's root, leaving the tab hung for 7 s,
// Personal for 13 s. A query belongs on the smallest subtree it styles, never above a list.
public class ContainerQueryScopeTests
{
    [AvaloniaFact]
    public void The_shell_root_holds_no_container_query_over_every_screen()
    {
        var root = Assert.IsAssignableFrom<Control>(new MainView().Content);

        Assert.Empty(Flatten(root.Styles).OfType<ContainerQuery>());
    }

    [AvaloniaFact]
    public void The_stammdaten_editor_lays_out_for_a_phone_without_a_container_query()
    {
        var view = new MasterDataEditorView();

        Assert.Empty(Flatten(view.Styles).OfType<ContainerQuery>());
    }

    [AvaloniaFact]
    public void The_workspace_root_holds_no_container_query_over_the_open_module()
    {
        var root = Assert.IsAssignableFrom<Control>(new IncidentWorkspaceView().Content);

        Assert.Empty(Flatten(root.Styles).OfType<ContainerQuery>());
    }

    // Measured on #685: leaving Beteiligte with 200 entries took 33 s with its own query in place,
    // 291 ms without. Every module lays out for a phone off the workspace's "narrow" class instead.
    [AvaloniaFact]
    public void No_module_view_lays_out_for_a_phone_with_a_container_query()
    {
        UserControl[] modules =
        [
            new EtbView(), new ForcesView(), new RolesView(), new TasksView(), new ScbaView(),
            new InvolvedPartiesView(), new FilesView(), new ContactsView(), new CoMessprotokollView(),
        ];

        Assert.All(modules, view => Assert.Empty(Flatten(view.Styles).OfType<ContainerQuery>()));
    }

    private static IEnumerable<IStyle> Flatten(IEnumerable<IStyle> styles) =>
        styles.SelectMany(s => Flatten(s.Children).Prepend(s));
}
