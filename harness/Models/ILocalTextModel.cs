using Microsoft.Extensions.AI;

namespace Harness.Models;

public interface ILocalTextModel : IDisposable
{
    string ModelId { get; }
    Task<string> GenerateAsync(IReadOnlyList<ChatMessage> messages,int maxTokens,CancellationToken cancellationToken);
}
