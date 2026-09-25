using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;
using SharpToken;

namespace LlmUsageMonitor.Infrastructure.Services;

/// <summary>
/// Token counting for cost pre-calculation. Uses a real BPE tokenizer (tiktoken
/// compatible) for OpenAI-style models and a CJK-aware heuristic otherwise.
/// </summary>
public sealed class TokenEstimator : ITokenEstimator
{
    private readonly ICostCalculator _cost;

    public TokenEstimator(ICostCalculator cost) => _cost = cost;

    public (long Tokens, bool Exact) CountTokens(string model, string text)
    {
        if (string.IsNullOrEmpty(text)) return (0, true);
        try
        {
            var encoding = GptEncoding.GetEncodingForModel(NormalizeModel(model));
            return (encoding.Encode(text).Count, true);
        }
        catch
        {
            return (Heuristic(text), false);
        }
    }

    public TokenEstimate Estimate(string model, string prompt, int maxOutputTokens,
        ModelPricing? pricing = null, decimal usdToCny = 7.2m)
    {
        var (input, exact) = CountTokens(model, prompt);
        var output = Math.Max(0, maxOutputTokens);
        var cost = _cost.ComputeUsd(new TokenUsage { InputTokens = input, OutputTokens = output }, pricing, usdToCny);
        return new TokenEstimate
        {
            Model = model,
            InputTokens = input,
            OutputTokens = output,
            CostUsd = cost,
            HasPricing = pricing is not null,
            ExactTokenizer = exact
        };
    }

    private static string NormalizeModel(string model)
    {
        // Strip vendor prefixes such as "openai/gpt-4o" or "azure/gpt-4o".
        var slash = model.IndexOf('/');
        return slash >= 0 && slash < model.Length - 1 ? model[(slash + 1)..] : model;
    }

    /// <summary>CJK ideographs ≈ 1 token each; other text ≈ 4 chars per token.</summary>
    private static long Heuristic(string text)
    {
        long cjk = 0;
        long other = 0;
        foreach (var ch in text)
        {
            if (IsCjk(ch)) cjk++;
            else other++;
        }
        return cjk + (other + 3) / 4;
    }

    private static bool IsCjk(char c) =>
        (c >= 0x4E00 && c <= 0x9FFF) ||
        (c >= 0x3400 && c <= 0x4DBF) ||
        (c >= 0x3040 && c <= 0x30FF) ||
        (c >= 0xAC00 && c <= 0xD7AF);
}
