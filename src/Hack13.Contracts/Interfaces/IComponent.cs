using Hack13.Contracts.Models;

namespace Hack13.Contracts.Interfaces;

public interface IComponent
{
    string ComponentType { get; }

    Task<ComponentResult> ExecuteAsync(
        ComponentConfiguration config,
        Dictionary<string, string> dataDictionary,
        CancellationToken cancellationToken = default);
}
