using NovaLite.Core.Models;
using System.Collections.Generic;
using System.Threading;

namespace NovaLite.Core.Interfaces;

/// <summary>
/// Represents an active inference session capable of generating tokens from a prompt.
/// Implementations (like LlamaSession) are typically stored in LoadedModel.State.
/// </summary>
public interface IInferenceSession : System.IDisposable
{
    IAsyncEnumerable<string> InferAsync(string prompt, InferenceOptions options, CancellationToken ct = default);
}
