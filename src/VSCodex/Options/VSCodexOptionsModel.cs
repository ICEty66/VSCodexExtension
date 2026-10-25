// Copyright (c) 2019-2026 Chris Pulman and contributors. All rights reserved.
// Chris Pulman and contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using VSCodex.Core.Models;
using VSCodex.Models;
using VSCodex.Services;

namespace VSCodex.Options;

/// <summary>Provides the vS Codex Options Model implementation.</summary>
public sealed class VSCodexOptionsModel
{
    /// <summary>Named number used by this type.</summary>
    private const double Numeric180D = 180D;

    /// <summary>Named string used by this type.</summary>
    private const string MediumText = "medium";

    /// <summary>Gets or sets the codex Cli Path.</summary>
    [Category("运行时")]
    [DisplayName("Codex CLI 路径")]
    [Description("本地 Codex CLI 后端使用的路径或命令。")]
    [DefaultValue("codex")]
    public string CodexCliPath { get; set; } = "codex";

    /// <summary>Gets or sets the node Path.</summary>
    [Category("运行时")]
    [DisplayName("Node 路径")]
    [Description("运行 Codex SDK 桥接所用的路径或命令。")]
    [DefaultValue("node")]
    public string NodePath { get; set; } = "node";

    /// <summary>Gets or sets the bridge Script Path.</summary>
    [Category("运行时")]
    [DisplayName("桥接脚本路径")]
    [Description("可选：覆盖 Resources\\codex-bridge.mjs。")]
    [DefaultValue("")]
    public string BridgeScriptPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the execution transport.</summary>
    [Category("运行时")]
    [DisplayName("执行传输方式")]
    [Description("使用 Codex 应用服务器可获得流式输出、审批与历史记录，或使用仅 CLI 模式以独立进程运行。仅 CLI 模式无法应答交互式审批表单。")]
    [DefaultValue(CodexTransportKind.SdkBridge)]
    public CodexTransportKind DefaultTransport { get; set; } = CodexTransportKind.SdkBridge;

    /// <summary>Gets or sets the default Model.</summary>
    [Category("模型")]
    [DisplayName("默认模型")]
    [Description("主要的交互式编码模型。")]
    [DefaultValue("gpt-6.1-sol")]
    [TypeConverter(typeof(AvailableModelTypeConverter))]
    public string DefaultModel { get; set; } = CodexModelCatalog.DefaultModel;

    /// <summary>Gets or sets the default Failover Model.</summary>
    [Category("模型")]
    [DisplayName("默认故障转移模型")]
    [Description("主模型不可用时使用的备用模型。")]
    [DefaultValue("gpt-6-sol")]
    [TypeConverter(typeof(AvailableModelTypeConverter))]
    public string DefaultFailoverModel { get; set; } = CodexModelCatalog.DefaultFailoverModel;

    /// <summary>Gets or sets the default Reasoning Effort.</summary>
    [Category("模型")]
    [DisplayName("推理强度")]
    [Description("模型请求的默认推理强度。Codex 提供 low、medium、high、xhigh，以及依模型而定的 max 或 ultra 选项。")]
    [DefaultValue(MediumText)]
    [TypeConverter(typeof(ReasoningEffortTypeConverter))]
    public string DefaultReasoningEffort { get; set; } = MediumText;

    /// <summary>Gets or sets the default Verbosity.</summary>
    [Category("模型")]
    [DisplayName("详细程度")]
    [Description("默认回复详细程度。常见取值：low、medium、high。")]
    [DefaultValue(MediumText)]
    [TypeConverter(typeof(VerbosityTypeConverter))]
    public string DefaultVerbosity { get; set; } = MediumText;

    /// <summary>Gets or sets the default Service Tier.</summary>
    [Category("模型")]
    [DisplayName("服务层级")]
    [Description("提供方的服务层级，例如 auto。")]
    [DefaultValue("auto")]
    public string DefaultServiceTier { get; set; } = "auto";

    /// <summary>Gets or sets the default Profile.</summary>
    [Category("模型")]
    [DisplayName("配置文件")]
    [Description("Codex 配置文件名。")]
    [DefaultValue("default")]
    public string DefaultProfile { get; set; } = "default";

    /// <summary>Gets or sets the custom Models.</summary>
    [Category("模型")]
    [DisplayName("自定义模型")]
    [Description("为模型选择器每行填写一个模型 ID。")]
    [RefreshProperties(RefreshProperties.All)]
    public string CustomModels { get; set; } = string.Empty;

    /// <summary>Gets or sets the default Approval Policy.</summary>
    [Category("审批与沙箱")]
    [DisplayName("默认审批策略")]
    [Description("控制 VSCodex 在执行编辑或工具调用前何时请求确认。")]
    [DefaultValue(ApprovalPolicy.OnRequest)]
    public ApprovalPolicy DefaultApprovalPolicy { get; set; } = ApprovalPolicy.OnRequest;

