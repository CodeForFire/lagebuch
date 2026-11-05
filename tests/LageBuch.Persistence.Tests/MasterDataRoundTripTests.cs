using System.Collections;
using System.Reflection;
using System.Text;
using LageBuch.Persistence.MasterData;
using LageBuch.TestFixtures;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

/// <summary>
/// The persistence legs of the Stammdaten import/export round trip: the JSON format and the
/// SQLite store, each proved against <see cref="StammdatenFixture"/>, a file that sets every field
/// of every category. The editor's legs are in the AppLogic and Acceptance tests.
/// </summary>
public sealed class MasterDataRoundTripTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"md-roundtrip-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static MasterDataSet ParseFixture() =>
        MasterDataJson.Parse(new MemoryStream(Encoding.UTF8.GetBytes(StammdatenFixture.Json)));

    [Fact]
    public void The_full_fidelity_fixture_is_written_exactly_as_the_export_writes_it()
    {
        // Pins the fixture itself: every other round-trip test compares an export against it
        // character for character, which is only meaningful if parse-then-write is the identity.
        Assert.Equal(StammdatenFixture.Json, StammdatenFixture.Canonical(MasterDataJson.Serialize(ParseFixture())));
    }

    [Fact]
    public void The_full_fidelity_fixture_sets_every_field_to_something_other_than_its_default()
    {
        // A field the fixture leaves at its default proves nothing: a leg that drops it writes the
        // default back and the comparison still matches. This fails as soon as a category or a
        // field is added to the Stammdaten without the fixture learning it.
        var set = ParseFixture();
        var missing = new List<string>();
        foreach (var category in typeof(MasterDataSet).GetProperties().Where(p => p.CanWrite))
        {
            var value = category.GetValue(set);
            if (value is IEnumerable items and not string)
            {
                var rows = items.Cast<object>().ToList();
                if (rows.Count == 0)
                {
                    missing.Add(category.Name);
                    continue;
                }

                missing.AddRange(FieldsLeftAtDefault(rows).Select(f => $"{category.Name}.{f}"));
            }
            else if (value is IncidentSettings settings)
            {
                missing.AddRange(FieldsEqualTo(settings, IncidentSettings.Defaults).Select(f => $"{category.Name}.{f}"));
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void Saving_the_fixture_to_the_store_and_reading_it_back_loses_nothing()
    {
        MasterDataStore.Save(_path, ParseFixture());

        var reread = MasterDataStore.GetOrCreate(_path);

        Assert.Equal(StammdatenFixture.Json, StammdatenFixture.Canonical(MasterDataJson.Serialize(reread)));
    }

    // The primary constructor's parameters are the record's stored fields; everything else on it
    // (DisplayName, HasEmail, Label ...) is derived. A field counts as covered when at least one
    // row holds a value other than the parameter's default -- false for a bool defaulting to true.
    // A list of plain strings has no fields: holding a row is all it can prove.
    private static IEnumerable<string> FieldsLeftAtDefault(List<object> rows) =>
        rows[0] is string
            ? []
            : StoredFields(rows[0].GetType())
                .Where(f => rows.All(r => IsDefault(f.Property.GetValue(r), f.Default)))
                .Select(f => f.Property.Name);

    private static IEnumerable<string> FieldsEqualTo(IncidentSettings actual, IncidentSettings defaults) =>
        StoredFields(typeof(IncidentSettings))
            .Where(f => Equals(f.Property.GetValue(actual), f.Property.GetValue(defaults)))
            .Select(f => f.Property.Name);

    private static IEnumerable<(PropertyInfo Property, object? Default)> StoredFields(Type type)
    {
        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        foreach (var parameter in constructor.GetParameters())
        {
            if (parameter.Name is { } name && type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { } property)
            {
                yield return (property, parameter.HasDefaultValue ? parameter.DefaultValue : DefaultOf(parameter.ParameterType));
            }
        }
    }

    private static bool IsDefault(object? value, object? fallback) => value switch
    {
        null => true,
        string s => s.Length == 0 || Equals(s, fallback),
        IEnumerable e => !e.Cast<object>().Any(),
        _ => Equals(value, fallback),
    };

    private static object? DefaultOf(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;
}
