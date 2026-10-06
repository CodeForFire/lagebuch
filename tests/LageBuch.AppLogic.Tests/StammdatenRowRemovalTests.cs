using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

/// <summary>
/// #543: every Stammdaten Entfernen asks first, as deleting a Checkliste and an import always have,
/// except for a row "+ HINZUFÜGEN" just made that is still blank -- nothing in it was ever stored,
/// and asking would only get in the way of taking back an add pressed once too often. A stored row
/// asks even once emptied. "+ HINZUFÜGEN" announces its row, so the view can put the caret in it.
/// </summary>
public class StammdatenRowRemovalTests
{
    public static TheoryData<string> Kinds => new()
    {
        "Liste", "Checkliste", "Links", "Fahrzeuge", "Trupp-Typen", "Rollen", "Personal",
    };

    // One empty section of each kind, with what this test needs to drive it: add a row, fill the
    // last row's name, count the rows, remove the last row.
    private sealed record Harness(EditorSection Section, Action Add, Action Fill, Func<int> Count, Action RemoveLast);

    private static Harness Build(string kind, Action<string, Action> requestConfirm)
    {
        switch (kind)
        {
            case "Liste":
                {
                    var s = new EditableListSection("Einheiten-Status", "STATUS", Array.Empty<string>(), () => { }, requestConfirm);
                    return new(s, () => s.AddCommand.Execute(null), () => s.Items[^1].Value = "Im Einsatz", () => s.Items.Count, () => s.RemoveCommand.Execute(s.Items[^1]));
                }

            case "Checkliste":
                {
                    var s = new ChecklistTemplateSection(Guid.NewGuid(), "Aufbau", Array.Empty<ChecklistTemplateItem>(), () => { }, requestConfirm);
                    return new(s, () => s.AddCommand.Execute(null), () => s.Rows[^1].Text = "Wasser", () => s.Rows.Count, () => s.RemoveCommand.Execute(s.Rows[^1]));
                }

            case "Links":
                {
                    var s = new LinksSection("Links", Array.Empty<Link>(), () => { }, requestConfirm);
                    return new(s, () => s.AddCommand.Execute(null), () => s.Rows[^1].Url = "https://example.org", () => s.Rows.Count, () => s.RemoveCommand.Execute(s.Rows[^1]));
                }

            case "Fahrzeuge":
                {
                    var s = new VehiclesSection("Fahrzeuge", Array.Empty<Vehicle>(), Array.Empty<string>(), Array.Empty<string>(), () => { }, requestConfirm);
                    return new(s, () => s.AddCommand.Execute(null), () => s.Rows[^1].Wache = "FFB Wache 1", () => s.Rows.Count, () => s.RemoveCommand.Execute(s.Rows[^1]));
                }

            case "Trupp-Typen":
                {
                    var s = new TruppTypesSection("Trupp-Typen", Array.Empty<TruppType>(), () => { }, requestConfirm);
                    return new(s, () => s.AddCommand.Execute(null), () => s.Rows[^1].Name = "Angriffstrupp", () => s.Rows.Count, () => s.RemoveCommand.Execute(s.Rows[^1]));
                }

            case "Rollen":
                {
                    var s = new RolesSection("Rollen", Array.Empty<Role>(), () => { }, requestConfirm);
                    return new(s, () => s.AddCommand.Execute(null), () => s.Rows[^1].Name = "EL", () => s.Rows.Count, () => s.RemoveCommand.Execute(s.Rows[^1]));
                }

            default:
                {
                    var s = new PersonnelSection("Personal", Array.Empty<Person>(), () => { }, requestConfirm);
                    return new(s, () => s.AddCommand.Execute(null), () => s.Rows[^1].Phone = "0170 0000000", () => s.Rows.Count, () => s.RemoveCommand.Execute(s.Rows[^1]));
                }
        }
    }

