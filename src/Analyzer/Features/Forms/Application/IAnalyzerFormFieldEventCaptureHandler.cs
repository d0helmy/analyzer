using Analyzer.Features.Forms.Domain;

namespace Analyzer.Features.Forms.Application;

/// <summary>
/// Slice 005 US2 — orchestrates one in-request per-field event
/// (<c>FieldFocus</c> / <c>FieldUnfocus</c>). Identity gate →
/// HadValue/EventType invariant → session resolution → repo insert →
/// state-store append → audit. Field-level events advance session
/// activity (<c>SessionActivityKind.CustomEvent</c>) — both
/// FieldFocus and FieldUnfocus count as engagement.
/// </summary>
public interface IAnalyzerFormFieldEventCaptureHandler
{
    Task<Guid> HandleAsync(AnalyzerFormFieldEventCapture command, CancellationToken ct);
}
