// Copyright (c) 2019-2026 Chris Pulman and contributors. All rights reserved.
// Chris Pulman and contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VSCodex.Models;

namespace VSCodex.Services;

/// <summary>Provides the model Analytics Service implementation.</summary>
public sealed class ModelAnalyticsService : IModelAnalyticsService
{
    /// <summary>Named number used by this type.</summary>
    private const double Numeric0Point20D = 0.20D;

    /// <summary>Named number used by this type.</summary>
    private const double Numeric0Point30D = 0.30D;

    /// <summary>Named number used by this type.</summary>
    private const double Numeric0Point45D = 0.45D;

    /// <summary>Named number used by this type.</summary>
    private const int Numeric100 = 100;

    /// <summary>Named number used by this type.</summary>
    private const double Numeric100D = 100D;

    /// <summary>Named number used by this type.</summary>
    private const int Numeric1050000 = 1_050_000;

    /// <summary>Named number used by this type.</summary>
    private const double Numeric1000000D = 1000000D;

    /// <summary>Named number used by this type.</summary>
    private const int Numeric12000 = 12_000;

    /// <summary>Named number used by this type.</summary>
    private const int Numeric128 = 128;

    /// <summary>Named number used by this type.</summary>
    private const int Numeric180 = 180;

    /// <summary>Named number used by this type.</summary>
    private const int Numeric25000 = 25_000;

    /// <summary>Named number used by this type.</summary>
    private const int Numeric250000 = 250_000;

    /// <summary>Named number used by this type.</summary>
    private const double Numeric4D = 4D;

    /// <summary>Named number used by this type.</summary>
    private const int Numeric4000 = 4000;

    /// <summary>Named number used by this type.</summary>
    private const int Numeric700 = 700;

    /// <summary>Named number used by this type.</summary>
    private const int Numeric80 = 80;

    /// <summary>Named number used by this type.</summary>
    private const int Numeric900 = 900;

