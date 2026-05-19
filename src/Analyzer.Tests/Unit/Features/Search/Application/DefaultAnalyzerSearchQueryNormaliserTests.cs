using System.Globalization;
using System.Text.Json;
using Analyzer.Features.Search.Application.Normalisation;
using FluentAssertions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Search.Application;

/// <summary>
/// Slice 007 / T010 — covers the five MUST-clauses of the
/// <see cref="Analyzer.Analytics.IAnalyzerSearchQueryNormaliser"/>
/// contract + the 100-pair fixture (SC-002):
/// <list type="bullet">
///   <item>Fixture match (100 / 100 entries).</item>
///   <item>Idempotency — <c>Normalise(Normalise(s)) == Normalise(s)</c>.</item>
///   <item>Culture stability — Turkish locale produces same output as
///     Invariant.</item>
///   <item>Empty / whitespace-only input returns empty string
///     (controller validates; normaliser does not throw).</item>
///   <item>Long-input cap is upstream — 1024-char input MUST NOT throw.</item>
/// </list>
/// </summary>
public sealed class DefaultAnalyzerSearchQueryNormaliserTests
{
    private static readonly Lazy<IReadOnlyList<FixturePair>> Fixture = new(LoadFixture);

    [Fact]
    public void Fixture_has_exactly_one_hundred_pairs()
    {
        Fixture.Value.Should().HaveCount(100,
            "SC-002 fixture is locked at 100 input/expected pairs covering trim, " +
            "case-fold, NFKC fullwidth/halfwidth, ligature decomposition, " +
            "compatibility characters, whitespace-run collapse, leading combining " +
            "marks, accented Latin, and emoji passthrough.");
    }

    [Fact]
    public void Fixture_matches_one_hundred_of_one_hundred_entries()
    {
        var normaliser = new DefaultAnalyzerSearchQueryNormaliser();
        var failures = new List<string>();

        foreach (var pair in Fixture.Value)
        {
            var actual = normaliser.Normalise(pair.Input);
            if (!string.Equals(actual, pair.Expected, StringComparison.Ordinal))
            {
                failures.Add($"input={Quote(pair.Input)} expected={Quote(pair.Expected)} actual={Quote(actual)}");
            }
        }

        failures.Should().BeEmpty(
            "SC-002 requires 100 / 100 fixture entries to match. " +
            "Failures: \n" + string.Join("\n", failures));
    }

    [Fact]
    public void Idempotency_normalising_twice_is_a_no_op()
    {
        var normaliser = new DefaultAnalyzerSearchQueryNormaliser();
        foreach (var pair in Fixture.Value)
        {
            var once = normaliser.Normalise(pair.Input);
            var twice = normaliser.Normalise(once);
            twice.Should().Be(once,
                $"Normalise(Normalise(s)) MUST equal Normalise(s) — input={Quote(pair.Input)}");
        }
    }

    [Fact]
    public void Culture_stability_under_tr_TR()
    {
        var normaliser = new DefaultAnalyzerSearchQueryNormaliser();
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var failures = new List<string>();
            foreach (var pair in Fixture.Value)
            {
                var actual = normaliser.Normalise(pair.Input);
                if (!string.Equals(actual, pair.Expected, StringComparison.Ordinal))
                {
                    failures.Add($"input={Quote(pair.Input)} expected={Quote(pair.Expected)} actual={Quote(actual)}");
                }
            }
            failures.Should().BeEmpty(
                "Invariant lowercasing must hold under Turkish locale — " +
                "the Turkish dotted-i hazard is the load-bearing reason " +
                "DefaultAnalyzerSearchQueryNormaliser uses CultureInfo.InvariantCulture. " +
                "Failures: \n" + string.Join("\n", failures));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData(" \t\n\r ")]
    public void Empty_or_whitespace_only_input_returns_empty_string(string input)
    {
        var normaliser = new DefaultAnalyzerSearchQueryNormaliser();
        normaliser.Normalise(input).Should().BeEmpty(
            "controller-layer validation handles the empty case; the " +
            "normaliser MUST NOT throw on whitespace-only input.");
    }

    [Fact]
    public void Long_input_cap_is_upstream_one_thousand_chars_does_not_throw()
    {
        var normaliser = new DefaultAnalyzerSearchQueryNormaliser();
        var longInput = new string('a', 1024);

        var act = () => normaliser.Normalise(longInput);

        act.Should().NotThrow(
            "length capping is the controller's job, not the normaliser's.");
    }

    [Fact]
    public void Null_input_throws_ArgumentNullException()
    {
        var normaliser = new DefaultAnalyzerSearchQueryNormaliser();
        var act = () => normaliser.Normalise(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static IReadOnlyList<FixturePair> LoadFixture()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Unit",
            "Features",
            "Search",
            "Application",
            "normaliser-fixture.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Normaliser fixture not found at {path}. " +
                "Verify Analyzer.Tests.csproj copies the JSON to the output directory.",
                path);
        }
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<FixturePair>>(
                   stream,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidOperationException(
                   $"Failed to deserialise normaliser fixture at {path}.");
    }

    private static string Quote(string s) => $"\"{s.Replace("\"", "\\\"")}\"";

    private sealed record FixturePair(string Input, string Expected);
}
