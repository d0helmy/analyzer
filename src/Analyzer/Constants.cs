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
    }
}
