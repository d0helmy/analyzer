# Contract — `IAnalyzerSearchQueryNormaliser`

**Feature**: `007-search-tracking`
**Date**: 2026-05-19
**Stability**: public; new in slice 007. **First new Analyzer-defined extension surface since slice 001's `IVisitorIdentifier`.** Pinned via `PublicSurfacePinningTests`.

The public extension point that converts a raw user-typed search query into the canonical grouping key used for "top queries" aggregations. Default implementation ships with the slice; multilingual hosts may register their own via a single composer call.

## Namespace

```
Analyzer.Analytics.IAnalyzerSearchQueryNormaliser
```

Lives in the pinned `Analyzer.Analytics` namespace alongside `IAnalyticsEventStateProvider`, `IVisitorIdentifier`, and the public-record types.

## Shape

```csharp
namespace Analyzer.Analytics;

public interface IAnalyzerSearchQueryNormaliser
{
    string Normalise(string rawQuery);
}
```

## Behavioural contract

Implementations MUST:

1. **Be culture-stable across hosts.** No reliance on `CultureInfo.CurrentCulture`. Repeated calls with the same `rawQuery` on different threads / hosts / locales MUST produce the same output.
2. **Produce a non-empty output for non-empty trimmed input.** An empty output for a valid input is treated as a validation failure at the capture endpoint (HTTP 400, no row written).
3. **Be referentially transparent.** No I/O, no shared mutable state. Called once per accepted POST on the request hot path.
4. **Tolerate any Unicode input** up to the 256-char post-trim length cap enforced upstream. Implementations MUST NOT throw on emoji, combining marks, surrogate pairs, or compatibility-encoded characters.
5. **Have idempotent normalisation**: `Normalise(Normalise(s)) == Normalise(s)` for any `s`. Applying the normaliser to a previously-normalised value MUST be a no-op.

Implementations MAY:

- Consult per-request state via `Scoped` DI (see "DI lifetime" below) — e.g. read the visitor's `preferredLanguage` claim to pick locale-specific folding.
- Apply additional folding beyond the default (ASCII-folding, ICU collation, stop-word removal, stemming, etc.).

Implementations MUST NOT:

- Persist normaliser state across requests (would violate culture-stability when hosted on a multi-tenant Umbraco instance).
- Throw on input it does not understand; if input cannot be sensibly normalised, return a deterministic sentinel (e.g. the trimmed `rawQuery` unchanged) so the upstream validator can decide whether to reject.

## Default implementation

`Analyzer.Features.Search.Application.Normalisation.DefaultAnalyzerSearchQueryNormaliser` (internal class; the public surface is the interface).

Applies in order:

1. `Trim()` — strips leading + trailing whitespace (Unicode-aware).
2. `Normalize(NormalizationForm.FormKC)` — Unicode compatibility-decomposition + canonical-composition.
3. `ToLower(CultureInfo.InvariantCulture)` — culture-stable lower-casing.
4. `Regex.Replace(@"\s+", " ")` — collapse internal whitespace runs to a single space.

The default is locked by the 100-pair fixture at `src/Analyzer.Tests/Unit/Features/Search/Application/normaliser-fixture.json`. SC-002 asserts 100 / 100 match.

## Replacement convention

Hosts replace the default via a single composer registration:

```csharp
public sealed class MyMultilingualNormaliserComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddScoped<IAnalyzerSearchQueryNormaliser, MyMultilingualNormaliser>();
    }
}
```

Per Umbraco's DI convention, the last `AddScoped<IAnalyzerSearchQueryNormaliser, ...>` call wins. Analyzer's `AnalyzerSearchComposer` registers the default; a host composer that runs later overrides it.

## DI lifetime

Registered as **Scoped** (per-request). Matches slice-001's `IVisitorIdentifier` lifetime decision (Clarification Q3) and Umbraco's per-request convention.

Future-proofs against a stateful custom implementation that wants to read per-request state (e.g. the visitor's locale claim). The default implementation is stateless; the lifetime choice is forward-looking, not driven by current state.

## Pinning

`IAnalyzerSearchQueryNormaliser` is added to `Analyzer.Tests.PublicSurfacePinningTests` as an additive diff in slice 007's Polish phase. Future changes to the interface (adding methods, default-impl methods, etc.) are MAJOR breaking changes per Principle X.

## Conformance tests

Unit tests (`DefaultAnalyzerSearchQueryNormaliserTests`) MUST cover:

- **Idempotency**: `Normalise(Normalise(s)) == Normalise(s)` for every entry in the 100-pair fixture.
- **Fixture match**: 100 / 100 entries in `normaliser-fixture.json` produce their expected output (SC-002).
- **Culture-stability**: running the test under `tr-TR` Turkish locale produces the same output as under `en-US` for every fixture entry — proves `InvariantCulture` lower-casing is correctly applied.
- **Empty / whitespace-only input**: returns empty string (the controller validator catches this; the normaliser itself should not throw).
- **Long-input cap is upstream**: a 1024-char input MUST normalise without throwing — length capping is the controller's job, not the normaliser's.

A separate **integration** test (`NormalisationAggregationTests`) seeds 3 000 variants of 1 000 queries and verifies `GROUP BY normalisedQuery` yields exactly 1 000 distinct groups (SC-007 — table-level equivalent of SC-002).

## Custom-implementation conformance suite

Slice 007 ships a `ContractConformanceTestBase<TNormaliser>` test base under `src/Analyzer.Tests/Unit/Features/Search/Application/` that third-party implementers can inherit (test-only artefact; not part of the production assembly). The base asserts the five MUST-clauses above without prescribing the algorithm. Documented in `quickstart.md` as a recommended verification step for any host shipping a custom normaliser.
