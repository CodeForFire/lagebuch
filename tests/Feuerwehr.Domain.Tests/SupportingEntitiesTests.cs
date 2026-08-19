namespace Feuerwehr.Domain.Tests;

public class SupportingEntitiesTests
{
    [Fact]
    public void ChecklistItem_toggles_done_state()
    {
        var item = new ChecklistItem("Rote Kennleuchte ein, Blaulicht aus?", isMandatory: false);
        Assert.False(item.IsDone);
        item.Toggle();
        Assert.True(item.IsDone);
        item.Toggle();
        Assert.False(item.IsDone);
    }

    [Fact]
    public void ChecklistItem_rejects_blank_text()
    {
        Assert.Throws<ArgumentException>(() => new ChecklistItem(" ", isMandatory: false));
    }

    [Fact]
    public void RoleAssignment_requires_role_and_person()
    {
        Assert.Throws<ArgumentException>(() => RoleAssignment.Create("EL", ""));
        Assert.NotEqual(Guid.Empty, RoleAssignment.Create("EL", "Müller").Id);
    }

    [Fact]
    public void ForceUnit_rejects_negative_personnel()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ForceUnit.Create("FFB", -1));
    }

    [Fact]
    public void ForceUnit_stores_values()
    {
        var unit = ForceUnit.Create("Emmering", 9, callSign: "Emmering 40/1");
        Assert.Equal("Emmering", unit.Brigade);
        Assert.Equal(9, unit.PersonnelCount);
        Assert.Equal("Emmering 40/1", unit.CallSign);
    }
}
