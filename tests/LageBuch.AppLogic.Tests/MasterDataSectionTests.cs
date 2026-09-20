using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Atemschutz;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class MasterDataSectionTests
{
    [Fact]
    public void Add_remove_and_reorder_a_list_and_flag_changes()
    {
        var changes = 0;
        var s = new EditableListSection("Rollen", "ROLLE", new[] { "EL", "ZF" }, () => changes++);

        s.AddCommand.Execute(null);
        s.Items[^1].Value = "GF";
        s.MoveUpCommand.Execute(s.Items[^1]);   // GF moves ahead of ZF
        s.RemoveCommand.Execute(s.Items.First(i => i.Value == "EL"));

        Assert.Equal(new[] { "GF", "ZF" }, s.ToValues());
        Assert.Equal(4, changes); // add + edit + move + remove, one onChanged each
    }

    [Fact]
    public void ToValues_trims_drops_blanks_and_dedupes_keeping_first()
    {
        var s = new EditableListSection("Rollen", "ROLLE", new[] { " EL ", "EL", string.Empty, "ZF" }, () => { });
        Assert.Equal(new[] { "EL", "ZF" }, s.ToValues());
    }

    [Fact]
    public void MoveDown_at_the_end_and_MoveUp_at_the_top_are_no_ops()
    {
        var s = new EditableListSection("Rollen", "ROLLE", new[] { "EL", "ZF" }, () => { });
        s.MoveUpCommand.Execute(s.Items[0]);
        s.MoveDownCommand.Execute(s.Items[^1]);
        Assert.Equal(new[] { "EL", "ZF" }, s.ToValues());
    }

    [Fact]
    public void No_op_list_edits_do_not_flag_changes_or_throw()
    {
        var changes = 0;
        var s = new EditableListSection("Rollen", "ROLLE", new[] { "EL", "ZF" }, () => changes++);
        var foreignItem = new MasterDataItem("Fremd", () => { });

        var exception = Record.Exception(() =>
        {
            s.RemoveCommand.Execute(foreignItem);       // not in the collection
            s.MoveUpCommand.Execute(s.Items[0]);        // already first
            s.MoveDownCommand.Execute(s.Items[^1]);      // already last
        });

        Assert.Null(exception);
        Assert.Equal(0, changes);
        Assert.Equal(new[] { "EL", "ZF" }, s.ToValues());
    }

    [Fact]
    public void Personnel_add_edit_and_normalize()
    {
        var changes = 0;
        var s = new PersonnelSection(
            "Personal",
            new[] { new Person("Mustermann", "Max", "ZF", "Land 1", "01 71 / 1 23 45 67") },
            () => changes++);

        s.AddCommand.Execute(null);
        var row = s.Rows[^1];
        row.LastName = "  Neu  ";
        row.FirstName = "Person";
        row.Phone = "   ";                         // blank optional -> null

        var people = s.ToPeople();
        Assert.Equal(2, people.Count);
        var neu = people.Single(p => p.LastName == "Neu");
        Assert.Equal("Person", neu.FirstName);
        Assert.Null(neu.Phone);
        Assert.Equal(4, changes); // add + last name + first name + phone, one onChanged each
    }

    [Fact]
    public void Personnel_rows_without_a_last_name_are_dropped()
    {
        var s = new PersonnelSection("Personal", Array.Empty<Person>(), () => { });
        s.AddCommand.Execute(null);                // empty row
        Assert.Empty(s.ToPeople());
    }

    [Fact]
    public void PersonRow_Role_change_flags_a_change()
    {
        var changes = 0;
        var row = new PersonRow("Mustermann", "Max", null, null, null, () => changes++);

        row.Role = "GF";

        Assert.Equal(1, changes);
        Assert.Equal("GF", row.Role);
    }

    [Fact]
    public void PersonRow_CallSign_change_flags_a_change()
    {
        var changes = 0;
        var row = new PersonRow("Mustermann", "Max", null, null, null, () => changes++);

        row.CallSign = "Land 1";

        Assert.Equal(1, changes);
        Assert.Equal("Land 1", row.CallSign);
    }

    [Fact]
    public void VehicleRow_HasZugfuehrer_change_flags_a_change()
    {
        var changes = 0;
        var row = new VehicleRow("Wache 1", "ELW 1", 4, false, Array.Empty<string>(), Array.Empty<string>(), () => changes++);

        row.HasZugfuehrer = true;

        Assert.Equal(1, changes);
        Assert.True(row.HasZugfuehrer);
    }

    [Fact]
    public void Vehicles_ToValues_round_trips_the_zugfuehrer_flag()
    {
        var vehicles = new[] { new Vehicle("Wache 1", "ELW 1", 4, HasZugfuehrer: true) };
        var s = new VehiclesSection("Fahrzeuge", vehicles, Array.Empty<string>(), Array.Empty<string>(), () => { });

        Assert.Equal(vehicles, s.ToValues());
    }

    [Fact]
    public void Vehicles_reorder_moves_a_row_and_flags_a_change()
    {
        var changes = 0;
        var vehicles = new[]
        {
            new Vehicle("Wache 1", "Land 1", 6),
            new Vehicle("Wache 1", "Land 2", 6),
        };
        var s = new VehiclesSection("Fahrzeuge", vehicles, Array.Empty<string>(), Array.Empty<string>(), () => changes++);

        s.MoveUpCommand.Execute(s.Rows[^1]); // Land 2 moves ahead of Land 1

        Assert.Equal(new[] { "Land 2", "Land 1" }, s.Rows.Select(r => r.CallSign));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Vehicles_MoveDown_at_the_end_and_MoveUp_at_the_top_are_no_ops()
    {
        var vehicles = new[]
        {
            new Vehicle("Wache 1", "Land 1", 6),
            new Vehicle("Wache 1", "Land 2", 6),
        };
        var s = new VehiclesSection("Fahrzeuge", vehicles, Array.Empty<string>(), Array.Empty<string>(), () => { });

        s.MoveUpCommand.Execute(s.Rows[0]);
        s.MoveDownCommand.Execute(s.Rows[^1]);

        Assert.Equal(new[] { "Land 1", "Land 2" }, s.Rows.Select(r => r.CallSign));
    }

    [Fact]
    public void TruppTypes_add_defaults_to_an_ordinary_two_person_trupp()
    {
        var changes = 0;
        var s = new TruppTypesSection("Trupp-Typen", Array.Empty<TruppType>(), () => changes++);

        s.AddCommand.Execute(null);

        Assert.Equal(AtemschutzTrupp.StandardMemberCount, s.Rows[0].MemberCount);
        Assert.Equal(AtemschutzTrupp.DefaultMaxDurationMinutes, s.Rows[0].MaxDurationMinutes);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void TruppTypes_ToValues_trims_drops_blanks_and_keeps_the_first_spelling()
    {
        // The Atemschutz form looks a type up by name; two rows differing only in case would make
        // that lookup a coin toss, so the first spelling wins as it does for the other lists.
        var s = new TruppTypesSection(
            "Trupp-Typen",
            new[]
            {
                new TruppType("  Angriffstrupp  ", 2, 30),
                new TruppType("angriffstrupp", 3, 20),
                new TruppType("   ", 2, 30),
                new TruppType("Chemietrupp", 3, 20),
            },
            () => { });

        Assert.Equal(
            new[] { new TruppType("Angriffstrupp", 2, 30), new TruppType("Chemietrupp", 3, 20) },
            s.ToValues());
    }

    [Fact]
    public void TruppTypes_ToValues_clamps_a_crew_size_the_sheet_has_no_room_for()
    {
        var s = new TruppTypesSection("Trupp-Typen", new[] { new TruppType("Gross", 2, 30) }, () => { });
        s.Rows[0].MemberCount = 9;

        Assert.Equal(AtemschutzTrupp.MaxMemberCount, s.ToValues()[0].MemberCount);
    }

    [Fact]
    public void TruppTypes_ToValues_clamps_an_einsatzzeit_no_countdown_can_run()
    {
        var s = new TruppTypesSection("Trupp-Typen", new[] { new TruppType("Kaputt", 2, 30) }, () => { });
        s.Rows[0].MaxDurationMinutes = 0;

        Assert.Equal(1, s.ToValues()[0].MaxDurationMinutes);
    }

    [Fact]
    public void TruppTypes_editing_a_row_flags_a_change()
    {
        var changes = 0;
        var s = new TruppTypesSection(
            "Trupp-Typen", new[] { new TruppType("Angriffstrupp") }, () => changes++);

        s.Rows[0].MemberCount = 3;
        s.Rows[0].MaxDurationMinutes = 20;
        s.Rows[0].Name = "Chemietrupp";

        Assert.Equal(3, changes);
    }

    [Fact]
    public void TruppTypes_remove_and_reorder_flag_changes()
    {
        var changes = 0;
        var s = new TruppTypesSection(
            "Trupp-Typen",
            new[] { new TruppType("Erster"), new TruppType("Zweiter"), new TruppType("Dritter") },
            () => changes++);

        s.MoveUpCommand.Execute(s.Rows[^1]);   // Erster, Dritter, Zweiter
        s.MoveDownCommand.Execute(s.Rows[0]);  // Dritter, Erster, Zweiter
        s.RemoveCommand.Execute(s.Rows[0]);    // Erster, Zweiter

        Assert.Equal(new[] { "Erster", "Zweiter" }, s.Rows.Select(r => r.Name));
        Assert.Equal(3, changes);
    }

    [Fact]
    public void TruppTypes_MoveDown_at_the_end_and_MoveUp_at_the_top_are_no_ops()
    {
        var s = new TruppTypesSection(
            "Trupp-Typen", new[] { new TruppType("Erster"), new TruppType("Zweiter") }, () => { });

        s.MoveUpCommand.Execute(s.Rows[0]);
        s.MoveDownCommand.Execute(s.Rows[^1]);

        Assert.Equal(new[] { "Erster", "Zweiter" }, s.Rows.Select(r => r.Name));
    }
}
