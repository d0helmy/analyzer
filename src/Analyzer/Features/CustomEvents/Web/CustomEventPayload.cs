using System.ComponentModel.DataAnnotations;

namespace Analyzer.Features.CustomEvents.Web;

/// <summary>
/// Slice 004 — inbound JSON payload for the custom-event management
/// endpoint. ASP.NET model binding deserialises + DataAnnotations
/// validates at the boundary; <see cref="AnalyzerCustomEventController"/>
/// performs additional manual whitespace-only guards on
/// <see cref="Category"/> and <see cref="Action"/> (DataAnnotations
/// doesn't reject pure-whitespace strings).
/// </summary>
/// <remarks>
/// Note: NaN/Infinity for <see cref="Value"/> are rejected at JSON
/// deserialisation by <c>System.Text.Json</c>'s default
/// <c>JsonNumberHandling</c>; the action body does not need a separate
/// check. <c>decimal</c> itself does not carry NaN/Infinity values.
/// </remarks>
public sealed class CustomEventPayload
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(64, MinimumLength = 1)]
    public string Category { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(64, MinimumLength = 1)]
    public string Action { get; init; } = string.Empty;

    [StringLength(256)]
    public string? Label { get; init; }

    public decimal? Value { get; init; }
}
