using System.Collections.Concurrent;
using ContextBudget.Application;
using Microsoft.ML.Tokenizers;

namespace ContextBudget.Infrastructure;

/// <summary>Uses a locally supplied OpenAI tiktoken vocabulary; never downloads vocabulary at runtime.</summary>
public sealed class TiktokenModelTokenizer : IModelTokenizer
{
    private readonly string? vocabPath;
    private readonly ConcurrentDictionary<string, TiktokenTokenizer> tokenizers = new(StringComparer.OrdinalIgnoreCase);
    public string Name => "tiktoken";

    public TiktokenModelTokenizer(string? vocabPath) => this.vocabPath = string.IsNullOrWhiteSpace(vocabPath) ? null : Path.GetFullPath(vocabPath);

    public bool Supports(string model) => vocabPath is not null && File.Exists(vocabPath) &&
        (model.StartsWith("gpt-4", StringComparison.OrdinalIgnoreCase) || model.StartsWith("gpt-3.5-turbo", StringComparison.OrdinalIgnoreCase) || model.StartsWith("o1", StringComparison.OrdinalIgnoreCase) || model.StartsWith("o3", StringComparison.OrdinalIgnoreCase));

    public long Count(string model, string text)
    {
        var tokenizer = tokenizers.GetOrAdd(model, name =>
        {
            using var stream = File.OpenRead(vocabPath!);
            return TiktokenTokenizer.CreateForModel(name, stream);
        });
        return tokenizer.CountTokens(text ?? "");
    }
}
