namespace Analyzer.Analytics;

/// <summary>
/// Slice 007 — the public extension point that converts a raw
/// user-typed search query into the canonical grouping key used for
/// "top queries" aggregations. Default implementation
/// (<c>Analyzer.Features.Search.Application.Normalisation.DefaultAnalyzerSearchQueryNormaliser</c>)
/// ships with the slice; multilingual hosts may register their own
/// via a single composer call.
/// </summary>
/// <remarks>
/// <para>
/// <b>First new Analyzer-defined public extension surface since slice
/// 001's <see cref="Analyzer.Features.Visitors.Application.Contracts.IVisitorIdentifier"/>.</b>
/// Pinned via <c>PublicSurfacePinningTests</c>; future changes to the
/// interface (adding methods, renaming parameters, etc.) are MAJOR
/// breaking changes per Constitution Principle X.
/// </para>
/// <para>
/// The default applies in order: <c>Trim</c> →
/// <c>Normalize(NormalizationForm.FormKC)</c> →
/// <c>ToLower(CultureInfo.InvariantCulture)</c> → internal-whitespace-
/// run collapse to a single space character.
/// </para>
/// <para>
/// <b>Implementations MUST</b>:
/// <list type="bullet">
///   <item>
///     <b>Be culture-stable across hosts.</b> No reliance on
///     <see cref="System.Globalization.CultureInfo.CurrentCulture"/>.
///     Repeated calls with the same <paramref name="rawQuery"/> on
///     different threads / hosts / locales MUST produce the same
///     output (defends against the Turkish-dotted-i hazard and similar).
///   </item>
///   <item>
///     <b>Produce a non-empty output for non-empty trimmed input.</b>
///     An empty output is treated as a validation failure at the
///     capture endpoint (HTTP 400, no row written).
///   </item>
///   <item>
///     <b>Be referentially transparent.</b> No I/O, no shared mutable
///     state. Called once per accepted POST on the request hot path.
///   </item>
///   <item>
///     <b>Tolerate any Unicode input</b> up to the 256-char post-trim
///     length cap enforced upstream. MUST NOT throw on emoji,
///     combining marks, surrogate pairs, or compatibility-encoded
///     characters.
///   </item>
///   <item>
///     <b>Have idempotent normalisation.</b>
///     <c>Normalise(Normalise(s)) == Normalise(s)</c> for any
///     <c>s</c>. Applying the normaliser to a previously-normalised
///     value MUST be a no-op.
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Implementations MAY</b>:
/// <list type="bullet">
///   <item>
///     Consult per-request state via <c>Scoped</c> DI — e.g. read the
///     visitor's <c>preferredLanguage</c> claim to pick locale-specific
///     folding.
///   </item>
///   <item>
///     Apply additional folding beyond the default (ASCII-folding,
///     ICU collation, stop-word removal, stemming, etc.).
///   </item>
/// </list>
/// </para>
/// <para>
/// Registered as <c>Scoped</c> (per-request lifetime; matches slice
/// 001's <see cref="Analyzer.Features.Visitors.Application.Contracts.IVisitorIdentifier"/>
/// convention). Hosts replace the default via a single composer
/// registration; per Umbraco's DI convention, the last
/// <c>AddScoped&lt;IAnalyzerSearchQueryNormaliser, ...&gt;</c> call
/// wins.
/// </para>
/// </remarks>
public interface IAnalyzerSearchQueryNormaliser
{
    /// <summary>
    /// Compute the canonical grouping key for
    /// <paramref name="rawQuery"/>.
    /// </summary>
    /// <param name="rawQuery">
    /// The user-typed query, post-trim of outer whitespace. MUST NOT be
    /// <c>null</c>.
    /// </param>
    /// <returns>
    /// The canonical key. MUST be non-empty for non-empty input.
    /// </returns>
    string Normalise(string rawQuery);
}
