using System.ComponentModel.DataAnnotations;
using Analyzer.Features.CustomEvents.Web;
using FluentAssertions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.CustomEvents.Web;

/// <summary>
/// Slice 004 / T042 — DataAnnotations validation rules on
/// <see cref="CustomEventPayload"/>. The controller adds a manual
/// whitespace-only guard (covered separately in
/// <see cref="AnalyzerCustomEventControllerTests"/>) — these tests
/// cover the boundary cases handled by attributes alone.
/// </summary>
public sealed class CustomEventPayloadValidatorTests
{
    [Theory]
    [InlineData("", "click", "Category")]
    [InlineData("engagement", "", "Action")]
    [InlineData(null, "click", "Category")]
    [InlineData("engagement", null, "Action")]
    public void Required_string_rejection(string? category, string? action, string expectedMember)
    {
        var payload = new CustomEventPayload
        {
            Category = category!,
            Action = action!,
        };

        var results = Validate(payload);

        results.Should().NotBeEmpty();
        results.SelectMany(r => r.MemberNames).Should().Contain(expectedMember);
    }

    [Fact]
    public void Category_over_64_chars_rejects()
    {
        var payload = new CustomEventPayload
        {
            Category = new string('x', 65),
            Action = "click",
        };

        var results = Validate(payload);

        results.Should().NotBeEmpty();
        results.SelectMany(r => r.MemberNames).Should().Contain("Category");
    }

    [Fact]
    public void Action_over_64_chars_rejects()
    {
        var payload = new CustomEventPayload
        {
            Category = "engagement",
            Action = new string('y', 65),
        };

        var results = Validate(payload);

        results.Should().NotBeEmpty();
        results.SelectMany(r => r.MemberNames).Should().Contain("Action");
    }

    [Fact]
    public void Label_over_256_chars_rejects()
    {
        var payload = new CustomEventPayload
        {
            Category = "engagement",
            Action = "click",
            Label = new string('z', 257),
        };

        var results = Validate(payload);

        results.Should().NotBeEmpty();
        results.SelectMany(r => r.MemberNames).Should().Contain("Label");
    }

    [Fact]
    public void Valid_payload_passes()
    {
        var payload = new CustomEventPayload
        {
            Category = "engagement",
            Action = "click",
            Label = "header-cta",
            Value = 42.5m,
        };

        var results = Validate(payload);

        results.Should().BeEmpty();
    }

    private static List<ValidationResult> Validate(CustomEventPayload payload)
    {
        var context = new ValidationContext(payload);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(payload, context, results, validateAllProperties: true);
        return results;
    }
}
