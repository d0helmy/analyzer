using Analyzer.Analytics;

namespace Analyzer.Features.Scroll.Domain;

/// <summary>
/// Slice 006 — thrown by <c>AnalyzerScrollSampleRepository</c> when
/// the unique non-clustered index
/// <c>UX_analyzerScrollSample_pageviewBucket</c> rejects an insert
/// because a row with the same <c>(pageviewKey, bucket)</c> tuple
/// already exists. The management controller maps to HTTP 409 with
/// body <c>{ "code": "duplicate" }</c>.
/// </summary>
/// <remarks>
/// <para>
/// A 409 is a SUCCESSFUL idempotency rejection (the visitor already
/// crossed this bucket; we don't want a second row), not an auth or
/// validation failure. The auditor records a <c>Duplicate</c>-tagged
/// entry on this path so the operations team can observe duplicate-
/// POST rates as a client-bug signal.
/// </para>
/// <para>
/// Slice-003's <c>UniqueConstraintViolationDetector</c> is the
/// authoritative SQL-error → exception mapper; the repository
/// inspects the <c>SqlException</c> and re-throws this typed
/// exception only when the constraint hit is
/// <c>UX_analyzerScrollSample_pageviewBucket</c>. UX hits on
/// <c>eventKey</c> (client supplied a colliding Guid) re-throw the
/// original <c>SqlException</c> — that is a client bug, not an
/// idempotency rejection.
/// </para>
/// </remarks>
internal sealed class ScrollSampleDuplicateException : Exception
{
    public ScrollSampleDuplicateException(Guid pageviewKey, AnalyzerScrollBucket bucket)
        : base($"A row already exists for pageviewKey={pageviewKey:D} bucket={bucket} (unique-index UX_analyzerScrollSample_pageviewBucket).")
    {
        PageviewKey = pageviewKey;
        Bucket = bucket;
    }

    public Guid PageviewKey { get; }

    public AnalyzerScrollBucket Bucket { get; }
}
