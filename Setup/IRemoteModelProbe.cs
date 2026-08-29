namespace ECAssistant.Core.Setup;

/// <summary>A model advertised by a remote OpenAI-compatible /models endpoint.</summary>
public sealed record RemoteModelInfo(string Id, bool SupportsVision);

/// <summary>Result of probing a remote provider's /models endpoint.</summary>
public sealed record RemoteProbeResult(bool Reachable, IReadOnlyList<RemoteModelInfo> Models, string? Error = null);

/// <summary>
/// Queries a remote OpenAI-compatible API to enumerate models and detect vision
/// capability (so the user does not have to declare it manually).
/// </summary>
public interface IRemoteModelProbe
{
    /// <summary>Fetches {endpoint}/models and inspects each model's metadata for image input support.</summary>
    Task<RemoteProbeResult> ProbeAsync(string endpoint, string? apiKey, CancellationToken ct = default);
}