    /// <summary>Stores the profiles.</summary>
    private readonly IReadOnlyList<ModelProfile> _profiles =
    [
        new ModelProfile
        {
            Id = "gpt-6.1-sol", DisplayName = "GPT-6.1 Sol",
            InputPricePerMillion = 2.00D, OutputPricePerMillion = 10.00D,
            ContextWindowTokens = Numeric1050000, BestForComplexity = ModelTaskComplexity.High,
            IsCodexOptimized = true, Notes = "按标准 API 的 token 价格估算；订阅额度与长上下文溢价有所不同。"
        },
        new ModelProfile
        {
            Id = "gpt-6-astra", DisplayName = "GPT-6 Astra",
            InputPricePerMillion = 10.00D, OutputPricePerMillion = 50.00D,
            ContextWindowTokens = Numeric1050000, BestForComplexity = ModelTaskComplexity.High,
            IsCodexOptimized = true, Notes = "面向高强度工作的标准 API token 价格估算；订阅用量有所不同。"
        },
        new ModelProfile
        {
            Id = "gpt-6-sol", DisplayName = "GPT-6 Sol",
            InputPricePerMillion = 2.00D, OutputPricePerMillion = 10.00D,
            ContextWindowTokens = Numeric1050000, BestForComplexity = ModelTaskComplexity.High,
            IsCodexOptimized = true, Notes = "上一代 Sol；按标准 API token 价格估算。"
        },
        new ModelProfile
        {
            Id = "gpt-6-luna", DisplayName = "GPT-6 Luna",
            InputPricePerMillion = 0.10D, OutputPricePerMillion = 0.50D,
            ContextWindowTokens = Numeric1050000, BestForComplexity = ModelTaskComplexity.Low,
            IsCodexOptimized = true, Notes = "面向聚焦、可重复工作的标准 API token 价格估算。"
        },
        new ModelProfile
        {
            Id = "gpt-5.6-sol", DisplayName = "GPT-5.6 Sol",
            InputPricePerMillion = 5.00D, OutputPricePerMillion = 30.00D,
            ContextWindowTokens = Numeric1050000, BestForComplexity = ModelTaskComplexity.High,
            IsCodexOptimized = true, Notes = "面向复杂、开放式工作的 Codex Power 模型。"
        },
        new ModelProfile
        {
            Id = "gpt-5.6-terra", DisplayName = "GPT-5.6 Terra",
            InputPricePerMillion = 2.50D, OutputPricePerMillion = 15.00D,
            ContextWindowTokens = Numeric1050000, BestForComplexity = ModelTaskComplexity.Medium,
            IsCodexOptimized = true, Notes = "兼顾能力与成本的 Codex 日常模型。"
        },
        new ModelProfile
        {
            Id = "gpt-5.6-luna", DisplayName = "GPT-5.6 Luna",
            InputPricePerMillion = 1.00D, OutputPricePerMillion = 6.00D,
            ContextWindowTokens = Numeric1050000, BestForComplexity = ModelTaskComplexity.Low,
            IsCodexOptimized = true, Notes = "面向清晰、可重复、大批量工作的 Codex 模型。"
        },
        new ModelProfile
        {
            Id = "gpt-5.5", DisplayName = "GPT-5.5", InputPricePerMillion = 5.00D,
            OutputPricePerMillion = 30.00D, ContextWindowTokens = Numeric1050000,
            BestForComplexity = ModelTaskComplexity.High,
            Notes = "面向复杂编码与专业工作的前沿模型。"
        },
        new ModelProfile
        {
            Id = "gpt-5.4", DisplayName = "GPT-5.4", InputPricePerMillion = 2.50D,
            OutputPricePerMillion = 15.00D, ContextWindowTokens = Numeric1050000,
            BestForComplexity = ModelTaskComplexity.High,
            Notes = "更经济的前沿模型，适用于编码与专业工作。"
        },
        new ModelProfile
        {
            Id = "gpt-5.4-mini", DisplayName = "GPT-5.4 Mini", InputPricePerMillion = 0.75D,
            OutputPricePerMillion = 4.50D, ContextWindowTokens = 400_000,
            BestForComplexity = ModelTaskComplexity.Medium,
            Notes = "低成本模型，适合聚焦编辑、测试与子智能体任务。"
        },
        new ModelProfile
        {
            Id = "gpt-5.3-codex-spark", DisplayName = "GPT-5.3 Codex Spark",
            InputPricePerMillion = 0D, OutputPricePerMillion = 0D, ContextWindowTokens = 128_000,
            BestForComplexity = ModelTaskComplexity.Low, IsCodexOptimized = true,
            Notes = "研究预览版 Codex 模型，拥有独立的配额速率限制，且未公布每 token 价格。"
        }
    ];

    /// <summary>Gets the profiles.</summary>
    public IReadOnlyList<ModelProfile> Profiles => _profiles;

