namespace Analyzer;

/// <summary>
/// Project-wide string constants for path and identity tokens.
///
/// Slice 001 keeps this minimal: <see cref="AppPluginsPath"/> is the
/// canonical backoffice bundle path (FR-006); <see cref="Claims"/>
/// declares the EntraID claim-type URIs Analyzer reads to populate
/// <see cref="Analyzer.Features.Visitors.Application.Contracts.VisitorIdentity"/>.
///
/// The concrete management-API route prefix (FR-007) is deliberately
/// not declared yet — per spec Clarification Q5, the prefix is pinned
/// by the slice that introduces the first management endpoint
/// (anticipated: slice 005, per-content-node Analytics content app).
/// </summary>
public static class Constants
{
    /// <summary>
    /// Path the Umbraco backoffice serves the Analyzer client bundle
    /// from, relative to the host's <c>wwwroot</c>.
    /// </summary>
    public const string AppPluginsPath = "App_Plugins/Analyzer";

    /// <summary>File name of the bundled backoffice extension entrypoint.</summary>
    public const string BackofficeBundleFileName = "analyzer.js";

    /// <summary>Display name of the package; used in log + exception messages.</summary>
    public const string PackageName = "Analyzer";

    /// <summary>
    /// EntraID OIDC claim-type URIs. Analyzer reads <c>oid</c> and
    /// <c>upn</c> from the authenticated <see cref="System.Security.Claims.ClaimsPrincipal"/>
    /// to surface both values on <c>VisitorIdentity</c> simultaneously
    /// (Customizer's <c>IdentityRef</c> only carries one of them at a
    /// time, so Analyzer reads claims independently — per inter-product
    /// contract D10 and Constitution Principle I).
    /// </summary>
    public static class Claims
    {
        /// <summary>OIDC <c>oid</c> claim, full URI form.</summary>
        public const string Oid = "http://schemas.microsoft.com/identity/claims/objectidentifier";

        /// <summary>OIDC <c>oid</c> claim, short form.</summary>
        public const string OidShort = "oid";

        /// <summary>OIDC <c>upn</c> claim, full URI form.</summary>
        public const string Upn = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn";

        /// <summary>OIDC <c>upn</c> claim, short form (a.k.a. <c>preferred_username</c>).</summary>
        public const string UpnShort = "upn";

        /// <summary>OIDC preferred-username claim — alternative source for UPN.</summary>
        public const string PreferredUsername = "preferred_username";
    }

    /// <summary>
    /// Database object names owned by Analyzer. Slice 002 introduces
    /// the first Analyzer-owned table; future slices append their own
    /// constants here.
    /// </summary>
    public static class Database
    {
        /// <summary>
        /// Table holding one row per <c>PageviewCaptured</c> notification
        /// successfully processed by Analyzer's subscriber. Soft FK to
        /// <c>customizerPageview(key)</c>, hard FK to
        /// <c>customizerVisitorProfile(key)</c>.
        /// </summary>
        public const string AnalyzerEventReceipt = "analyzerEventReceipt";

        /// <summary>
        /// Slice 003 — one row per session (a bounded sequence of
        /// pageviews by one visitor on one device within the configured
        /// inactivity timeout). Hard FK to
        /// <c>customizerVisitorProfile(key)</c>; partial unique index on
        /// <c>(visitorProfileKey, deviceKey) WHERE isActive = 1</c>
        /// enforces "exactly one active session per visitor+device".
        /// </summary>
        public const string AnalyzerSession = "analyzerSession";

        /// <summary>
        /// Slice 004 — one row per <c>analyzer.send(...)</c> custom-event
        /// POST successfully processed by the management endpoint. Hard FK
        /// to <c>analyzerSession.sessionKey</c> (first Analyzer-to-Analyzer
        /// hard FK) and to <c>customizerVisitorProfile(key)</c>; soft FK on
        /// <c>receiptKey</c> for rare in-request co-capture.
        /// </summary>
        public const string AnalyzerCustomEvent = "analyzerCustomEvent";

        /// <summary>
        /// Slice 005 — one row per Umbraco-Forms lifecycle event
        /// (<c>Impression</c> / <c>Start</c> / <c>Success</c> /
        /// <c>Abandon</c>) per <c>(visitorKey, formKey, sessionKey)</c>.
        /// Hard FK to <c>customizerVisitorProfile(key)</c>; soft FK to
        /// <c>analyzerSession(sessionKey)</c>; <c>Abandon</c> rows are
        /// materialised by the slice-003 sweeper rather than POSTed.
        /// </summary>
        public const string AnalyzerFormEvent = "analyzerFormEvent";

        /// <summary>
        /// Slice 005 — one row per <c>FieldFocus</c> / <c>FieldUnfocus</c>
        /// event per <c>(visitorKey, formKey, fieldKey, sessionKey)</c>.
        /// Privacy invariant: the schema carries no column for field
        /// content; <c>hadValue</c> (a single bit, populated only on
        /// <c>FieldUnfocus</c>) is the only signal derived from what
        /// the user typed.
        /// </summary>
        public const string AnalyzerFormFieldEvent = "analyzerFormFieldEvent";

