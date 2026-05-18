using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Analyzer.Tests.Integration.HostBoot;

/// <summary>
/// US3 acceptance — the Vite-built backoffice bundle is present at the
/// expected path with the expected umbraco-package.json manifest
/// alongside (FR-006).
///
/// File-existence + manifest-content assertions are sufficient for
/// slice 001 to verify the "wiring channel" is established without
/// booting a full HTTP host. The browser-side "no console errors" part
/// of FR-006 is asserted by the Vitest test at
/// <c>src/Analyzer/Client/src/index.test.ts</c> and by manual smoke
/// per <c>quickstart.md</c>.
///
/// These tests assume <c>npm run build</c> ran before <c>dotnet test</c>
/// — the MSBuild target <c>BuildAnalyzerClient</c> in
/// <c>src/Analyzer/Analyzer.csproj</c> handles this when
/// <c>node_modules</c> is present.
/// </summary>
public sealed class BackofficeBundleTests
{
    private static readonly string AnalyzerProjectDir = ResolveAnalyzerProjectDir();

    [Fact]
    public void AnalyzerJs_ExistsAtAppPluginsPath()
    {
        var path = Path.Combine(AnalyzerProjectDir, "wwwroot", "App_Plugins", "Analyzer", "analyzer.js");
        File.Exists(path).Should().BeTrue($"slice 001 must emit the bundle to {path}");
        new FileInfo(path).Length.Should().BeGreaterThan(0, "bundle file must not be empty");
    }

    [Fact]
    public void UmbracoPackageJson_ExistsAtAppPluginsPath()
    {
        var path = Path.Combine(AnalyzerProjectDir, "wwwroot", "App_Plugins", "Analyzer", "umbraco-package.json");
        File.Exists(path).Should().BeTrue($"slice 001 must emit the manifest to {path}");
    }

    [Fact]
    public void UmbracoPackageJson_DeclaresAnalyzerJsAsBundleEntrypoint()
    {
        var path = Path.Combine(AnalyzerProjectDir, "wwwroot", "App_Plugins", "Analyzer", "umbraco-package.json");
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;
        root.GetProperty("id").GetString().Should().Be("Analyzer");
        var extensions = root.GetProperty("extensions");
        extensions.GetArrayLength().Should().BeGreaterThan(0);

        var bundle = extensions.EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("type").GetString() == "bundle");
        bundle.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        bundle.GetProperty("js").GetString().Should().EndWith("/analyzer.js");
    }

    private static string ResolveAnalyzerProjectDir()
    {
        // Tests run from src/Analyzer.Tests/bin/Debug/net10.0/.
        // Walk up to the repo root, then descend to src/Analyzer.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 6 && !File.Exists(Path.Combine(dir, "Analyzer.slnx")); i++)
        {
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        return Path.Combine(dir, "src", "Analyzer");
    }
}
