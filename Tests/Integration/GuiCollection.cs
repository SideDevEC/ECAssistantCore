namespace ECAssistant.Tests.Integration;

/// <summary>
/// xUnit test collection that serializes tests sharing the static EGuiTestHarness field.
/// Without this, parallel test execution causes EGuiTestHarness to be overwritten between tests.
/// </summary>
[CollectionDefinition("ProgramGuiCollection", DisableParallelization = true)]
public class ProgramGuiCollection
{
}