using System.Text.Json;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Osm.Tests;

public class StreetListTests
{
    private static readonly StreetList List = new(
        ["Aich", "Puch"],
        [new Street("Pucher Straße", "Puch", "82256", "Fürstenfeldbruck"), new Street("Am Vorfeld")]);

    [Fact]
    public void The_fragment_holds_only_ortsteile_and_strassen()
    {
        using var doc = JsonDocument.Parse(List.ToJsonFragment());

        Assert.Equal(["districts", "streets"], doc.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void The_fragment_reads_back_as_the_same_stammdaten()
    {
        var set = MasterDataJson.Parse(List.ToJsonFragment());

        Assert.Equal(List.Districts, set.Districts);
        Assert.Equal(List.Streets, set.Streets);
    }

    [Fact]
    public void The_fragment_is_written_like_an_exported_file()
    {
        var fragment = List.ToJsonFragment();

        Assert.Contains("Fürstenfeldbruck", fragment, StringComparison.Ordinal); // umlauts stay readable
        Assert.StartsWith("{\n  \"districts\"", fragment.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.EndsWith("}\n", fragment, StringComparison.Ordinal);
    }

    [Fact]
    public void Merging_replaces_only_ortsteile_and_strassen_and_leaves_the_rest_of_the_file_as_it_was()
    {
        const string File = """
            {
              "_comment": "Fiktive Stammdaten",
              "keywords": ["B3 Person"],
              "districts": ["Lindach"],
              "vehicles": [{ "wache": "FFB Wache 1", "callSign": "FFB 1/10/1", "seats": 9 }],
              "streets": [{ "name": "Alte Straße" }]
            }
            """;

        using var merged = JsonDocument.Parse(List.MergeInto(File));
        var root = merged.RootElement;

        Assert.Equal(["_comment", "keywords", "districts", "vehicles", "streets"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Fiktive Stammdaten", root.GetProperty("_comment").GetString());
        Assert.Equal(1, root.GetProperty("vehicles")[0].EnumerateObject().Count(p => p.Name == "seats"));
        Assert.False(root.GetProperty("vehicles")[0].TryGetProperty("isOwn", out _)); // not rewritten
        var set = MasterDataJson.Parse(root.GetRawText());
        Assert.Equal(List.Districts, set.Districts);
        Assert.Equal(List.Streets, set.Streets);
    }

    [Fact]
    public void Merging_into_a_file_without_the_keys_adds_them_after_the_stichwoerter()
    {
        using var merged = JsonDocument.Parse(List.MergeInto("""{ "roles": [], "keywords": [], "vehicles": [] }"""));

        Assert.Equal(
            ["roles", "keywords", "districts", "streets", "vehicles"],
            merged.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{ not json")]
    [InlineData("""{ "vehicles": [{ "seats": 9 }] }""")] // a vehicle without its required fields
    public void Merging_into_something_that_is_not_a_stammdaten_file_is_refused(string file)
    {
        Assert.ThrowsAny<JsonException>(() => List.MergeInto(file));
    }

    [Fact]
    public void Applying_the_list_replaces_ortsteile_and_strassen_and_keeps_every_other_category()
    {
        var before = MasterDataSet.Empty with
        {
            Keywords = ["B3 Person"],
            Districts = ["Lindach"],
            Streets = [new Street("Alte Straße")],
            Vehicles = [new Vehicle("FFB Wache 1", "FFB 1/10/1", 9)],
        };

        var after = List.ApplyTo(before);

        Assert.Equal(List.Districts, after.Districts);
        Assert.Equal(List.Streets, after.Streets);
        Assert.Equal(before.Keywords, after.Keywords);
        Assert.Equal(before.Vehicles, after.Vehicles);
    }
}
