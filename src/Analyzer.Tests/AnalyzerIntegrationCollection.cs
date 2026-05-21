namespace Analyzer.Tests;

/// <summary>
/// xUnit v3 collection definition that serialises every test class
/// deriving from <c>AnalyzerIntegrationTestBase</c>. Closes #58.
/// </summary>
/// <remarks>
/// <para>
/// Each integration test class boots its own
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// against Umbraco. Umbraco bootstraps process-shared static state
/// (<c>StaticServiceProvider.Instance</c>, the TypeFinder cache, etc.)
/// during each host start. With xUnit v3's default collection
/// parallelism, two classes can race: class A's <c>WebApplicationFactory.Dispose</c>
/// can run while class B's <c>InitializeAsync</c> is mid-boot, leaving
/// B's <c>Host.StartAsync</c> resolving services off a now-disposed
/// <see cref="System.IServiceProvider"/> and throwing
/// <see cref="System.ObjectDisposedException"/> deep inside Umbraco's
/// <c>PackageDataInstallation</c> ctor. Symptom: ~3-6 cascade or
/// schema tests rotate between runs of the full suite; each passes
/// when run with <c>-class &lt;FQN&gt;</c>.
/// </para>
/// <para>
/// Marking <see cref="TestHelpers.AnalyzerIntegrationTestBase"/> with
/// <c>[Collection("AnalyzerIntegration")]</c> ties every derived class
/// into one collection; xUnit v3 serialises classes within a
/// collection, so only one Umbraco WAF host boots at a time. Unit
/// tests (which do not extend the integration base) keep their
/// default parallel execution.
/// </para>
/// </remarks>
[Xunit.CollectionDefinition("AnalyzerIntegration", DisableParallelization = true)]
public sealed class AnalyzerIntegrationCollection
{
}