    /// <summary>Estimates the operation.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The estimate result.</returns>
    public ModelUsageEstimate Estimate(CodexRunRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var inputTokens = EstimateInputTokens(request);
        var outputTokens = EstimateOutputTokens(request, inputTokens);
        var primary = FindProfile(request.Options.Model);
        var budget = FindProfile(request.Options.BudgetModel);
        var failover = FindProfile(request.Options.FailoverModel);
        var complexity = ClassifyComplexity(request);
        var recommended = RecommendModel(complexity, primary, budget, inputTokens);
        var primaryCost = EstimateCost(primary, inputTokens, outputTokens);
        var budgetCost = EstimateCost(budget, inputTokens, outputTokens);
        var savings = primaryCost <= 0D ? 0D : Math.Max(0D, (primaryCost - budgetCost) / primaryCost * Numeric100D);
        var contextWindow = Math.Max(1, primary.ContextWindowTokens);
        var contextUsedPercent = Math.Max(0, Math.Min(Numeric100, (int)Math.Round(inputTokens / (double)contextWindow * Numeric100D)));
        var contextRemainingTokens = Math.Max(0, contextWindow - inputTokens);

        var estimate = new ModelUsageEstimate
        {
            EstimatedInputTokens = inputTokens,
            EstimatedOutputTokens = outputTokens,
            ContextWindowTokens = contextWindow,
            ContextRemainingTokens = contextRemainingTokens,
            ContextUsedPercent = contextUsedPercent,
            ContextRemainingPercent = Math.Max(0, Numeric100 - contextUsedPercent),
            PrimaryModel = primary.Id,
            FailoverModel = failover.Id,
            BudgetModel = budget.Id,
            RecommendedModel = recommended.Id,
            PrimaryEstimatedCost = primaryCost,
            BudgetEstimatedCost = budgetCost,
            EstimatedSavingsPercent = savings,
            Complexity = complexity,
            RecommendationReason = BuildReason(complexity, recommended, primary, budget, savings)
        };
        estimate.Summary = BuildSummary(estimate);
        return estimate;
    }

    /// <summary>Estimates input Tokens.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The estimate Input Tokens result.</returns>
    private int EstimateInputTokens(CodexRunRequest request)
    {
        var chars = SafeLength(request.Prompt);
        if (request.Options.IncludeWorkspaceContext && request.SolutionContext is not null)
        {
            chars += request.SolutionContext.Format().Length;
        }

        chars += request.WorkspaceFiles.Sum(x => SafeLength(x.Preview));
        chars += request.Memories.Sum(x => SafeLength(x.Text));
        chars += request.Skills.Where(x => x.IsEnabled).Sum(x => Math.Min(SafeLength(x.Content), Numeric4000));
        chars += request.McpServers
            .Where(x => x.IsEnabled)
            .Sum(x => SafeLength(x.Name) + SafeLength(x.Command) + x.Args.Sum(SafeLength));
        chars += request.Attachments.Sum(x => SafeLength(x.Path) + Numeric128);
        return Math.Max(1, (int)Math.Ceiling(chars / Numeric4D));
    }

    /// <summary>Estimates output Tokens.</summary>
    /// <param name="request">The request.</param>
    /// <param name="inputTokens">The input Tokens.</param>
    /// <returns>The estimate Output Tokens result.</returns>
    private int EstimateOutputTokens(CodexRunRequest request, int inputTokens)
    {
        var modeMultiplier = request.Options.Mode switch
        {
            CodexRunMode.Build => Numeric0Point45D,
            CodexRunMode.Plan => Numeric0Point30D,
            _ => Numeric0Point20D,
        };
        return Math.Max(Numeric700, Math.Min(Numeric12000, (int)Math.Ceiling(inputTokens * modeMultiplier) + Numeric900));
    }

