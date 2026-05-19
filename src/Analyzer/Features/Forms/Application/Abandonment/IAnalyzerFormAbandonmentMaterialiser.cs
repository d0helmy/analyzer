namespace Analyzer.Features.Forms.Application.Abandonment;

/// <summary>
/// Slice 005 — materialises <c>Abandon</c> rows in
/// <c>analyzerFormEvent</c> for <c>(visitorKey, formKey, sessionKey)</c>
/// tuples whose session has just been logically closed but whose form
/// lifecycle has a <c>Start</c> row without a corresponding
/// <c>Success</c> row.
/// </summary>
/// <remarks>
/// Invoked by <c>AnalyzerSessionSweeperService</c> after a batch of
/// sessions is closed; runs inside the sweeper's outer NPoco scope so
/// the inserts atomically roll back with the close-UPDATEs on
/// failure.
/// </remarks>
internal interface IAnalyzerFormAbandonmentMaterialiser
{
    Task MaterialiseAsync(
        IReadOnlyCollection<Guid> closedSessionKeys,
        DateTimeOffset logicalCloseUtc,
        CancellationToken ct);
}
