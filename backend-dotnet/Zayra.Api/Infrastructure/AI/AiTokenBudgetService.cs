using System.Text;

namespace Zayra.Api.Infrastructure.AI;

public sealed class AiTokenBudgetService
{
    public int EstimateTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        return Math.Max(1, (int)Math.Ceiling(value.Length / 4.0));
    }

    public string TrimToBudget(string value, int maxTokens)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (maxTokens <= 0) return string.Empty;

        var maxChars = Math.Max(1, maxTokens * 4);
        if (value.Length <= maxChars) return value;
        return value[..maxChars] + "\n[context trimmed to fit AI token budget]";
    }

    public string BuildBudgetedContext(string systemPrompt, string userPrompt, int maxTokens, out int estimatedTokens)
    {
        var combined = new StringBuilder();
        combined.AppendLine(systemPrompt);
        combined.AppendLine();
        combined.AppendLine(userPrompt);
        var text = combined.ToString();
        estimatedTokens = EstimateTokens(text);
        if (estimatedTokens <= maxTokens) return text;
        return TrimToBudget(text, maxTokens);
    }
}
