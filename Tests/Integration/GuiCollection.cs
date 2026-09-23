namespace ECAssistant.Core.Tests.Integration;

/// <summary>
/// xUnit test collection that serializes tests sharing the static GuiTestHarness field.
/// Without this, parallel test execution causes GuiTestHarness to be overwritten between tests.
/// </summary>
[CollectionDefinition("ProgramGuiCollection", DisableParallelization = true)]
public class ProgramGuiCollection
{
}