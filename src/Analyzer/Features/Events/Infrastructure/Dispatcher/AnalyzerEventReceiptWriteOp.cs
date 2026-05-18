using Analyzer.Analytics;

namespace Analyzer.Features.Events.Infrastructure.Dispatcher;

/// <summary>
/// Single payload item the bounded write queue accepts and the
/// dispatcher consumes. Mirrors Customizer's <c>VisitorWriteOp</c>
/// shape without the discriminated-union layering — slice 002 only
/// has one operation kind (receipt insert).
/// </summary>
internal sealed record AnalyzerEventReceiptWriteOp(AnalyticsEventReceipt Receipt);
