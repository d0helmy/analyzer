using Analyzer.Analytics;
using Analyzer.Features.Forms.Application;
using Analyzer.Features.Forms.Domain;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Forms;

/// <summary>
/// Slice 005 / T055 (US2 AS1) — end-to-end field-event persistence.
/// Focus/blur cycles produce paired rows; hadValue is set only on
/// FieldUnfocus. Also asserts the SC-003 zero-field-value-in-DB
/// invariant by inspecting the column-shape (no VARCHAR/NVARCHAR
/// column holding payload-derived strings).
/// </summary>
[Trait("Category", "Integration")]
public sealed class FieldEventCaptureTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Focus_then_unfocus_persists_two_rows_with_correct_hadValue()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var ct = TestContext.Current.CancellationToken;

        var formKey = Guid.NewGuid();
        var fieldKey = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;

        await DispatchAsync(visitor, formKey, fieldKey,
            AnalyzerFormFieldEventType.FieldFocus, hadValue: null,
            t0, ct);
        await DispatchAsync(visitor, formKey, fieldKey,
            AnalyzerFormFieldEventType.FieldUnfocus, hadValue: true,
            t0.AddSeconds(2), ct);

        var rows = ReadRows(visitor, fieldKey);
        rows.Should().HaveCount(2);
        rows[0].EventType.Should().Be((byte)AnalyzerFormFieldEventType.FieldFocus);
        rows[0].HadValue.Should().BeNull();
        rows[1].EventType.Should().Be((byte)AnalyzerFormFieldEventType.FieldUnfocus);
        rows[1].HadValue.Should().BeTrue();
    }

    [Fact]
    public async Task Schema_carries_no_column_capable_of_holding_field_content()
    {
        // SC-003 column-shape audit: only the expected non-content
        // columns exist on analyzerFormFieldEvent. Catches accidental
        // schema drift if a future migration adds a value-bearing
        // column.
        using var scope = ScopeProvider.CreateScope();
        var columns = scope.Database.Fetch<ColumnRow>(
            "SELECT COLUMN_NAME AS Name, DATA_TYPE AS DataType " +
            "FROM INFORMATION_SCHEMA.COLUMNS " +
            "WHERE TABLE_NAME = @0",
            Constants.Database.AnalyzerFormFieldEvent);
        scope.Complete();

        columns.Should().NotBeEmpty();
        var stringColumns = columns
            .Where(c => c.DataType is "varchar" or "nvarchar" or "text" or "ntext")
            .Select(c => c.Name)
            .ToArray();
        stringColumns.Should().BeEmpty(
            "the privacy invariant (SC-003) forbids any string column on analyzerFormFieldEvent");
    }

    private async Task DispatchAsync(
        Guid visitor,
        Guid formKey,
        Guid fieldKey,
        AnalyzerFormFieldEventType eventType,
        bool? hadValue,
        DateTimeOffset receivedUtc,
        CancellationToken ct)
    {
        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IAnalyzerFormFieldEventCaptureHandler>();
        await handler.HandleAsync(
            new AnalyzerFormFieldEventCapture(
                Actor: NewIdentity(visitor),
                FormKey: formKey,
                FieldKey: fieldKey,
                EventType: eventType,
                HadValue: hadValue,
                UserAgent: "UA/test",
                ReceivedUtc: receivedUtc),
            ct);
    }

    private List<RowProjection> ReadRows(Guid visitor, Guid fieldKey)
    {
        using var scope = ScopeProvider.CreateScope();
        var rows = scope.Database.Fetch<RowProjection>(
            $"SELECT eventKey AS EventKey, eventType AS EventType, " +
            $"       hadValue AS HadValue, receivedUtc AS ReceivedUtc " +
            $"FROM {Constants.Database.AnalyzerFormFieldEvent} " +
            $"WHERE visitorProfileKey = @0 AND fieldKey = @1 ORDER BY receivedUtc",
            visitor, fieldKey);
        scope.Complete();
        return rows;
    }

    private static VisitorIdentity NewIdentity(Guid key) => new(
        IsAvailable: true,
        Key: key,
        Oid: "oid-1",
        Upn: "user@example.com",
        IsAnonymized: false);

    private sealed class RowProjection
    {
        public Guid EventKey { get; set; }
        public byte EventType { get; set; }
        public bool? HadValue { get; set; }
        public DateTimeOffset ReceivedUtc { get; set; }
    }

    private sealed class ColumnRow
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
    }
}
