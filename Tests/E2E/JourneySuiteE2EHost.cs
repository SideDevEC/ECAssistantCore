using ECAssistant.TestSupport;

namespace ECAssistant.Core.Tests.E2E;

/// <summary>
/// Opt-in host for the TestSupport JourneySuiteE2E (Emre 2026-09-25): the journey
/// suite lives in the ECAssistant.TestSupport package; `dotnet test` only scans a
/// project's own assembly, so consuming test projects declare this one-line
/// subclass to discover the inherited [Fact]s. Env-gated — skips cleanly when no
/// ECA_E2E tier is configured. Projects that don't want the journeys simply don't
/// host the class.
/// </summary>
public sealed class JourneySuiteE2EHost : JourneySuiteE2E { }