using Analyzer.Features.Sessions.Application;
using FluentAssertions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Sessions.Application;

public sealed class DeviceKeyHasherTests
{
    [Fact]
    public void Same_UA_produces_same_hash()
    {
        var ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

        DeviceKeyHasher.Compute(ua).Should().Be(DeviceKeyHasher.Compute(ua));
    }

    [Fact]
    public void Different_UAs_produce_different_hashes()
    {
        var chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120";
        var edge = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Edg/120";

        DeviceKeyHasher.Compute(chrome).Should().NotBe(DeviceKeyHasher.Compute(edge));
    }

    [Fact]
    public void Hash_is_16_hex_lowercase()
    {
        var hash = DeviceKeyHasher.Compute("Mozilla/5.0");

        hash.Should().HaveLength(16);
        hash.Should().MatchRegex("^[0-9a-f]{16}$");
    }

    [Fact]
    public void Null_and_empty_and_whitespace_all_hash_to_same_sentinel()
    {
        var nullHash = DeviceKeyHasher.Compute(null);
        var emptyHash = DeviceKeyHasher.Compute(string.Empty);
        var whitespaceHash = DeviceKeyHasher.Compute("   ");

        nullHash.Should().Be(emptyHash);
        emptyHash.Should().Be(whitespaceHash);
    }

    [Fact]
    public void Non_ASCII_UA_is_tolerated()
    {
        var ua = "Mozilla/5.0 — 中文 — Έλληνας";

        var act = () => DeviceKeyHasher.Compute(ua);

        act.Should().NotThrow();
        DeviceKeyHasher.Compute(ua).Should().HaveLength(16);
    }

    [Fact]
    public void Leading_and_trailing_whitespace_normalised()
    {
        var bare = "Mozilla/5.0";
        var padded = "  Mozilla/5.0  ";

        DeviceKeyHasher.Compute(bare).Should().Be(DeviceKeyHasher.Compute(padded));
    }
}
