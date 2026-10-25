// Copyright (c) 2019-2026 Chris Pulman and contributors. All rights reserved.
// Chris Pulman and contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using VSCodex.Core.Models;
using VSCodex.Models;

namespace VSCodex.Services;

/// <summary>Coordinates SDK and CLI Codex transports.</summary>
public sealed class CodexOrchestrator : ICodexOrchestrator, IThreadCatalogClient
{
    /// <summary>Stores the prompt builder.</summary>
    private readonly PromptBuilder _promptBuilder = new();

    /// <summary>Stores the SDK client.</summary>
    private readonly ICodexClient _sdk;

    /// <summary>Stores the CLI client.</summary>
    private readonly ICodexClient _cli;

    /// <summary>Publishes transport events.</summary>
    private readonly Subject<CodexEvent> _events = new();

    /// <summary>Tracks the active execution transport for interruption and steering.</summary>
    private volatile bool _isCliActive;

    /// <summary>Initializes a new instance of the <see cref="CodexOrchestrator"/> class.</summary>
    /// <param name="sdk">The SDK client.</param>
    /// <param name="cli">The CLI client.</param>
    public CodexOrchestrator(ICodexClient sdk, ICodexClient cli)
    {
        _sdk = sdk;
        _cli = cli;
        _ = _sdk.Events.Merge(_cli.Events).Subscribe(_events);
    }

    /// <summary>Gets transport events.</summary>
    public IObservable<CodexEvent> Events => _events.AsObservable();

    /// <summary>Gets the transport capability without implying CLI support.</summary>
    private IThreadCatalogClient ThreadCatalog => _sdk as IThreadCatalogClient
        ?? throw new NotSupportedException("线程管理需要 Codex 应用服务器传输方式。");

    /// <summary>Gets models from the account-aware app-server transport.</summary>
    /// <returns>The complete runtime catalog.</returns>
    public Task<JObject?> GetModelsAsync() => _sdk.GetModelsAsync();

    /// <inheritdoc />
    public Task<JObject> ListThreadsAsync(bool archived) => ThreadCatalog.ListThreadsAsync(archived);

    /// <inheritdoc />
    public Task<JObject> ReadThreadAsync(string threadId) => ThreadCatalog.ReadThreadAsync(threadId);

    /// <inheritdoc />
    public Task<JObject> ForkThreadAsync(string threadId) => ThreadCatalog.ForkThreadAsync(threadId);

    /// <inheritdoc />
    public Task<JObject> SetThreadArchivedAsync(string threadId, bool archived) => ThreadCatalog.SetThreadArchivedAsync(threadId, archived);