        /// <summary>
        /// Slice 006 — one row per scroll-depth milestone crossing
        /// (25 / 50 / 75 / 100 %) per <c>(visitorKey, pageviewKey,
        /// contentKey, bucket)</c>. Hard FK to
        /// <c>customizerVisitorProfile(key)</c>; soft FKs to
        /// <c>customizerPageview(key)</c> and
        /// <c>analyzerSession(sessionKey)</c>. Unique index on
        /// <c>(pageviewKey, bucket)</c> enforces per-pageview-per-bucket
        /// idempotency at the DB layer (defence in depth alongside the
        /// client-side per-bucket fire-once flag).
        /// </summary>
        public const string AnalyzerScrollSample = "analyzerScrollSample";

        /// <summary>
        /// Slice 007 — one row per accepted intranet-search submission
        /// (raw + normalised query + result count) per
        /// <c>(visitorKey, sessionKey, pageviewKey, contentKey)</c>.
        /// Hard FKs to <c>customizerVisitorProfile(key)</c> and
        /// <c>analyzerSession(sessionKey)</c>; soft FK to
        /// <c>customizerPageview(key)</c> (tombstone-tolerant).
        /// <b>PII tag (FR-SRC-04)</b>: <c>rawQuery</c> and
        /// <c>normalisedQuery</c> are potentially personal data — never
        /// logged via the structured-log substrate (the DB row is the
        /// canonical, role-gated record), and the cascade step
        /// hard-deletes rows on visitor anonymisation rather than
        /// re-keying.
        /// </summary>
        public const string AnalyzerSearchEvent = "analyzerSearchEvent";
    }

    /// <summary>
    /// Audit-log action names emitted via <c>ILogger</c> for state-changing
    /// management surfaces. Slice 004 introduces the first action; later
    /// slices append.
    /// </summary>
    public static class AuditLog
    {
        /// <summary>
        /// Slice 004 — emitted on every successful custom-event capture
        /// via the management endpoint (FR-008).
        /// </summary>
        public const string CustomEventCapture = "custom-event-capture";

        /// <summary>
        /// Slice 005 — emitted on every successful per-form lifecycle
        /// capture (<c>Impression</c> / <c>Start</c> / <c>Success</c>).
        /// <c>Abandon</c> rows materialised by the sweeper emit a
        /// separate batch log entry, not this per-row action.
        /// </summary>
        public const string FormEventCapture = "form-event-capture";

        /// <summary>
        /// Slice 005 — emitted on every successful field-event capture
        /// (<c>FieldFocus</c> / <c>FieldUnfocus</c>).
        /// </summary>
        public const string FormFieldEventCapture = "form-field-event-capture";

        /// <summary>
        /// Slice 006 — emitted on every successful scroll-milestone
        /// capture via the management endpoint (FR-006). A second
        /// <c>Duplicate</c>-tagged entry is emitted when the
        /// <c>UX_analyzerScrollSample_pageviewBucket</c> unique index
        /// rejects a same-tuple replay (409 idempotency path).
        /// </summary>
        public const string ScrollEventCapture = "scroll-event-capture";

        /// <summary>
        /// Slice 007 — emitted on every successful search-event capture
        /// via the management endpoint (FR-009 / SC-006). The log entry
        /// carries <c>EventKey</c> + <c>PageviewKey</c> + <c>ResultCount</c>
        /// + <c>ActorUpn</c> + <c>ActorOid</c> + <c>ReceivedUtc</c> —
        /// it MUST NOT include <c>RawQuery</c> or <c>NormalisedQuery</c>
        /// (search queries are PII per FR-SRC-04). No 409 / duplicate
        /// path: search events have no idempotency index.
        /// </summary>
        public const string SearchEventCapture = "search-event-capture";
    }

    /// <summary>
    /// Slice 004 — Umbraco management-API integration constants for
    /// Analyzer's first backoffice endpoint (and slice 005+ surfaces).
    /// Mirrors Customizer's <c>Customizer.Constants.ApiName</c> +
    /// composition pattern.
    /// </summary>
    public static class ManagementApi
    {
        /// <summary>
        /// API name used by <see cref="Umbraco.Cms.Api.Common.Attributes.MapToApiAttribute"/>
        /// and the Swagger document group. Resolves the
        /// <c>[BackOfficeRoute("analyzer/api/v{version:apiVersion}")]</c>
        /// prefix to <c>/umbraco/management/api/v1/analyzer/...</c>.
        /// </summary>
        public const string ApiName = "analyzer";

        /// <summary>
        /// Slice 008 — relative route segment for the per-content-node
        /// content-analytics management endpoint. Resolved by the
        /// shared <c>BackOfficeRoute</c> prefix to
        /// <c>/umbraco/management/api/v1/analyzer/content-analytics/{contentKey:guid}</c>.
        /// </summary>
        public const string ContentAnalyticsPath = "content-analytics";
    }

    /// <summary>
    /// Slice 008 — strongly-named configuration section keys consumed
    /// via <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>.
    /// </summary>
    public static class Configuration
    {
        /// <summary>
        /// Section bound to
        /// <c>Analyzer.Features.Reporting.Application.AnalyzerReportingOptions</c>
        /// — host operators override
        /// <c>IndividualDataUserGroupAlias</c> here.
        /// </summary>
        public const string ReportingSection = "Analyzer:Reporting";
    }
}

/// <summary>
/// Slice 004 — shorthand wrapper around
/// <see cref="Analyzer.Constants.ManagementApi.ApiName"/>. Top-level so
/// controller attribute arguments stay compact:
/// <c>[MapToApi(AnalyzerApiConstants.ApiName)]</c>.
/// </summary>
internal static class AnalyzerApiConstants
{
    public const string ApiName = Analyzer.Constants.ManagementApi.ApiName;
}
