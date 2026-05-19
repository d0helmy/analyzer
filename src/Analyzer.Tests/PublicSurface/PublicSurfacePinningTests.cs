using System.Reflection;
using System.Text;
using Analyzer.Analytics;
using FluentAssertions;
using Xunit;

namespace Analyzer.Tests.PublicSurface;

/// <summary>
/// Slice 002 / T032 — FR-009 + SC-005 public-surface pinning. Mirrors
/// Customizer's <c>PublicSurfacePinningTests</c> shape. Reflection over
/// <c>Analyzer.dll</c> produces a canonical-form serialisation of
/// every public type under the pinned namespaces and asserts
/// byte-equality against the baseline snapshot. Any non-additive change
/// trips this test.
/// </summary>
/// <remarks>
/// Pinned namespaces (per Clarifications Q3):
/// <list type="bullet">
///   <item><c>Analyzer.Analytics</c> — hosts
///   <see cref="IAnalyticsEventStateProvider"/> +
///   <see cref="AnalyticsEventReceipt"/> +
///   <see cref="AnalyticsCustomEvent"/> +
///   <see cref="AnalyticsFormEvent"/> +
///   <see cref="AnalyticsFormFieldEvent"/>.</item>
///   <item><c>Analyzer.Features.Visitors.Application.Contracts</c> —
///   slice-001 <c>IVisitorIdentifier</c> + <c>VisitorIdentity</c>
///   (retroactively pinned per Clarifications Q3).</item>
///   <item><c>Analyzer.Features.Forms.Infrastructure.UmbracoForms</c> —
///   slice-005 <c>AnalyzerVisitorIdField</c> (consumed by host
///   operators via Umbraco Forms' field-type designer).</item>
/// </list>
/// Regenerate the baseline ONLY when the change is deliberate and a
/// semver bump is planned: set
/// <c>ANALYZER_REGENERATE_SNAPSHOTS=1</c> and run the test.
/// </remarks>
public sealed class PublicSurfacePinningTests
{
    private static readonly string[] PinnedNamespaces =
    {
        "Analyzer.Analytics",
        "Analyzer.Features.Visitors.Application.Contracts",
        "Analyzer.Features.Forms.Infrastructure.UmbracoForms",
    };

    private static readonly string SnapshotPath = LocateSnapshot();

    [Fact]
    public void RegenerateBaseline_when_explicitly_opted_in()
    {
        if (Environment.GetEnvironmentVariable("ANALYZER_REGENERATE_SNAPSHOTS") != "1")
        {
            return;
        }
        File.WriteAllText(SnapshotPath, SerializePublicSurface());
    }

    [Fact]
    public void Public_surface_matches_baseline_snapshot()
    {
        var actual = SerializePublicSurface();
        File.Exists(SnapshotPath).Should().BeTrue(
            $"Snapshot baseline missing at {SnapshotPath} — regenerate via ANALYZER_REGENERATE_SNAPSHOTS=1.");

        var expected = File.ReadAllText(SnapshotPath).Replace("\r\n", "\n").TrimEnd();
        actual.TrimEnd().Should().Be(expected,
            "Public surface of Analyzer's pinned namespaces changed. " +
            "If deliberate AND additive (MINOR), regenerate the snapshot by setting " +
            "ANALYZER_REGENERATE_SNAPSHOTS=1 and re-running. If non-additive (MAJOR), " +
            "bump the package version + update the snapshot together with a spec amendment.");
    }

    private static string SerializePublicSurface()
    {
        var assembly = typeof(IAnalyticsEventStateProvider).Assembly;
        var sb = new StringBuilder();
        var types = assembly.GetExportedTypes()
            .Where(t => PinnedNamespaces.Any(ns =>
                string.Equals(t.Namespace, ns, StringComparison.Ordinal)))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var t in types)
        {
            sb.AppendLine($"TYPE {t.FullName} : {DescribeKind(t)} : {DescribeBaseAndInterfaces(t)}");
            foreach (var m in EnumerateMembers(t))
            {
                sb.AppendLine($"  {m}");
            }
        }
        return sb.ToString();
    }

    private static string DescribeKind(Type t)
    {
        if (t.IsInterface) return "interface";
        if (t.IsEnum) return "enum";
        if (t.IsValueType) return "struct";
        if (t.IsAbstract && t.IsSealed) return "static class";
        var attrs = new List<string>();
        if (t.IsAbstract) attrs.Add("abstract");
        if (t.IsSealed) attrs.Add("sealed");
        attrs.Add("class");
        return string.Join(" ", attrs);
    }

    private static string DescribeBaseAndInterfaces(Type t)
    {
        var parts = new List<string>();
        if (t.BaseType is not null && t.BaseType != typeof(object) && t.BaseType != typeof(ValueType) && t.BaseType != typeof(Enum))
        {
            parts.Add($"base={t.BaseType.FullName}");
        }
        var interfaces = t.GetInterfaces()
            .Select(i => i.FullName)
            .OrderBy(n => n, StringComparer.Ordinal);
        var ifaceList = string.Join(",", interfaces);
        if (!string.IsNullOrEmpty(ifaceList))
        {
            parts.Add($"interfaces=[{ifaceList}]");
        }
        return parts.Count == 0 ? "<>" : string.Join(" ", parts);
    }

    private static IEnumerable<string> EnumerateMembers(Type t)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var members = new List<string>();

        foreach (var ctor in t.GetConstructors(flags).OrderBy(c => c.ToString(), StringComparer.Ordinal))
        {
            members.Add($"CTOR({DescribeParameters(ctor.GetParameters())})");
        }
        foreach (var p in t.GetProperties(flags).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            members.Add($"PROP {FormatType(p.PropertyType)} {p.Name} {{{(p.CanRead ? " get;" : "")}{(p.CanWrite ? " set;" : "")} }}");
        }
        foreach (var m in t.GetMethods(flags).Where(m => !m.IsSpecialName).OrderBy(m => m.Name + m.ToString(), StringComparer.Ordinal))
        {
            members.Add($"METHOD {(m.IsStatic ? "static " : "")}{(m.IsAbstract ? "abstract " : "")}{(m.IsVirtual && !m.IsAbstract ? "virtual " : "")}{FormatType(m.ReturnType)} {m.Name}({DescribeParameters(m.GetParameters())})");
        }
        foreach (var f in t.GetFields(flags).OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            members.Add($"FIELD {(f.IsStatic ? "static " : "")}{FormatType(f.FieldType)} {f.Name}");
        }
        return members;
    }

    private static string DescribeParameters(ParameterInfo[] parameters) =>
        string.Join(", ", parameters.Select(p => $"{FormatType(p.ParameterType)} {p.Name}"));

    private static string FormatType(Type t)
    {
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition().FullName!;
            var args = string.Join(",", t.GetGenericArguments().Select(FormatType));
            return $"{def.Substring(0, def.IndexOf('`'))}<{args}>";
        }
        if (t.IsGenericParameter) return t.Name;
        return t.FullName ?? t.Name;
    }

    private static string LocateSnapshot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Analyzer.slnx")))
        {
            dir = dir.Parent;
        }
        return dir is null
            ? "PublicSurface/Baselines/Analyzer-public-surface.txt"
            : Path.Combine(dir.FullName, "src", "Analyzer.Tests", "PublicSurface", "Baselines", "Analyzer-public-surface.txt");
    }
}