    /// <summary>Runs a Codex request.</summary>
    /// <param name="request">The request.</param>
    /// <returns>A task whose result contains the run result.</returns>
    public async Task<CodexRunResult> RunAsync(CodexRunRequest request)
    {
        CodexRunRequest enrichedRequest = EnrichRequest(request);
        _isCliActive = request.Options.Transport == CodexTransportKind.CliFallback;
        if (request.Options.Transport == CodexTransportKind.CliFallback)
        {
            return await _cli.RunAsync(enrichedRequest).ConfigureAwait(continueOnCapturedContext: false);
        }

        try
        {
            return await _sdk.RunAsync(enrichedRequest).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (Exception sdkException) when (sdkException is not CodexRunFailureException)
        {
            return await RunWithFallbackAsync(enrichedRequest, sdkException).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    /// <summary>Gets rate limits from the preferred transport.</summary>
    /// <returns>A task whose result contains the rate limits.</returns>
    public async Task<JObject?> GetRateLimitsAsync()
    {
        try
        {
            return await _sdk.GetRateLimitsAsync().ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (Exception)
        {
            return await _cli.GetRateLimitsAsync().ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    /// <summary>Steers an active SDK turn.</summary>
    /// <param name="threadId">The thread identifier.</param>
    /// <param name="prompt">The steering prompt.</param>
    /// <returns>A task that completes after steering.</returns>
    public async Task SteerAsync(string threadId, string prompt)
    {
        ICodexClient active = _isCliActive ? _cli : _sdk;
        _ = await active.SteerAsync(threadId, prompt).ConfigureAwait(continueOnCapturedContext: false);
    }

    /// <summary>Interrupts an active SDK turn.</summary>
    /// <param name="threadId">The optional thread identifier.</param>
    /// <returns>A task that completes after interruption.</returns>
    public async Task InterruptAsync(string? threadId)
    {
        ICodexClient active = _isCliActive ? _cli : _sdk;
        _ = await active.InterruptAsync(threadId).ConfigureAwait(continueOnCapturedContext: false);
    }

    /// <summary>Responds to an SDK server request.</summary>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="method">The server request method.</param>
    /// <param name="approve">Whether to approve the request.</param>
    /// <param name="content">The accepted form values.</param>
    /// <param name="cancel">Whether the user cancelled elicitation.</param>
    /// <returns>A task that completes after the response is sent.</returns>
    public async Task RespondToServerRequestAsync(JToken requestId, string method, bool approve, JObject? content, bool cancel)
    {
        JObject response = ServerRequestProtocol.CreateResult(method, approve, content, cancel);
        _ = await _sdk.RespondToServerRequestAsync(requestId, response)
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    /// <summary>Cancels active SDK and CLI work without blocking.</summary>
    public void Cancel()
    {
        _sdk.CancelActiveRun();
        _cli.CancelActiveRun();
    }

    /// <summary>Builds a failover request when a distinct model is configured.</summary>
    /// <param name="request">The source request.</param>
    /// <returns>The failover request, or <see langword="null"/>.</returns>
    private static CodexRunRequest? BuildFailoverRequest(CodexRunRequest request)
    {
        string failoverModel = request.Options.FailoverModel;
        bool hasDistinctFailover = !string.IsNullOrWhiteSpace(failoverModel)
            && !failoverModel.Equals(request.Options.Model, StringComparison.OrdinalIgnoreCase);
        return hasDistinctFailover ? CloneRequest(request, CreateFailoverOptions(request.Options, failoverModel)) : null;
    }

    /// <summary>Creates options for the configured failover model.</summary>
    /// <param name="source">The source options.</param>
    /// <param name="failoverModel">The failover model.</param>
    /// <returns>The failover options.</returns>
    private static CodexRunOptions CreateFailoverOptions(CodexRunOptions source, string failoverModel)
    {
        return new CodexRunOptions
        {
            Model = failoverModel,
            FailoverModel = failoverModel,
            ReasoningEffort = CodexModelCatalog.ResolveReasoningEffort(failoverModel, source.ReasoningEffort),
            Verbosity = source.Verbosity,
            ServiceTier = source.ServiceTier,
            Profile = source.Profile,
            ApprovalPolicy = source.ApprovalPolicy,
            SandboxMode = source.SandboxMode,
            Mode = source.Mode,
            Transport = source.Transport,
            IncludeWorkspaceContext = source.IncludeWorkspaceContext,
            IncludeMemory = source.IncludeMemory,
            IncludeSkills = source.IncludeSkills,
            IncludeMcpServers = source.IncludeMcpServers,
            UseMultiAgentOrchestration = source.UseMultiAgentOrchestration,
            MaxAgentConcurrency = source.MaxAgentConcurrency,
            AgentStrategy = source.AgentStrategy,
            OrchestrationModel = source.OrchestrationModel,
            BudgetDrivenModelSelection = source.BudgetDrivenModelSelection,
            BudgetModel = source.BudgetModel
        };
    }

    /// <summary>Clones a request with different options.</summary>
    /// <param name="source">The source request.</param>
    /// <param name="options">The replacement options.</param>
    /// <param name="prompt">The optional replacement prompt.</param>
    /// <returns>The cloned request.</returns>
    private static CodexRunRequest CloneRequest(CodexRunRequest source, CodexRunOptions options, string? prompt = null)
    {
        return new CodexRunRequest
        {
            OperationId = source.OperationId,
            Prompt = prompt ?? source.Prompt,
            ThreadId = source.ThreadId,
            WorkspaceRoot = source.WorkspaceRoot,
            WorkspaceName = source.WorkspaceName,
            WorkspaceSolutionPath = source.WorkspaceSolutionPath,
            SolutionContext = source.SolutionContext,
            WorkspaceMemoryRoot = source.WorkspaceMemoryRoot,
            ReactiveMemoryContext = source.ReactiveMemoryContext,
            WorkspaceIdentity = source.WorkspaceIdentity,
            Options = options,
            Attachments = source.Attachments,
            Skills = source.Skills,
            Memories = source.Memories,
            McpServers = source.McpServers,
            WorkspaceFiles = source.WorkspaceFiles,
            AgentRoles = source.AgentRoles
        };
    }

    /// <summary>Enriches a request with its composed prompt.</summary>
    /// <param name="request">The source request.</param>
    /// <returns>The enriched request.</returns>
    private CodexRunRequest EnrichRequest(CodexRunRequest request)
    {
        return CloneRequest(request, request.Options, _promptBuilder.Build(request));
    }

    /// <summary>Runs the SDK fallback sequence.</summary>
    /// <param name="request">The enriched request.</param>
    /// <param name="sdkException">The SDK failure.</param>
    /// <returns>A task whose result contains the fallback result.</returns>
    private async Task<CodexRunResult> RunWithFallbackAsync(CodexRunRequest request, Exception sdkException)
    {
        CodexRunRequest? failoverRequest = BuildFailoverRequest(request);
        if (failoverRequest is null)
        {
            PublishFallbackEvent("fallback", $"SDK 桥接失败，改用 CLI 回退：{sdkException.Message}");
            return await RunCliFallbackAsync(request, sdkException).ConfigureAwait(continueOnCapturedContext: false);
        }

        PublishFallbackEvent(
            "fallback-model",
            $"SDK 桥接在 {request.Options.Model} 上失败，正在改用故障转移模型 {failoverRequest.Options.Model}：{sdkException.Message}");
        try
        {
            return await _sdk.RunAsync(failoverRequest).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (Exception failoverException) when (failoverException is not CodexRunFailureException)
        {
            PublishFallbackEvent("fallback", $"故障转移模型同样失败，改用 CLI 回退：{failoverException.Message}");
            return await RunCliFallbackAsync(failoverRequest, failoverException).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    /// <summary>Publishes an orchestration fallback event.</summary>
    /// <param name="type">The event type.</param>
    /// <param name="message">The event message.</param>
    private void PublishFallbackEvent(string type, string message)
    {
        _events.OnNext(new CodexEvent { Type = type, Message = message });
    }

    /// <summary>Runs the CLI fallback and preserves both transport errors.</summary>
    /// <param name="request">The request.</param>
    /// <param name="sdkException">The SDK failure.</param>
    /// <returns>A task whose result contains the CLI result.</returns>
    private async Task<CodexRunResult> RunCliFallbackAsync(CodexRunRequest request, Exception sdkException)
    {
        try
        {
            _isCliActive = true;
            return await _cli.RunAsync(request).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (Exception cliException)
        {
            throw new InvalidOperationException(CreateTransportFailureMessage(sdkException, cliException), cliException);
        }
    }

    /// <summary>Creates a combined SDK and CLI failure message.</summary>
    /// <param name="sdkException">The SDK failure.</param>
    /// <param name="cliException">The CLI failure.</param>
    /// <returns>The combined failure message.</returns>
    private string CreateTransportFailureMessage(Exception sdkException, Exception cliException)
    {
        var message = new StringBuilder();
        _ = message.Append("VSCodex 无法运行，因为 Codex SDK 桥接失败，且可选的 Codex CLI 回退也无法完成该请求。");
        _ = message.Append("请打开 VSCodex 设置标签页，运行“检查配置”，并确认 Codex SDK 可用（npm install -g @openai/codex-sdk）；");
        _ = message.Append("只有当 %USERPROFILE%\\.codex\\config.toml 中存在对应配置项时，才设置 Codex 配置文件。");
        _ = message.Append("可选 CLI 回退安装：npm install -g @openai/codex。 ");
        _ = message.Append($"SDK 失败：{sdkException.Message}；CLI 失败：{cliException.Message}");
        return message.ToString();
    }
}
