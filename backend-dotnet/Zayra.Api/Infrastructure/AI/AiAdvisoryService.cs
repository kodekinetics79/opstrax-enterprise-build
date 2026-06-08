using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.AI;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.AI;

public sealed class AiAdvisoryService : IAiAdvisoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly ZayraDbContext _db;
    private readonly IAiGovernanceService _governance;
    private readonly IAiPromptBuilder _promptBuilder;
    private readonly ILlmClient _llmClient;
    private readonly IAiAuditService _audit;
    private readonly IAiResponseCacheService _cache;
    private readonly AiOptions _options;
    private readonly AiRedactionService _redaction;
    private readonly AiTokenBudgetService _tokenBudget;

    public AiAdvisoryService(
        ZayraDbContext db,
        IAiGovernanceService governance,
        IAiPromptBuilder promptBuilder,
        ILlmClient llmClient,
        IAiAuditService audit,
        IAiResponseCacheService cache,
        AiOptions options,
        AiRedactionService redaction,
        AiTokenBudgetService tokenBudget)
    {
        _db = db;
        _governance = governance;
        _promptBuilder = promptBuilder;
        _llmClient = llmClient;
        _audit = audit;
        _cache = cache;
        _options = options;
        _redaction = redaction;
        _tokenBudget = tokenBudget;
    }

    public async Task<AIQueryResponse> QueryAsync(AiUserContext caller, AIQueryRequest request, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var roles = caller.Roles.ToArray();
        var permissions = caller.Permissions.ToArray();
        var governance = _governance.Evaluate(request.Query, roles);
        var humanReviewRequired = governance.HumanReviewRequired || _options.RequireHumanReview;
        var userRole = string.Join(",", roles);
        var permissionSignature = BuildSignature(permissions);
        var roleSignature = BuildSignature(roles);
        var normalizedQuery = NormalizeQuery(request.Query);
        var cacheKey = BuildCacheKey(caller.TenantId, governance.Intent, governance.Module, request.EmployeeId, normalizedQuery, roleSignature, permissionSignature);
        var cacheLookup = new AiCacheKey(
            caller.TenantId,
            cacheKey,
            _redaction.Hash(normalizedQuery),
            normalizedQuery,
            governance.Intent,
            governance.Module,
            request.EmployeeId,
            roleSignature,
            permissionSignature);

        if (!governance.Allowed)
        {
            var blockedResponse = new AIQueryResponse(
                "I'm unable to provide that information based on your current access level.",
                governance.Intent,
                true,
                governance.BlockedReason ?? "Insufficient permissions to access this information.",
                0,
                true,
                GetSuggestions(governance.Intent))
            {
                Provider = "policy",
                Model = string.Empty,
                HumanReviewRequired = humanReviewRequired
            };

            await _audit.LogAsync(new AiAuditEntry(
                caller.TenantId,
                caller.UserId,
                caller.EmployeeId,
                userRole,
                request.Query,
                _redaction.Hash(request.Query),
                _redaction.Summarize(request.Query),
                blockedResponse.Answer,
                governance.Intent,
                governance.Module,
                true,
                blockedResponse.BlockedReason,
                blockedResponse.Provider,
                string.Empty,
                "blocked",
                blockedResponse.HumanReviewRequired,
                true,
                0,
                0,
                0,
                (int)timer.ElapsedMilliseconds), cancellationToken);

            return blockedResponse;
        }

        var cached = await _cache.TryGetAsync(cacheLookup, cancellationToken);
        if (cached is not null)
        {
            var cacheHitResponse = new AIQueryResponse(
                cached.Answer,
                governance.Intent,
                false,
                string.Empty,
                cached.TokensUsed,
                true,
                GetSuggestions(governance.Intent))
            {
                Provider = cached.Provider,
                Model = cached.Model,
                HumanReviewRequired = cached.HumanReviewRequired
            };

            await _audit.LogAsync(new AiAuditEntry(
                caller.TenantId,
                caller.UserId,
                caller.EmployeeId,
                userRole,
                request.Query,
                cacheLookup.QueryHash,
                _redaction.Summarize(request.Query),
                cacheHitResponse.Answer,
                governance.Intent,
                governance.Module,
                false,
                string.Empty,
                cacheHitResponse.Provider,
                cacheHitResponse.Model ?? string.Empty,
                "cache_hit",
                cacheHitResponse.HumanReviewRequired,
                true,
                cacheHitResponse.TokensUsed,
                cached.PromptTokens,
                cached.CompletionTokens,
                (int)timer.ElapsedMilliseconds), cancellationToken);

            return cacheHitResponse;
        }

        var context = await BuildContextAsync(caller.TenantId, governance.Intent, request.EmployeeId, cancellationToken);
        var contextJson = JsonSerializer.Serialize(context, JsonOptions);
        var prompt = _promptBuilder.Build(new AiPromptContext(
            caller.TenantId,
            governance.Intent,
            governance.Module,
            request.Query,
            contextJson,
            governance.IsSensitive,
            humanReviewRequired,
            request.EmployeeId,
            roles));

        var effectiveProvider = ResolveProvider();
        var model = ResolveModel(effectiveProvider);
        var promptForLlm = new LlmRequest(effectiveProvider, model, prompt.SystemPrompt, prompt.UserPrompt, Math.Max(256, _options.MaxContextTokens / 2));

        var llmResponse = await _llmClient.CompleteAsync(promptForLlm, cancellationToken);
        var responseStatus = llmResponse.Success ? "provider_success" : "fallback";
        var answer = llmResponse.Success && !string.IsNullOrWhiteSpace(llmResponse.Text)
            ? llmResponse.Text.Trim()
            : BuildFallbackAnswer(governance.Intent, context);

        var tokensUsed = llmResponse.Success
            ? Math.Max(0, llmResponse.InputTokens + llmResponse.OutputTokens)
            : _tokenBudget.EstimateTokens(prompt.PromptForLogging + "\n" + answer);

        var response = new AIQueryResponse(
            answer,
            governance.Intent,
            false,
            string.Empty,
            tokensUsed,
            true,
            GetSuggestions(governance.Intent))
        {
            Provider = llmResponse.Success ? llmResponse.Provider : "fallback",
            Model = llmResponse.Success ? llmResponse.Model : string.Empty,
            HumanReviewRequired = humanReviewRequired
        };

        await _audit.LogAsync(new AiAuditEntry(
            caller.TenantId,
            caller.UserId,
            caller.EmployeeId,
            userRole,
            request.Query,
            _redaction.Hash(prompt.PromptForLogging),
            prompt.PromptForLogging,
            answer,
            governance.Intent,
            governance.Module,
            false,
            string.Empty,
            response.Provider,
            response.Model ?? string.Empty,
            responseStatus,
            response.HumanReviewRequired,
            true,
            tokensUsed,
            llmResponse.InputTokens,
            llmResponse.OutputTokens,
            (int)timer.ElapsedMilliseconds), cancellationToken);

        await _cache.StoreAsync(
            cacheLookup,
            response,
            responseStatus,
            llmResponse.Success ? llmResponse.InputTokens : 0,
            llmResponse.Success ? llmResponse.OutputTokens : 0,
            (int)timer.ElapsedMilliseconds,
            cancellationToken);

        return response;
    }

    private string ResolveProvider()
    {
        var configured = _options.EffectiveProvider;
        if (configured == "anthropic" && !string.IsNullOrWhiteSpace(_options.AnthropicApiKey)) return "anthropic";
        if (configured == "openai" && !string.IsNullOrWhiteSpace(_options.OpenAIApiKey)) return "openai";
        if (configured == "ollama") return "ollama";
        if (!string.IsNullOrWhiteSpace(_options.AnthropicApiKey)) return "anthropic";
        if (!string.IsNullOrWhiteSpace(_options.OpenAIApiKey)) return "openai";
        if (!string.IsNullOrWhiteSpace(_options.OllamaBaseUrl)) return "ollama";
        return "fallback";
    }

    private string ResolveModel(string provider)
    {
        if (!string.IsNullOrWhiteSpace(_options.Model)) return _options.Model;
        return provider switch
        {
            "anthropic" => "claude-sonnet-4-20250514",
            "openai" => "gpt-5",
            "ollama" => string.IsNullOrWhiteSpace(_options.Model) ? "llama3.1" : _options.Model,
            _ => string.Empty
        };
    }

    private static string NormalizeQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return string.Empty;
        return string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string BuildSignature(IEnumerable<string> values)
    {
        var cleaned = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        return cleaned.Length == 0 ? string.Empty : string.Join('|', cleaned);
    }

    private string BuildCacheKey(Guid tenantId, string intent, string module, int? employeeId, string normalizedQuery, string roleSignature, string permissionSignature)
    {
        return _redaction.Hash(string.Join("::", new[]
        {
            tenantId.ToString("D"),
            intent,
            module,
            employeeId?.ToString() ?? string.Empty,
            roleSignature,
            permissionSignature,
            normalizedQuery
        }));
    }

    private async Task<Dictionary<string, object>> BuildContextAsync(Guid tenantId, string intent, int? employeeId, CancellationToken cancellationToken)
    {
        var context = new Dictionary<string, object>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        switch (intent)
        {
            case "headcount":
                context["totalActive"] = await _db.Employees.CountAsync(e => e.TenantId == tenantId && !e.IsDeleted && e.Status == "Active", cancellationToken);
                context["byDepartment"] = await _db.Employees
                    .Where(e => e.TenantId == tenantId && !e.IsDeleted && e.Status == "Active")
                    .GroupBy(e => e.Department)
                    .Select(g => new { Department = g.Key, Count = g.Count() })
                    .ToListAsync(cancellationToken);
                break;

            case "leave_status":
                context["onLeaveToday"] = await _db.LeaveRequests.CountAsync(r => r.TenantId == tenantId && r.Status == "Approved" && r.StartDate <= today && r.EndDate >= today, cancellationToken);
                break;

            case "leave_balance" when employeeId.HasValue:
                context["balances"] = await _db.EmployeeLeaveBalances
                    .Where(b => b.TenantId == tenantId && b.EmployeeId == employeeId.Value && b.Year == DateTime.UtcNow.Year)
                    .Select(b => new { b.LeaveTypeName, b.Available, b.Used })
                    .ToListAsync(cancellationToken);
                break;

            case "pending_approvals":
                var statuses = new[] { "Submitted", "PendingManagerApproval", "PendingHRApproval" };
                context["pendingLeaveCount"] = await _db.LeaveRequests.CountAsync(r => r.TenantId == tenantId && statuses.Contains(r.Status), cancellationToken);
                context["pendingOvertimeCount"] = await _db.OvertimeRequests.CountAsync(r => r.TenantId == tenantId && r.Status == "PendingApproval", cancellationToken);
                break;

            case "department_info":
                context["departments"] = await _db.Departments
                    .Where(d => d.TenantId == tenantId && !d.IsDeleted)
                    .Select(d => new { Name = d.NameEn, d.Code })
                    .ToListAsync(cancellationToken);
                break;

            case "holiday_info":
                var nextMonth = today.AddMonths(1);
                context["upcomingHolidays"] = await _db.PublicHolidays
                    .Where(h => h.TenantId == tenantId && h.Date >= today && h.Date <= nextMonth)
                    .OrderBy(h => h.Date)
                    .Select(h => new { h.NameEn, h.Date })
                    .ToListAsync(cancellationToken);
                break;

            case "overtime_summary":
                var approvedMinutes = await _db.OvertimeRequests
                    .Where(o => o.TenantId == tenantId && o.Status == "Approved" && o.WorkDate >= today.AddDays(-30))
                    .SumAsync(o => (decimal?)o.ApprovedMinutes, cancellationToken);
                context["approvedOvertimeHours"] = Math.Round((approvedMinutes ?? 0m) / 60m, 2);
                break;
        }

        return context;
    }

    private static string BuildFallbackAnswer(string intent, IReadOnlyDictionary<string, object> context)
    {
        return intent switch
        {
            "headcount" => context.TryGetValue("totalActive", out var total)
                ? $"There are currently {total} active employees in your organisation."
                : "I couldn't retrieve headcount data at this time.",
            "leave_status" => context.TryGetValue("onLeaveToday", out var leaveCount)
                ? $"There are {leaveCount} employees on approved leave today."
                : "I couldn't retrieve leave data at this time.",
            "pending_approvals" => context.TryGetValue("pendingLeaveCount", out var pendingLeave)
                ? $"There are {pendingLeave} pending leave requests and {(context.TryGetValue("pendingOvertimeCount", out var pendingOvertime) ? pendingOvertime : 0)} pending overtime requests awaiting approval."
                : "I couldn't retrieve pending approval data.",
            "leave_balance" => context.TryGetValue("balances", out var balances)
                ? $"Here are the leave balances for the requested employee: {JsonSerializer.Serialize(balances)}"
                : "I couldn't retrieve leave balance data.",
            "department_info" => context.TryGetValue("departments", out var departments)
                ? $"Your organisation has departments: {string.Join(", ", ((System.Collections.IEnumerable)departments).Cast<dynamic>().Select(x => (string)x.Name))}."
                : "I couldn't retrieve department data.",
            "holiday_info" => context.TryGetValue("upcomingHolidays", out var holidays)
                ? $"Upcoming public holidays: {JsonSerializer.Serialize(holidays)}"
                : "No upcoming holidays found in the next month.",
            "overtime_summary" => context.TryGetValue("approvedOvertimeHours", out var overtimeHours)
                ? $"Approved overtime in the last 30 days totals {overtimeHours} hours."
                : "I couldn't retrieve overtime summary data.",
            "payroll_details" => "Payroll details are available to authorised payroll and HR roles only.",
            "salary_details" => "Salary details are available to authorised payroll and HR roles only.",
            "employee_risk" => "Employee risk insights are available to HR leadership roles only.",
            "disciplinary" => "Disciplinary matters require HR review and are not automated by AI.",
            "termination" => "Termination decisions require human review and are not automated by AI.",
            "compensation" => "Compensation changes require human review and are not automated by AI.",
            _ => "I'm your HR assistant. I can help with headcount, leave status, pending approvals, balances, department information, and related advisory queries. Please rephrase your question."
        };
    }

    private static List<string> GetSuggestions(string intent) =>
        intent switch
        {
            "headcount" => ["Show department breakdown", "What are pending approvals?"],
            "leave_status" => ["Show leave balances", "Who is on leave today?"],
            "pending_approvals" => ["List leave requests", "List overtime requests"],
            "leave_balance" => ["Show leave status", "Show pending approvals"],
            "department_info" => ["Show headcount by department", "What holiday is next?"],
            "holiday_info" => ["Show leave status", "Show pending approvals"],
            "overtime_summary" => ["Show headcount by department", "Review pending overtime approvals"],
            "payroll_details" => ["Contact HR or payroll", "Run payroll validation"],
            "salary_details" => ["Contact HR or payroll", "Review compensation policy"],
            "employee_risk" => ["Contact HR leadership", "Review workforce trends"],
            "disciplinary" => ["Contact HR leadership", "Review case notes"],
            "termination" => ["Contact HR leadership", "Review legal checklist"],
            "compensation" => ["Contact HR leadership", "Review compensation policy"],
            _ => ["Ask about headcount", "Ask about leave status"]
        };
}
