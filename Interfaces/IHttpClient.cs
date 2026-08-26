using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// HTTP client abstraction.
/// </summary>
public interface IHttpClient
{
    Task<string> GetAsync(string url, CancellationToken ct = default);
    Task<string> GetAsync(string url, Dictionary<string, string>? headers, CancellationToken ct = default);
    Task<string> PostAsync(string url, string content, CancellationToken ct = default);
}