    /// <summary>Gets or sets the default Sandbox Mode.</summary>
    [Category("审批与沙箱")]
    [DisplayName("默认沙箱模式")]
    [Description("控制智能体运行时的文件系统与网络访问。")]
    [DefaultValue(SandboxMode.WorkspaceWrite)]
    public SandboxMode DefaultSandboxMode { get; set; } = SandboxMode.WorkspaceWrite;

    /// <summary>Gets or sets the default Use Multi Agent Orchestration.</summary>
    [Category("智能体")]
    [DisplayName("启用多智能体编排")]
    [Description("默认启用多智能体编排。")]
    [DefaultValue(false)]
    public bool DefaultUseMultiAgentOrchestration { get; set; }

    /// <summary>Gets or sets the default Max Agent Concurrency.</summary>
    [Category("智能体")]
    [DisplayName("智能体最大并发数")]
    [Description("可同时运行的 VSCodex 子智能体最大数量。")]
    [DefaultValue(1)]
    public int DefaultMaxAgentConcurrency { get; set; } = 1;

    /// <summary>Gets or sets the default Agent Strategy.</summary>
    [Category("智能体")]
    [DisplayName("智能体策略")]
    [Description("默认的多智能体交接与审阅策略。")]
    [DefaultValue(AgentExecutionStrategy.ReviewGate)]
    public AgentExecutionStrategy DefaultAgentStrategy { get; set; } = AgentExecutionStrategy.ReviewGate;

    /// <summary>Gets or sets the default Orchestration Model.</summary>
    [Category("智能体")]
    [DisplayName("编排模型")]
    [Description("用于拆分、路由并校验多智能体任务的模型。")]
    [DefaultValue("gpt-6.1-sol")]
    [TypeConverter(typeof(AvailableModelTypeConverter))]
    public string DefaultOrchestrationModel { get; set; } = CodexModelCatalog.DefaultModel;

    /// <summary>Gets or sets the default Budget Driven Model Selection.</summary>
    [Category("智能体")]
    [DisplayName("按预算选择模型")]
    [Description("当任务可以安全地在低成本模型上运行时，改用低成本模型。")]
    [DefaultValue(false)]
    public bool DefaultBudgetDrivenModelSelection { get; set; }

    /// <summary>Gets or sets the default Budget Model.</summary>
    [Category("智能体")]
    [DisplayName("低成本模型")]
    [Description("启用按预算选择时使用的低成本模型。")]
    [DefaultValue("gpt-6-luna")]
    [TypeConverter(typeof(AvailableModelTypeConverter))]
    public string DefaultBudgetModel { get; set; } = CodexModelCatalog.DefaultBudgetModel;

    /// <summary>Gets or sets the default Follow Up Behavior.</summary>
    [Category("上下文、技能与记忆")]
    [DisplayName("后续消息行为")]
    [Description("选择 Enter 是排队为后续消息还是引导当前回合。Ctrl+Enter 执行相反行为。")]
    [DefaultValue(FollowUpBehavior.Queue)]
    public FollowUpBehavior DefaultFollowUpBehavior { get; set; } = FollowUpBehavior.Queue;

    /// <summary>Gets or sets the default Input Area Height.</summary>
    [Category("上下文、技能与记忆")]
    [DisplayName("输入区高度")]
    [Description("VSCodex 工具窗口中提示词编辑器的默认高度。")]
    [DefaultValue(Numeric180D)]
    public double DefaultInputAreaHeight { get; set; } = Numeric180D;

    /// <summary>Gets or sets the skill Roots.</summary>
    [Category("上下文、技能与记忆")]
    [DisplayName("技能根目录")]
    [Description("每行一个目录。VSCodex 会在这些目录中查找可复用的编码技能。")]
    public string SkillRoots { get; set; } = string.Empty;

    /// <summary>Gets or sets the enabled Skill Paths.</summary>
    [Category("上下文、技能与记忆")]
    [DisplayName("已启用的技能")]
    [Description("每行一个已启用的技能路径。")]
    public string EnabledSkillPaths { get; set; } = string.Empty;

    /// <summary>Gets or sets the reactive Memory Server.</summary>
    [Category("上下文、技能与记忆")]
    [DisplayName("ReactiveMemory MCP 服务器")]
    [Description(
        "CP.ReactiveMemory.Mcp.Server 会自动配置为 "
        + "[mcp_servers.cp-reactivememory-mcp-server]，并在每次请求前后调用，"
        + "以保留 Visual Studio 项目上下文。")]
    [DefaultValue("cp-reactivememory-mcp-server")]
    [ReadOnly(true)]
    public string ReactiveMemoryServer { get; set; } = "cp-reactivememory-mcp-server";

    /// <summary>Gets the Visual Studio theme integration mode used by the extension.</summary>
    [Category("外观")]
    [DisplayName("颜色主题")]
    [Description("VSCodex 跟随 Visual Studio 当前的配色主题，包括高对比度主题。")]
    [ReadOnly(true)]
    public string ColorTheme => "跟随 Visual Studio";