    /// <summary>Performs the classify Complexity operation.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The classify Complexity result.</returns>
    private ModelTaskComplexity ClassifyComplexity(CodexRunRequest request)
    {
        var prompt = request.Prompt ?? string.Empty;
        var highRiskTerms = new[]
        {
            "architecture", "security", "authentication", "authorization", "migration", "production",
            "release", "debug", "exception", "threading", "concurrency", "memory", "mcp",
            "Visual Studio 扩展", "vsix", "reactiveui", "refactor",
        };
        var mediumTerms = new[] { "test", "review", "explain", "document", "optimize", "selection", "单文件", "focused" };
        var words = prompt.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;

        if (request.Options.Mode == CodexRunMode.Build
            && (words > Numeric80 || highRiskTerms.Any(x => prompt.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)))
        {
            return ModelTaskComplexity.High;
        }

        if (words < Numeric80 && mediumTerms.Any(x => prompt.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return ModelTaskComplexity.Low;
        }

        return words > Numeric180 ? ModelTaskComplexity.High : ModelTaskComplexity.Medium;
    }

    /// <summary>Performs the recommend Model operation.</summary>
    /// <param name="complexity">The complexity.</param>
    /// <param name="primary">The primary.</param>
    /// <param name="budget">The budget.</param>
    /// <param name="inputTokens">The input Tokens.</param>
    /// <returns>The recommend Model result.</returns>
    private ModelProfile RecommendModel(ModelTaskComplexity complexity, ModelProfile primary, ModelProfile budget, int inputTokens)
        => complexity == ModelTaskComplexity.High
            || inputTokens > Math.Min(Numeric250000, budget.ContextWindowTokens - Numeric25000)
            ? primary
            : budget;

    /// <summary>Estimates cost.</summary>
    /// <param name="profile">The profile.</param>
    /// <param name="inputTokens">The input Tokens.</param>
    /// <param name="outputTokens">The output Tokens.</param>
    /// <returns>The estimate Cost result.</returns>
    private double EstimateCost(ModelProfile profile, int inputTokens, int outputTokens)
        => (inputTokens / Numeric1000000D * profile.InputPricePerMillion)
            + (outputTokens / Numeric1000000D * profile.OutputPricePerMillion);

    /// <summary>Builds reason.</summary>
    /// <param name="complexity">The complexity.</param>
    /// <param name="recommended">The recommended.</param>
    /// <param name="primary">The primary.</param>
    /// <param name="budget">The budget.</param>
    /// <param name="savings">The savings.</param>
    /// <returns>The build Reason result.</returns>
    private string BuildReason(ModelTaskComplexity complexity, ModelProfile recommended, ModelProfile primary, ModelProfile budget, double savings)
    {
        return recommended.Id.Equals(primary.Id, StringComparison.OrdinalIgnoreCase)
            ? $"建议使用 {primary.Id}，因为该请求被判定为 {complexity.ToString().ToLowerInvariant()} 复杂度。"
            : $"建议对此 {complexity.ToString().ToLowerInvariant()} 复杂度请求使用 {budget.Id}；"
                + $"预计可节省 {savings.ToString("F0", CultureInfo.InvariantCulture)}%。";
    }

    /// <summary>Builds summary.</summary>
    /// <param name="estimate">The estimate.</param>
    /// <returns>The build Summary result.</returns>
    private string BuildSummary(ModelUsageEstimate estimate)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "预计输入 {0:N0} / 输出 {1:N0} 个 token。主模型 {2} 约 ${3:F4}；低成本模型 {4} 约 ${5:F4}；故障转移 {6}。建议：{7}（最多可节省 {8:F0}%）。",
            estimate.EstimatedInputTokens,
            estimate.EstimatedOutputTokens,
            estimate.PrimaryModel,
            estimate.PrimaryEstimatedCost,
            estimate.BudgetModel,
            estimate.BudgetEstimatedCost,
            estimate.FailoverModel,
            estimate.RecommendedModel,
            estimate.EstimatedSavingsPercent);
    }

    /// <summary>Performs the safe Length operation.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The safe Length result.</returns>
    private int SafeLength(string? value) => (value?.Length) ?? 0;

    /// <summary>Finds profile.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The find Profile result.</returns>
    private ModelProfile FindProfile(string model)
    {
        var profile = _profiles.FirstOrDefault(x => x.Id.Equals(model ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        if (profile is not null)
        {
            return profile;
        }

        var fallback = _profiles[0];
        return new ModelProfile
        {
            Id = string.IsNullOrWhiteSpace(model) ? fallback.Id : model.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(model) ? fallback.DisplayName : model.Trim(),
            InputPricePerMillion = fallback.InputPricePerMillion,
            OutputPricePerMillion = fallback.OutputPricePerMillion,
            ContextWindowTokens = fallback.ContextWindowTokens,
            BestForComplexity = fallback.BestForComplexity,
            Notes = "未知模型 ID；在补充模型档案前，按 GPT-6.1 Sol 的标准 API 价格估算。"
        };
    }
}
