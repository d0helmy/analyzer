namespace Analyzer.Composers;

/// <summary>
/// Thrown by <see cref="AnalyzerComposer"/> when a required runtime
/// prerequisite is missing — primarily, when the Customizer package's
/// <see cref="Customizer.Features.Visitors.Application.Contracts.IPersonalizationProfile"/>
/// service descriptor is not registered in the host's DI container
/// (Constitution Principle III; spec FR-002).
///
/// Single-error semantics: <see cref="AnalyzerComposer"/> emits exactly
/// one of these and registers no Analyzer services. The host fails fast
/// rather than booting with a partially-wired Analyzer.
/// </summary>
public sealed class AnalyzerCompositionException : Exception
{
    /// <inheritdoc cref="AnalyzerCompositionException"/>
    public AnalyzerCompositionException(string message)
        : base(message)
    {
    }

    /// <inheritdoc cref="AnalyzerCompositionException"/>
    public AnalyzerCompositionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