    /// <summary>Loads from Settings Store.</summary>
    public void LoadFromSettingsStore()
    {
        var settings = new SettingsStore().Current;
        CodexCliPath = settings.CodexCliPath;
        NodePath = settings.NodePath;
        BridgeScriptPath = settings.BridgeScriptPath;
        DefaultTransport = settings.DefaultTransport;
        DefaultModel = settings.DefaultModel;
        DefaultFailoverModel = settings.DefaultFailoverModel;
        DefaultReasoningEffort = settings.DefaultReasoningEffort;
        DefaultVerbosity = settings.DefaultVerbosity;
        DefaultServiceTier = settings.DefaultServiceTier;
        DefaultProfile = settings.DefaultProfile;
        CustomModels = JoinLines(settings.CustomModels);
        DefaultApprovalPolicy = settings.DefaultApprovalPolicy;
        DefaultSandboxMode = settings.DefaultSandboxMode;
        DefaultUseMultiAgentOrchestration = settings.DefaultUseMultiAgentOrchestration;
        DefaultMaxAgentConcurrency = settings.DefaultMaxAgentConcurrency;
        DefaultAgentStrategy = settings.DefaultAgentStrategy;
        DefaultOrchestrationModel = settings.DefaultOrchestrationModel;
        DefaultBudgetDrivenModelSelection = settings.DefaultBudgetDrivenModelSelection;
        DefaultBudgetModel = settings.DefaultBudgetModel;
        DefaultFollowUpBehavior = settings.DefaultFollowUpBehavior;
        DefaultInputAreaHeight = settings.DefaultInputAreaHeight;
        SkillRoots = JoinLines(settings.SkillRoots);
        EnabledSkillPaths = JoinLines(settings.EnabledSkillPaths);
        ReactiveMemoryServer = "cp-reactivememory-mcp-server";
    }

    /// <summary>Saves to Settings Store.</summary>
    public void SaveToSettingsStore()
    {
        var store = new SettingsStore();
        var settings = store.Current;
        settings.CodexCliPath = TrimOr(CodexCliPath, "codex");
        settings.NodePath = TrimOr(NodePath, "node");
        settings.BridgeScriptPath = TrimOr(BridgeScriptPath, string.Empty);
        settings.DefaultTransport = DefaultTransport;
        settings.DefaultModel = TrimOr(DefaultModel, CodexModelCatalog.DefaultModel);
        settings.DefaultFailoverModel = TrimOr(DefaultFailoverModel, CodexModelCatalog.DefaultFailoverModel);
        settings.DefaultReasoningEffort = CodexModelCatalog.ResolveReasoningEffort(settings.DefaultModel, TrimOr(DefaultReasoningEffort, MediumText));
        settings.DefaultVerbosity = TrimOr(DefaultVerbosity, MediumText);
        settings.DefaultServiceTier = TrimOr(DefaultServiceTier, "auto");
        settings.DefaultProfile = TrimOr(DefaultProfile, "default");
        settings.CustomModels = Lines(CustomModels).ToList();
        settings.DefaultApprovalPolicy = DefaultApprovalPolicy;
        settings.DefaultSandboxMode = DefaultSandboxMode;
        settings.DefaultUseMultiAgentOrchestration = DefaultUseMultiAgentOrchestration;
        settings.DefaultMaxAgentConcurrency = Math.Max(1, DefaultMaxAgentConcurrency);
        settings.DefaultAgentStrategy = DefaultAgentStrategy;
        settings.DefaultOrchestrationModel = TrimOr(DefaultOrchestrationModel, settings.DefaultModel);
        settings.DefaultBudgetDrivenModelSelection = DefaultBudgetDrivenModelSelection;
        settings.DefaultBudgetModel = TrimOr(DefaultBudgetModel, CodexModelCatalog.DefaultBudgetModel);
        settings.DefaultFollowUpBehavior = DefaultFollowUpBehavior;
        settings.DefaultInputAreaHeight = DefaultInputAreaHeight > 0 ? DefaultInputAreaHeight : Numeric180D;
        settings.SkillRoots = Lines(SkillRoots).ToList();
        settings.EnabledSkillPaths = Lines(EnabledSkillPaths).ToList();
        store.Save(settings);
    }

    /// <summary>Performs the join Lines operation.</summary>
    /// <param name="values">The values.</param>
    /// <returns>The join Lines result.</returns>
    private static string JoinLines(IEnumerable<string> values)
        => string.Join(Environment.NewLine, values ?? Enumerable.Empty<string>());

    /// <summary>Performs the lines operation.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The lines result.</returns>
    private static IEnumerable<string> Lines(string value)
        => (value ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Performs the trim Or operation.</summary>
    /// <param name="value">The value.</param>
    /// <param name="fallback">The fallback.</param>
    /// <returns>The trim Or result.</returns>
    private static string TrimOr(string? value, string fallback)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }
}