    // One section of each kind holding one row as the Stammdaten stored it, and an action that
    // empties every field of that row a user can type into, then presses its Entfernen.
    private static (Func<int> Count, Action EmptyThenRemove) BuildStored(string kind, Action<string, Action> requestConfirm)
    {
        switch (kind)
        {
            case "Liste":
                {
                    var s = new EditableListSection("Einheiten-Status", "STATUS", new[] { "Alarmiert" }, () => { }, requestConfirm);
                    void EmptyThenRemove()
                    {
                        s.Items[0].Value = string.Empty;
                        s.RemoveCommand.Execute(s.Items[0]);
                    }

                    return (() => s.Items.Count, EmptyThenRemove);
                }

            case "Checkliste":
                {
                    var s = new ChecklistTemplateSection(Guid.NewGuid(), "Aufbau", new[] { new ChecklistTemplateItem("Wasser", false) }, () => { }, requestConfirm);
                    void EmptyThenRemove()
                    {
                        s.Rows[0].Text = string.Empty;
                        s.RemoveCommand.Execute(s.Rows[0]);
                    }

                    return (() => s.Rows.Count, EmptyThenRemove);
                }

            case "Links":
                {
                    var s = new LinksSection("Links", new[] { new Link("Wetter", "https://example.org", "Info") }, () => { }, requestConfirm);
                    void EmptyThenRemove()
                    {
                        s.Rows[0].Name = string.Empty;
                        s.Rows[0].Url = string.Empty;
                        s.Rows[0].Group = string.Empty;
                        s.RemoveCommand.Execute(s.Rows[0]);
                    }

                    return (() => s.Rows.Count, EmptyThenRemove);
                }

            case "Fahrzeuge":
                {
                    var s = new VehiclesSection("Fahrzeuge", new[] { new Vehicle("FFB Wache 1", "FFB 1/44/1", 6) }, Array.Empty<string>(), Array.Empty<string>(), () => { }, requestConfirm);
                    void EmptyThenRemove()
                    {
                        s.Rows[0].Wache = string.Empty;
                        s.Rows[0].CallSign = string.Empty;
                        s.RemoveCommand.Execute(s.Rows[0]);
                    }

                    return (() => s.Rows.Count, EmptyThenRemove);
                }

            case "Trupp-Typen":
                {
                    var s = new TruppTypesSection("Trupp-Typen", new[] { new TruppType("Angriffstrupp", 2, 30) }, () => { }, requestConfirm);
                    void EmptyThenRemove()
                    {
                        s.Rows[0].Name = string.Empty;
                        s.RemoveCommand.Execute(s.Rows[0]);
                    }

                    return (() => s.Rows.Count, EmptyThenRemove);
                }

            case "Rollen":
                {
                    var s = new RolesSection("Rollen", new[] { new Role("EL") }, () => { }, requestConfirm);
                    void EmptyThenRemove()
                    {
                        s.Rows[0].Name = string.Empty;
                        s.RemoveCommand.Execute(s.Rows[0]);
                    }

                    return (() => s.Rows.Count, EmptyThenRemove);
                }

            default:
                {
                    var s = new PersonnelSection("Personal", new[] { new Person("Mustermann", "Max", null, null, null) }, () => { }, requestConfirm);
                    void EmptyThenRemove()
                    {
                        s.Rows[0].LastName = string.Empty;
                        s.Rows[0].FirstName = string.Empty;
                        s.RemoveCommand.Execute(s.Rows[0]);
                    }

                    return (() => s.Rows.Count, EmptyThenRemove);
                }
        }
    }

    // A stored row someone empties first is still a stored row: SPEICHERN would drop what the
    // Stammdaten hold, so Entfernen has to ask. Only a row "+ HINZUFÜGEN" made goes without asking.
    [Theory]
    [MemberData(nameof(Kinds))]
    public void Removing_a_stored_row_emptied_first_still_asks(string kind)
    {
        var asked = 0;
        var (count, emptyThenRemove) = BuildStored(kind, (_, _) => asked++);

        emptyThenRemove();

        Assert.Equal(1, asked);
        Assert.Equal(1, count());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Removing_a_filled_row_asks_first_and_removes_only_on_confirm(string kind)
    {
        string? asked = null;
        Action? confirm = null;
        var h = Build(kind, (message, onConfirmed) =>
        {
            asked = message;
            confirm = onConfirmed;
        });
        h.Add();
        h.Fill();

        h.RemoveLast();

        Assert.NotNull(asked);
        Assert.Contains(h.Section.Title, asked, StringComparison.Ordinal);
        Assert.Equal(1, h.Count());
        Assert.NotNull(confirm);
        confirm();
        Assert.Equal(0, h.Count());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Removing_a_row_still_blank_does_not_ask(string kind)
    {
        var asked = 0;
        var h = Build(kind, (_, _) => asked++);
        h.Add();

        h.RemoveLast();

        Assert.Equal(0, asked);
        Assert.Equal(0, h.Count());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Hinzufuegen_announces_the_new_row(string kind)
    {
        var h = Build(kind, (_, onConfirmed) => onConfirmed());
        object? announced = null;
        h.Section.RowAdded += (_, e) => announced = e.Row;

        h.Add();

        Assert.NotNull(announced);
    }

    [Fact]
    public void The_message_names_the_row_and_its_category()
    {
        string? asked = null;
        var s = new RolesSection("Rollen", new[] { new Role("Einsatzleiter") }, () => { }, (message, _) => asked = message);

        s.RemoveCommand.Execute(s.Rows[0]);

        Assert.Equal("„Einsatzleiter“ aus Rollen entfernen?", asked);
    }

    [Fact]
    public void Entfernen_in_the_editor_opens_the_confirm_overlay()
    {
        var vm = new MasterDataEditorViewModel(
            new Provider(MasterDataSet.Empty with { Roles = new[] { new Role("EL"), new Role("ZF") } }),
            new FakeDialogs(),
            new NoFiles());
        var roles = vm.Sections.OfType<RolesSection>().Single();

        roles.RemoveCommand.Execute(roles.Rows[0]);

        var dialog = Assert.IsType<ConfirmDialogViewModel>(vm.PendingConfirm);
        Assert.Equal(2, roles.Rows.Count);
        dialog.CancelCommand.Execute(null);
        Assert.Null(vm.PendingConfirm);
        Assert.Equal(2, roles.Rows.Count);

        roles.RemoveCommand.Execute(roles.Rows[0]);
        Assert.IsType<ConfirmDialogViewModel>(vm.PendingConfirm).ConfirmCommand.Execute(null);
        Assert.Equal("ZF", Assert.Single(roles.Rows).Name);
        Assert.True(vm.IsDirty);
    }

    private sealed class Provider(MasterDataSet set) : IMasterDataProvider
    {
        private MasterDataSet _set = set;

        public MasterDataSet Get() => _set;

        public void Save(MasterDataSet set) => _set = set;
    }
}
