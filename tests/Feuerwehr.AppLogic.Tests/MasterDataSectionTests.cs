using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.Tests;

public class MasterDataSectionTests
{
    [Fact]
    public void Add_remove_and_reorder_a_list_and_flag_changes()
    {
        var changes = 0;
        var s = new EditableListSection("Rollen", new[] { "EL", "ZF" }, () => changes++);

        s.AddCommand.Execute(null);
        s.Items[^1].Value = "GF";
        s.MoveUpCommand.Execute(s.Items[^1]);   // GF moves ahead of ZF
        s.RemoveCommand.Execute(s.Items.First(i => i.Value == "EL"));

        Assert.Equal(new[] { "GF", "ZF" }, s.ToValues());
        Assert.True(changes >= 4); // add + edit + move + remove
    }

    [Fact]
    public void ToValues_trims_drops_blanks_and_dedupes_keeping_first()
    {
        var s = new EditableListSection("Rollen", new[] { " EL ", "EL", "", "ZF" }, () => { });
        Assert.Equal(new[] { "EL", "ZF" }, s.ToValues());
    }

    [Fact]
    public void MoveDown_at_the_end_and_MoveUp_at_the_top_are_no_ops()
    {
        var s = new EditableListSection("Rollen", new[] { "EL", "ZF" }, () => { });
        s.MoveUpCommand.Execute(s.Items[0]);
        s.MoveDownCommand.Execute(s.Items[^1]);
        Assert.Equal(new[] { "EL", "ZF" }, s.ToValues());
    }

    [Fact]
    public void Personnel_add_edit_and_normalize()
    {
        var changes = 0;
        var s = new PersonnelSection("Personal",
            new[] { new Person("Mustermann", "Max", "ZF", "Land 1", "01 71 / 1 23 45 67") }, () => changes++);

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
        Assert.True(changes >= 3);
    }

    [Fact]
    public void Personnel_rows_without_a_last_name_are_dropped()
    {
        var s = new PersonnelSection("Personal", Array.Empty<Person>(), () => { });
        s.AddCommand.Execute(null);                // empty row
        Assert.Empty(s.ToPeople());
    }
}
