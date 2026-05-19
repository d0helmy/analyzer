using Analyzer.Features.Visitors.Application.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Forms.Core;
using Umbraco.Forms.Core.Enums;
using Umbraco.Forms.Core.Models;

namespace Analyzer.Features.Forms.Infrastructure.UmbracoForms;

/// <summary>
/// Slice 005 US4 — Umbraco Forms field type that injects the current
/// visitor's <c>customizerVisitorProfile.key</c> into a submitted
/// entry. Operators drag the "Analyzer Visitor ID" field onto a form
/// in the Forms designer; the field type's <see cref="Id"/> stable
/// Guid is used by Umbraco Forms to match field instances back to
/// this provider after submit.
/// </summary>
/// <remarks>
/// <para>
/// Submission population: <see cref="ConvertToRecord"/> overrides
/// the framework's default echo to write the resolved
/// <see cref="IVisitorIdentifier"/> key. Forms 17 uses
/// <c>FieldType.ConvertToRecord(field, values, httpContext)</c> as
/// the post-validation pre-persist hook — the contract doc's
/// <c>INotificationHandler&lt;FormSubmittingNotification&gt;</c>
/// shape doesn't exist in Forms 17 (only Workflow notifications
/// are surfaced); the field-type override is the equivalent +
/// idiomatic-for-Forms-17 mechanism. Pre-existing client-supplied
/// values are overwritten (read-only contract).
/// </para>
/// <para>
/// Misconfig fallback: if <see cref="IVisitorIdentifier"/> is
/// unavailable (compose-time misconfiguration, anonymous request),
/// the field writes <c>Guid.Empty.ToString()</c> and emits a warning
/// via <see cref="ILogger"/>. The submission proceeds — the user's
/// primary task (submitting the form) is not blocked by an Analyzer
/// misconfig (R10).
/// </para>
/// <para>
/// Auto-discovered by Umbraco Forms' field-type composer. No
/// explicit DI registration; <see cref="IComposer"/> participation
/// is not required.
/// </para>
/// </remarks>
public sealed class AnalyzerVisitorIdField : FieldType
{
    /// <summary>Stable, slice-005-owned field-type identifier.</summary>
    public static readonly Guid FieldTypeId =
        new("00000005-0000-0000-0000-000000000001");

    public AnalyzerVisitorIdField()
    {
        Id = FieldTypeId;
        Name = "Analyzer Visitor ID";
        Description =
            "Server-resolved visitor identifier (customizerVisitorProfile.key) " +
            "for entries submitted by authenticated employees. Read-only from " +
            "the front-end; populated automatically at submit time by Analyzer.";
        Icon = "icon-user";
        DataType = FieldDataType.String;
        SortOrder = 100;
        // Forms 17 carries a small RenderInputType enum (Single /
        // Multiple / Custom) — there's no first-class "hidden" value.
        // Single is the right shape for a one-value field; the
        // ConvertToRecord override below overwrites any client-supplied
        // value with the server-resolved key, so the rendered control
        // itself is non-load-bearing. A polish-phase partial view at
        // `Views/Partials/Forms/Fieldsets/FieldTypes/FieldType.AnalyzerVisitorId.cshtml`
        // can render `<input type="hidden" />` once a Razor view layer
        // ships with the package.
        RenderInputType = RenderInputType.Single;
        SupportsRegex = false;
    }

    public override IEnumerable<object> ConvertToRecord(
        Field field,
        IEnumerable<object> values,
        HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var sp = httpContext.RequestServices;
        var identifier = sp.GetService<IVisitorIdentifier>();
        var logger = sp.GetService<ILogger<AnalyzerVisitorIdField>>();

        if (identifier is null)
        {
            logger?.LogWarning(
                "AnalyzerVisitorIdField submitted with no IVisitorIdentifier in RequestServices " +
                "(misconfiguration). Writing Guid.Empty.");
            return new object[] { Guid.Empty.ToString() };
        }

        var identity = identifier.GetCurrent();
        if (!identity.IsAvailable || identity.Key == Guid.Empty)
        {
            logger?.LogWarning(
                "AnalyzerVisitorIdField submitted by unauthenticated visitor or with empty key. " +
                "Writing Guid.Empty. (IsAvailable={IsAvailable}; Key={Key})",
                identity.IsAvailable,
                identity.Key);
            return new object[] { Guid.Empty.ToString() };
        }

        // Pre-existing client-supplied value (if any) is intentionally
        // discarded — this field is read-only from the client and the
        // server-side value is the source of truth.
        return new object[] { identity.Key.ToString() };
    }
}
