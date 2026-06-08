using System.Text;
using Zayra.Api.Application.AI;

namespace Zayra.Api.Infrastructure.AI;

public sealed class AiPromptBuilder : IAiPromptBuilder
{
    private readonly AiRedactionService _redaction;
    private readonly AiTokenBudgetService _tokenBudget;
    private readonly AiOptions _options;

    public AiPromptBuilder(AiRedactionService redaction, AiTokenBudgetService tokenBudget, AiOptions options)
    {
        _redaction = redaction;
        _tokenBudget = tokenBudget;
        _options = options;
    }

    public AiPromptBundle Build(AiPromptContext context)
    {
        var systemPrompt = BuildSystemPrompt(context);
        var userPrompt = BuildUserPrompt(context);
        var combined = _tokenBudget.BuildBudgetedContext(systemPrompt, userPrompt, _options.MaxContextTokens, out var estimatedTokens);
        var promptForLogging = _redaction.Summarize(combined, 800);

        return new AiPromptBundle(
            SystemPrompt: combined.Contains(userPrompt, StringComparison.Ordinal) ? systemPrompt : _redaction.Summarize(systemPrompt, 1200),
            UserPrompt: combined.Contains(userPrompt, StringComparison.Ordinal) ? userPrompt : _redaction.Summarize(userPrompt, 1800),
            PromptForLogging: promptForLogging,
            Intent: context.Intent,
            Module: context.Module,
            IsSensitive: context.IsSensitive,
            HumanReviewRequired: context.HumanReviewRequired,
            EstimatedInputTokens: estimatedTokens);
    }

    private static string BuildSystemPrompt(AiPromptContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an advisory-only HR assistant for a multi-tenant enterprise SaaS.");
        sb.AppendLine("Never approve, reject, trigger, or finalize HR, payroll, leave, disciplinary, promotion, demotion, or termination actions.");
        sb.AppendLine("Always respond as guidance for a human reviewer, not as an automated decision maker.");
        sb.AppendLine("Respect tenant isolation. Never infer data from another tenant.");
        sb.AppendLine("If the topic is sensitive, clearly note that human review is required.");
        if (context.HumanReviewRequired) sb.AppendLine("This response requires human review.");
        sb.AppendLine($"Module: {context.Module}");
        sb.AppendLine($"Intent: {context.Intent}");
        return sb.ToString().Trim();
    }

    private static string BuildUserPrompt(AiPromptContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("User question:");
        sb.AppendLine(context.Query);
        sb.AppendLine();
        sb.AppendLine("Tenant-safe context JSON:");
        sb.AppendLine(context.ContextJson);
        sb.AppendLine();
        sb.AppendLine("Instructions:");
        sb.AppendLine("- Reply concisely.");
        sb.AppendLine("- Keep the answer advisory only.");
        sb.AppendLine("- Do not make any automated decisions.");
        sb.AppendLine("- Include human review caution if the topic is sensitive.");
        return sb.ToString().Trim();
    }
}
