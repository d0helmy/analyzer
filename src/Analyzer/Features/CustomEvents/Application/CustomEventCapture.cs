using Analyzer.Features.Visitors.Application.Contracts;

namespace Analyzer.Features.CustomEvents.Application;

/// <summary>
/// Slice 004 — in-process command passed from
/// <c>AnalyzerCustomEventController</c> to
/// <c>CustomEventCaptureHandler</c>. Keeps the controller thin: it
/// parses the request, builds the command, hands off to the handler;
/// the handler owns resolver + repository + state-store + audit
/// orchestration.
/// </summary>
/// <inheritdoc cref="ICustomEventCaptureHandler" />
public sealed record CustomEventCapture(
    VisitorIdentity Actor,
    string Category,
    string Action,
    string? Label,
    decimal? Value,
    string? UserAgent,
    DateTimeOffset ReceivedUtc);
