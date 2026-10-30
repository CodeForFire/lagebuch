using LageBuch.Persistence.Map;
using LageBuch.Persistence.Sqlite;

namespace LageBuch.Persistence.Tests;

public sealed class AddressIndexTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-test-").FullName;

    public AddressIndexTests() => Create(
        ("Hauptstraße", "1", "82256", "Fürstenfeldbruck", string.Empty, 48.1785, 11.2555),
        ("Hauptstraße", "12", "82256", "Fürstenfeldbruck", string.Empty, 48.1790, 11.2560),
        ("Hauptstraße", "12a", "82256", "Fürstenfeldbruck", string.Empty, 48.1791, 11.2561),
        ("Hauptstraße", "12", "82216", "Maisach", "Gernlinden", 48.2173, 11.2591),
        ("Hauptstraße", "3", string.Empty, string.Empty, string.Empty, 48.1960, 11.1180),
        ("Allinger Str.", "5", "82223", "Eichenau", string.Empty, 48.1660, 11.3120),
        ("Weg%_Nord", "1", "82256", "Fürstenfeldbruck", string.Empty, 48.17, 11.25),
        ("Weg Nord", "1", "82256", "Fürstenfeldbruck", string.Empty, 48.17, 11.25));

    private string AddressPath => Path.Join(_dir, "addresses.sqlite");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void A_street_and_house_number_find_that_address_first_and_longer_numbers_after()
    {
        var hits = Search("Hauptstraße 12");

        Assert.Equal(["12", "12", "12a"], hits.Select(h => h.HouseNumber));
    }

    [Theory]
    [InlineData("Hauptstr 12a")]
    [InlineData("hauptstr. 12a")]
    [InlineData("Hauptstrasse 12a")]
    [InlineData("HAUPTSTRASSE 12A")]
    public void Every_way_of_writing_strasse_finds_the_same_address(string query)
    {
        var hit = Assert.Single(Search(query), h => h.HouseNumber == "12a");

        Assert.Equal("Hauptstraße", hit.Street);
    }

    [Theory]
    [InlineData("Allinger Straße 5")]
    [InlineData("Allinger Str 5")]
    public void A_street_the_data_abbreviates_is_found_by_its_full_name(string query) =>
        Assert.Equal("Allinger Str.", Assert.Single(Search(query)).Street);

    [Theory]
    [InlineData("Hauptstraße 12 Maisach")]
    [InlineData("Hauptstraße 12, 82216")]
    [InlineData("Hauptstraße 12 gernlinden")]
    public void Words_after_the_house_number_narrow_the_place(string query)
    {
        var hit = Assert.Single(Search(query));

        Assert.Equal("Maisach", hit.City);
    }

    [Fact]
    public void An_address_in_the_named_place_comes_before_one_without_any_place()
    {
        Create(("Hauptstraße", "1", string.Empty, string.Empty, string.Empty, 48.19, 11.11));

        var hits = Search("Hauptstraße 1 Fürstenfeldbruck");

        Assert.Equal(["1 Fürstenfeldbruck", "1 ", "12 Fürstenfeldbruck", "12a Fürstenfeldbruck"], hits.Select(h => $"{h.HouseNumber} {h.City}"));
    }

    [Fact]
    public void The_exact_house_number_comes_before_longer_ones_wherever_they_are()
    {
        Create(("Amperweg", "10", "82256", "Fürstenfeldbruck", string.Empty, 48.17, 11.25), ("Amperweg", "1", "82216", "Maisach", string.Empty, 48.21, 11.25));

        Assert.Equal(["1", "10"], Search("Amperweg 1").Select(h => h.HouseNumber));
    }

    [Fact]
    public void An_address_without_any_place_is_not_ruled_out_by_the_place_words()
    {
        var hit = Assert.Single(Search("Hauptstraße 3 Maisach"));

        Assert.Equal(string.Empty, hit.City);
    }

    // "Haupt Allee" sorts before "Hauptstraße" and shares its stem, so these rows come first.
    [Fact]
    public void A_street_behind_more_other_streets_than_the_candidate_cap_is_still_found()
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(AddressPath))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 2100) " +
                "INSERT INTO addresses SELECT 'Haupt Allee', '12', '82140', 'Olching', '', 48.2, 11.3, 'node/2' FROM n;";
            cmd.ExecuteNonQuery();
        }

        Assert.Equal("Maisach", Assert.Single(Search("Hauptstraße 12 Maisach")).City);
    }

    [Theory]
    [InlineData("ölmühlweg 4")]
    [InlineData("Ölmühlweg 4")]
    [InlineData("ÖLMÜHLWEG 4")]
    public void A_street_starting_with_an_umlaut_is_found_in_any_case(string query)
    {
        Create(("Ölmühlweg", "4", "82256", "Fürstenfeldbruck", string.Empty, 48.18, 11.24));

        Assert.Equal("Ölmühlweg", Assert.Single(Search(query)).Street);
    }

    [Fact]
    public void An_umlaut_after_the_first_letter_is_found_in_any_case()
    {
        Create(("Mühlweg", "4", "82256", "Fürstenfeldbruck", string.Empty, 48.18, 11.24));

        Assert.Equal("Mühlweg", Assert.Single(Search("MÜHLWEG 4")).Street);
    }

    [Theory]
    [InlineData("druden")]
    [InlineData("Druden 3")]
    [InlineData("drudenbogen 3")]
    [InlineData("am druden")]
    public void A_street_is_found_by_a_later_word_of_its_name(string query)
    {
        Create(("Am Drudenbogen", "3", "82256", "Fürstenfeldbruck", string.Empty, 48.18, 11.24));

        Assert.Equal("Am Drudenbogen", Assert.Single(Search(query)).Street);
    }

    [Theory]
    [InlineData("Stifter 7")]
    [InlineData("Adalbert-Stifter 7")]
    [InlineData("adalbert stifter straße 7")]
    public void A_street_is_found_by_a_word_after_a_hyphen_and_by_its_whole_name(string query)
    {
        Create(("Adalbert-Stifter-Straße", "7", "82256", "Fürstenfeldbruck", string.Empty, 48.18, 11.24));

        Assert.Equal("Adalbert-Stifter-Straße", Assert.Single(Search(query)).Street);
    }

    // "Am Bogen" sorts before "Bogenweg", so only the SQL's order puts the street named as typed first.
    [Fact]
    public void A_street_starting_with_the_query_comes_before_one_containing_it_later()
    {
        Create(("Am Bogen", "1", "82256", "Fürstenfeldbruck", string.Empty, 48.18, 11.24), ("Bogenweg", "1", "82256", "Fürstenfeldbruck", string.Empty, 48.18, 11.24));

        Assert.Equal(["Bogenweg", "Am Bogen"], Search("Bogen 1").Select(h => h.Street));
    }

    [Theory]
    [InlineData("Ölberg 2")]
    [InlineData("ölberg 2")]
    public void A_later_word_starting_with_an_umlaut_is_found_in_any_case(string query)
    {
        Create(("Am Ölberg", "2", "82256", "Fürstenfeldbruck", string.Empty, 48.18, 11.24));

        Assert.Equal("Am Ölberg", Assert.Single(Search(query)).Street);
    }

    // The SQL narrows only up to the first umlaut, so "mühl" reaches the word comparison for
    // "Moosmühlweg" by its "M".
    [Theory]
    [InlineData("bogen 3")]
    [InlineData("mühl 3")]
    public void A_word_inside_another_word_is_no_match(string query)
    {
        Create(("Am Drudenbogen", "3", "82256", "Fürstenfeldbruck", string.Empty, 48.18, 11.24), ("Moosmühlweg", "3", "82256", "Fürstenfeldbruck", string.Empty, 48.18, 11.24));

        Assert.Empty(Search(query));
    }

    // Thousands of "Am Allee" rows hold "Allee" as a later word, sort first and are stored first.
    [Fact]
    public void A_street_starting_with_the_query_is_found_behind_more_later_word_matches_than_the_cap()
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(AddressPath))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 2100) " +
                "INSERT INTO addresses SELECT 'Am Allee', '1', '82140', 'Olching', '', 48.2, 11.3, 'node/2' FROM n;";
            cmd.ExecuteNonQuery();
        }

        Create(("Alleeweg", "1", "82256", "Fürstenfeldbruck", string.Empty, 48.18, 11.24));

        Assert.Equal("Alleeweg", new AddressIndex(AddressPath).Search("Allee 1", limit: 1)[0].Street);
    }

    [Fact]
    public void A_cancelled_search_stops_instead_of_reading_on()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => new AddressIndex(AddressPath).Search("Hauptstraße 1", cancellationToken: cancelled.Token));
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_auptstraße")]
    [InlineData("Haupt%")]
    public void Like_wildcards_in_the_query_are_taken_as_the_letters_they_are(string query) =>
        Assert.Empty(Search(query));

    // Read as wildcards, "%_" would match the space of "Weg Nord" as well.
    [Fact]
    public void A_street_holding_a_wildcard_is_found_by_it_and_nothing_else() =>
        Assert.Equal("Weg%_Nord", Assert.Single(Search("Weg%_Nord 1")).Street);

    [Fact]
    public void Sql_in_the_query_is_only_text_and_the_file_is_left_alone()
    {
        Assert.Empty(Search("x'; DROP TABLE addresses; -- 1"));

        Assert.NotEmpty(Search("Hauptstraße 1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12 Hauptstraße")]
    public void A_query_without_a_street_finds_nothing(string query) => Assert.Empty(Search(query));

    [Fact]
    public void A_query_longer_than_its_cap_finds_nothing() =>
        Assert.Empty(Search("Hauptstraße 1 " + new string('x', AddressIndex.MaxQueryLength)));

    [Fact]
    public void No_more_hits_come_back_than_asked_for() =>
        Assert.Equal(2, new AddressIndex(AddressPath).Search("Hauptstraße", limit: 2).Count);

    [Fact]
    public void A_missing_file_finds_nothing() =>
        Assert.Empty(new AddressIndex(Path.Join(_dir, "missing.sqlite")).Search("Hauptstraße 1"));

    [Fact]
    public void A_file_that_is_no_address_table_finds_nothing()
    {
        var other = Path.Join(_dir, "other.sqlite");
        using (var cn = SqliteConnectionFactory.OpenReadWrite(other))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE tiles (x INTEGER);";
            cmd.ExecuteNonQuery();
        }

        Assert.Empty(new AddressIndex(other).Search("Hauptstraße 1"));
    }

    [Fact]
    public void A_row_whose_cells_hold_the_wrong_types_or_a_position_off_the_globe_is_skipped()
    {
        using (var cn = SqliteConnectionFactory.OpenReadWrite(AddressPath))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO addresses VALUES ('Hauptstraße', '1', '82256', 'Fürstenfeldbruck', '', 'north', 11.2, 'node/9');" +
                "INSERT INTO addresses VALUES ('Hauptstraße', '1', '82256', 'Fürstenfeldbruck', '', 95.0, 11.2, 'node/9');" +
                "INSERT INTO addresses VALUES ('Hauptstraße', X'31', '82256', 'Fürstenfeldbruck', '', 48.1, 11.2, 'node/9');";
            cmd.ExecuteNonQuery();
        }

        var hit = Assert.Single(Search("Hauptstraße 1 Fürstenfeldbruck"), h => h.HouseNumber == "1");

        Assert.Equal(48.1785, hit.Position.Latitude, precision: 4);
    }

    [Theory]
    [InlineData("Hauptstraße", "hauptstr")]
    [InlineData("Allinger Str.", "allinger str")]
    [InlineData("ÜBERSEESTRASSE", "überseestr")]
    public void Normalizing_lowers_and_folds_strasse(string text, string normalized) =>
        Assert.Equal(normalized, AddressIndex.Normalize(text));

    private List<AddressHit> Search(string query) => [.. new AddressIndex(AddressPath).Search(query)];

    // FORMAT.md's table, as lagebuch-geodata builds it.
    private void Create(params (string Street, string Number, string Postcode, string City, string Suburb, double Lat, double Lon)[] rows)
    {
        using var cn = SqliteConnectionFactory.OpenReadWrite(AddressPath);
        using (var create = cn.CreateCommand())
        {
            create.CommandText =
                "CREATE TABLE IF NOT EXISTS addresses (street TEXT NOT NULL, housenumber TEXT NOT NULL, postcode TEXT NOT NULL, city TEXT NOT NULL, " +
                "suburb TEXT NOT NULL, lat REAL NOT NULL, lon REAL NOT NULL, osm TEXT NOT NULL);" +
                "CREATE INDEX IF NOT EXISTS addresses_street ON addresses (street, housenumber);";
            create.ExecuteNonQuery();
        }

        foreach (var row in rows)
        {
            using var insert = cn.CreateCommand();
            insert.CommandText = "INSERT INTO addresses VALUES ($s, $n, $p, $c, $u, $lat, $lon, 'node/1');";
            insert.Parameters.AddWithValue("$s", row.Street);
            insert.Parameters.AddWithValue("$n", row.Number);
            insert.Parameters.AddWithValue("$p", row.Postcode);
            insert.Parameters.AddWithValue("$c", row.City);
            insert.Parameters.AddWithValue("$u", row.Suburb);
            insert.Parameters.AddWithValue("$lat", row.Lat);
            insert.Parameters.AddWithValue("$lon", row.Lon);
            insert.ExecuteNonQuery();
        }
    }
}
