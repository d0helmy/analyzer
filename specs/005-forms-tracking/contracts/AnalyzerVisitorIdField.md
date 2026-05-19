# Contract — `AnalyzerVisitorIdField`

**Feature**: `005-forms-tracking`
**Date**: 2026-05-19
**Stability**: public (consumed by host operators via Umbraco Forms' field-type designer); pinned via the new `Analyzer.Features.Forms.Infrastructure.UmbracoForms` namespace addition to `PublicSurfacePinningTests`.

The Umbraco Forms field type that injects the current visitor's `customizerVisitorProfile.key` into a submitted Forms entry at submit time, satisfying FR-FRM-05.

## Signature

```csharp
namespace Analyzer.Features.Forms.Infrastructure.UmbracoForms;

public sealed class AnalyzerVisitorIdField : Umbraco.Forms.Core.Providers.FieldTypes.FieldType
{
    public AnalyzerVisitorIdField()
    {
        Id = new Guid("00000005-0000-0000-0000-000000000001"); // stable, slice-005-owned
        Name = "Analyzer Visitor ID";
        Description = "Server-resolved visitor identifier (customizerVisitorProfile.key) for entries submitted by authenticated employees. Read-only from the front-end; populated automatically at submit time by Analyzer.";
        Icon = "icon-user";
        DataType = FieldDataType.String;
        SortOrder = 100;
        RenderInputType = "hidden";
        SupportsRegexValidation = false;
    }
}
```

## Behavioural contract

1. **Auto-discovery**: registered automatically by Umbraco Forms' field-type composer (Umbraco Forms walks the assembly graph and picks up public `FieldType` subclasses). No explicit `IComposer` registration required.
2. **Server-side population**: a separate `INotificationHandler<FormSubmittingNotification>` (named `AnalyzerVisitorIdFieldSubmissionHandler`) finds any field whose `FieldTypeId` matches `AnalyzerVisitorIdField.Id`, resolves `IVisitorIdentifier.IdentifyAsync(HttpContext)`, and writes the resolved `customizerVisitorProfile.key` (Guid → ToString) into the `RecordField.Values[0]`. Pre-existing values from the client are overwritten (read-only contract).
3. **Front-end rendering**: `RenderInputType = "hidden"` means the field is invisible to end users. The form's editor configures it as part of a form definition; the visitor never sees or interacts with it.
4. **Misconfig fallback** (R10): if `IVisitorIdentifier.IsAvailable == false`, the handler writes `Guid.Empty.ToString()` and logs a warning via `ILogger<AnalyzerVisitorIdFieldSubmissionHandler>`. The submission proceeds — the user's primary task (submitting the form) is not blocked by an Analyzer misconfig.

## Conformance tests

| Conformance | Test class |
|---|---|
| Field type auto-discovered by Umbraco Forms | `Integration/Forms/VisitorIdFieldSubmitTests.FieldTypeAutoDiscovered` |
| Submission populates entry with `customizerVisitorProfile.key` | `Integration/Forms/VisitorIdFieldSubmitTests.SubmissionPopulatesVisitorKey` |
| Pre-existing client value is overwritten (read-only contract) | `Integration/Forms/VisitorIdFieldSubmitTests.ClientValueOverwritten` |
| `Guid.Empty` written on `IsAvailable=false`, warning logged | `Integration/Forms/VisitorIdFieldSubmitTests.WritesEmptyOnMisconfigWithWarning` |
| Submission still succeeds on misconfig (no blocking) | `Integration/Forms/VisitorIdFieldSubmitTests.MisconfigDoesNotBlockSubmission` |

## Notes for operators

To add the Visitor ID to a form, the operator opens the form in Umbraco Forms' designer, drags an "Analyzer Visitor ID" field onto the form, and saves. No code or config change needed. Per spec Edge Case "Forms with no Visitor ID field": the field is opt-in per form, not auto-injected — operators choose which forms record the visitor key.
