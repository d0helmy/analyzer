# Contract: IIndividualDataAccessCheck

**Slice**: 008-content-analytics-app
**Surface**: Internal role-gate check-function
**Namespace**: `Analyzer.Features.Reporting.Application`
**Stability**: INTERNAL — NOT added to `PublicSurfacePinningTests` in this slice

## Why internal in MVP

Per Spec Clarifications §4 + `Principle X`: the check-function is plumbing for the future per-visitor drill-down slice. No MVP response payload has per-visitor fields to filter, so promoting the interface to a public extension surface now would be premature. The per-visitor drill-down slice (whenever it lands) is the one that adds public extensibility: e.g. a host wanting to delegate group resolution to a custom IdP rather than Umbraco's built-in claims would implement and register `IIndividualDataAccessCheck` then. At THAT point this interface moves out of `Application/` and into `Analyzer.Reporting.ContentAnalytics.Authorization` (public namespace) and joins `PublicSurfacePinningTests`.

## C# shape

```csharp
namespace Analyzer.Features.Reporting.Application;

/// <summary>
/// Checks whether the requesting backoffice user is permitted to see
/// individual-level visitor data (UPN, identity reference, per-visitor
/// drill-down). The MVP slice ships no such fields, so this check has
/// no observable effect on the wire response; it exists so the future
/// per-visitor drill-down slice can land its UI without re-plumbing.
/// </summary>
internal interface IIndividualDataAccessCheck
{
    bool IsAuthorised(ClaimsPrincipal principal);
}

internal sealed class DefaultIndividualDataAccessCheck : IIndividualDataAccessCheck
{
    private const string FallbackGroupAlias = "Analytics.IndividualData";

    private readonly IOptions<AnalyzerReportingOptions> _options;

    public DefaultIndividualDataAccessCheck(IOptions<AnalyzerReportingOptions> options)
    {
        _options = options;
    }

    public bool IsAuthorised(ClaimsPrincipal principal)
    {
        var configured = _options.Value.IndividualDataUserGroupAlias;
        var groupAlias = string.IsNullOrWhiteSpace(configured)
            ? FallbackGroupAlias
            : configured!;

        return principal.Claims.Any(c =>
            c.Type == Umbraco.Cms.Core.Constants.Security.UserGroupClaimType &&
            string.Equals(c.Value, groupAlias, StringComparison.Ordinal));
    }
}
```

## Behavioural contract

| Principal state | Configured alias | Result |
|---|---|---|
| Authenticated, has `userGroup` claim matching configured alias | non-empty | `true` |
| Authenticated, has `userGroup` claim matching the fallback `"Analytics.IndividualData"` | null / empty / whitespace | `true` |
| Authenticated, no `userGroup` claim at all | any | `false` |
| Authenticated, has `userGroup` claim mismatching alias | any | `false` |
| Anonymous (`principal.Identity?.IsAuthenticated == false`) | any | `false` (no claims to match) |

## Registration

```csharp
// AnalyzerReportingComposer
builder.Services.AddSingleton<IIndividualDataAccessCheck, DefaultIndividualDataAccessCheck>();
builder.Services.Configure<AnalyzerReportingOptions>(
    builder.Config.GetSection("Analyzer:Reporting"));
```

Singleton lifetime (the check is stateless and reads `IOptions<>` at evaluation time — no per-request state).

## Test plan (`DefaultIndividualDataAccessCheckTests`)

| # | Scenario | Expected |
|---|---|---|
| 1 | Principal with single matching `userGroup` claim | true |
| 2 | Principal with multiple `userGroup` claims, one matches | true |
| 3 | Principal with no `userGroup` claim | false |
| 4 | Principal with mismatching `userGroup` claim | false |
| 5 | `IndividualDataUserGroupAlias` config explicitly set | matching alias accepted |
| 6 | `IndividualDataUserGroupAlias` config = null | falls back to `"Analytics.IndividualData"` |
| 7 | `IndividualDataUserGroupAlias` config = whitespace | falls back to `"Analytics.IndividualData"` |
| 8 | `IndividualDataUserGroupAlias` config = empty string | falls back to `"Analytics.IndividualData"` |
| 9 | Case-sensitive comparison enforced (`Ordinal`) — claim value differs only in case | false |
| 10 | Anonymous principal | false |

## Forward-compatibility commitment

When this contract is promoted to public surface (per-visitor drill-down slice):

- Interface moves to namespace `Analyzer.Reporting.ContentAnalytics.Authorization`.
- Method signature MUST remain unchanged (no parameter additions / removals).
- The `Default` implementation MUST remain DI-discoverable so existing tests don't break.
- The promotion is added to `PublicSurfacePinningTests` as an additive baseline diff.
- The slice document that performs the promotion notes "promotion only; no behavioural change" in its Constitution Check.
