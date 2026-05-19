using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Analyzer.Analytics;

namespace Analyzer.Features.Search.Application.Normalisation;

/// <summary>
/// Slice 007 — default
/// <see cref="IAnalyzerSearchQueryNormaliser"/>. Applies in order:
/// <list type="number">
///   <item><c>Trim()</c> — strip leading/trailing Unicode whitespace.</item>
///   <item><c>Normalize(NormalizationForm.FormKC)</c> — Unicode
///     compatibility-decomposition + canonical-composition; folds
///     fullwidth/halfwidth, ligatures, compatibility-encoded variants.</item>
///   <item><c>ToLower(CultureInfo.InvariantCulture)</c> — culture-stable
///     lower-casing (avoids the Turkish dotted-i hazard).</item>
///   <item><c>Regex.Replace(@"\s+", " ")</c> — collapse internal
///     whitespace runs (incl. CRLF / tab / NBSP) to a single space.</item>
/// </list>
/// Locked by the 100-pair fixture at
/// <c>src/Analyzer.Tests/Unit/Features/Search/Application/normaliser-fixture.json</c>
/// (SC-002).
/// </summary>
/// <remarks>
/// <para>
/// Internal sealed class — the public surface is the
/// <see cref="IAnalyzerSearchQueryNormaliser"/> interface. The default
/// is registered as <c>Scoped</c> per Umbraco's per-request convention
/// (research §R5); a host composer may swap it out via a later
/// <c>AddScoped&lt;IAnalyzerSearchQueryNormaliser, ...&gt;</c> call.
/// </para>
/// <para>
/// The compiled <c>Regex</c> is held as a <c>static readonly</c> field
/// so the per-call cost is one method dispatch + one string allocation
/// for the substitution buffer. Idempotent: re-applying the normaliser
/// to a previously-normalised value yields the same string.
/// </para>
/// </remarks>
internal sealed class DefaultAnalyzerSearchQueryNormaliser : IAnalyzerSearchQueryNormaliser
{
    private static readonly Regex WhitespaceRun = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Normalise(string rawQuery)
    {
        ArgumentNullException.ThrowIfNull(rawQuery);

        if (rawQuery.Length == 0)
        {
            return string.Empty;
        }

        var trimmed = rawQuery.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var nfkc = trimmed.Normalize(NormalizationForm.FormKC);
        var lower = nfkc.ToLower(CultureInfo.InvariantCulture);
        return WhitespaceRun.Replace(lower, " ");
    }
}
