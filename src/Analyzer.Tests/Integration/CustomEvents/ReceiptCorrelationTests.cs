using Analyzer.Analytics;
using Analyzer.Features.CustomEvents.Application;
using Analyzer.Features.Events.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.CustomEvents;

/// <summary>
/// Slice 004 / T021b (US1 AS6) — covers Analyze finding A1: receipt
/// correlation paths.
/// <list type="bullet">
///   <item>Typical request (no slice-002 receipt populated in scope) →
///   <c>analyzerCustomEvent.receiptKey IS NULL</c>.</item>
///   <item>Synthetic in-request co-capture (the request scope's
///   <see cref="AnalyticsEventStateStore"/> has
///   <see cref="AnalyticsEventStateStore.SetCurrentReceipt"/> seeded
///   before capture) → <c>receiptKey = receipt.Id</c>.</item>
/// </list>
/// </summary>
[Trait("Category", "Integration")]
public sealed class ReceiptCorrelationTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Typical_capture_has_null_receiptKey()
    {
        var visitor = Guid.NewGuid();
        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICustomEventCaptureHandler>();

        var eventKey = await handler.HandleAsync(
            new CustomEventCapture(
                Actor: NewIdentity(visitor),
                Category: "engagement",
                Action: "click",
                Label: null,
                Value: null,
                UserAgent: "UA/test",
                ReceivedUtc: DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        ReadReceiptKey(eventKey).Should().BeNull();
    }

    [Fact]
    public async Task Capture_with_seeded_receipt_populates_receiptKey()
    {
        var visitor = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        using var scope = Services.CreateScope();

        // Pre-populate the scoped state-store as the rare in-request
        // co-capture path would: imagine the slice-002 handler ran on
        // this thread and SetCurrentReceipt before slice 004 fires.
        var store = scope.ServiceProvider.GetRequiredService<AnalyticsEventStateStore>();
        store.SetCurrentReceipt(new AnalyticsEventReceipt(
            Id: receiptId,
            PageviewKey: Guid.NewGuid(),
            VisitorProfileKey: visitor,
            ReceivedUtc: DateTimeOffset.UtcNow));

        var handler = scope.ServiceProvider.GetRequiredService<ICustomEventCaptureHandler>();
        var eventKey = await handler.HandleAsync(
            new CustomEventCapture(
                Actor: NewIdentity(visitor),
                Category: "engagement",
                Action: "click",
                Label: null,
                Value: null,
                UserAgent: "UA/test",
                ReceivedUtc: DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        ReadReceiptKey(eventKey).Should().Be(receiptId);
    }

    private Guid? ReadReceiptKey(Guid eventKey)
    {
        using var scope = ScopeProvider.CreateScope();
        var row = scope.Database.Single<RowProjection>(
            $"SELECT receiptKey AS ReceiptKey FROM {Constants.Database.AnalyzerCustomEvent} " +
            $"WHERE eventKey = @0", eventKey);
        scope.Complete();
        return row.ReceiptKey;
    }

    private static VisitorIdentity NewIdentity(Guid key) => new(
        IsAvailable: true,
        Key: key,
        Oid: "oid-1",
        Upn: "user@example.com",
        IsAnonymized: false);

    private sealed class RowProjection
    {
        public Guid? ReceiptKey { get; set; }
    }
}
