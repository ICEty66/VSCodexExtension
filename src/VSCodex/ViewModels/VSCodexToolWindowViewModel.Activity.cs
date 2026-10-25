// Copyright (c) 2019-2026 Chris Pulman and contributors. All rights reserved.
// Chris Pulman and contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace VSCodex.ViewModels;

/// <summary>Builds and updates the Codex run-activity presentation.</summary>
public sealed partial class VSCodexToolWindowViewModel
{
    /// <summary>Creates agent Plan Prompt.</summary>
    private void CreateAgentPlanPrompt()
    {
        string goal = (string.IsNullOrWhiteSpace(Prompt) ? "基于当前 Visual Studio 解决方案上下文，为选中的编码任务制定计划。" : Prompt);
        RefreshAgentPlanPreview(goal);
        Prompt = _assistantContext.BuildPlanPrompt(goal, BuildAgentSummary());
        Mode = CodexRunMode.Plan;
        SelectedToolTabIndex = Numeric6;
        IsToolPanelOpen = true;
        Status = "已准备 VSCodex 智能体计划";
    }

    /// <summary>Refreshes agent Plan Preview.</summary>
    /// <param name="goal">The goal.</param>
    private void RefreshAgentPlanPreview(string goal)
    {
        List<AgentRoleDefinition> agents = DistinctAgentRoles(AgentRoles.Where((x) => x.IsEnabled)).ToList();
        if (agents.Count == 0)
        {
            agents = DefaultAgentRoles();
        }

        List<OrchestrationTaskSection> sections = new[]
        {
            (
                Preferred: "Planner",
                Title: "明确目标与验收标准",
                Description: "Use the current Visual Studio solution, selected code, references, memories, and MCP tools to define the work."),
            (
                Preferred: "Architect",
                Title: "评估架构与集成风险",
                Description: "Identify affected projects, services, UI surfaces, threading risks, and compatibility constraints before editing."),
            (
                Preferred: "Builder",
                Title: "实施聚焦改动",
                Description: "Apply the smallest coherent code changes needed for the requested outcome."),
            (
                Preferred: "Reviewer",
                Title: "审阅行为、体验与安全性",
                Description: "Check correctness, regressions, user-visible behavior, and missing coverage."),
            (
                Preferred: "Verifier",
                Title: "在 Visual Studio 与命令行中验证",
                Description: "Run the relevant build, test, VSIX, and interactive Visual Studio checks, then summarize evidence.")
        }.Select((section, index) => new OrchestrationTaskSection
        {
            Index = index + 1,
            Title = section.Title,
            Description = $"{section.Description}{Environment.NewLine}Goal: {(string.IsNullOrWhiteSpace(goal) ? "Use current context." : goal)}",
            AssignedAgent = PickAgentName(agents, section.Preferred, index),
            DependsOnSectionId = ((index == 0) ? string.Empty : "previous")
        }).ToList();
        Replace(OrchestrationSections, sections);
    }

    /// <summary>Copies message To Clipboard.</summary>
    /// <param name="message">The message.</param>
    private void CopyMessageToClipboard(ChatMessage? message)
    {
        if (message is null || string.IsNullOrWhiteSpace(message.Content))
        {
            Status = "没有可复制的消息内容";
            return;
        }

        try
        {
            Clipboard.SetText(message.Content);
            Status = "已复制 VSCodex 消息";
        }
        catch (Exception ex)
        {
            Status = $"无法复制消息：{ex.Message}";
        }
    }

    /// <summary>Copies activity Detail To Clipboard.</summary>
    /// <param name="node">The node.</param>
    private void CopyActivityDetailToClipboard(RunActivityNode? node)
    {
        if (node is null || string.IsNullOrWhiteSpace(node.Detail))
        {
            Status = "没有可复制的活动文本";
            return;
        }

        try
        {
            Clipboard.SetText(node.Detail);
            Status = "已复制 VSCodex 回复文本";
        }
        catch (Exception ex)
        {
            Status = $"无法复制回复文本：{ex.Message}";
        }
    }

    /// <summary>Performs the use Message As Prompt operation.</summary>
    /// <param name="message">The message.</param>
    private void UseMessageAsPrompt(ChatMessage? message)
    {
        if (message is null || string.IsNullOrWhiteSpace(message.Content))
        {
            Status = "没有可用的消息内容";
            return;
        }

        Prompt = message.Content;
        Status = ((message.Role == CodexMessageRole.User) ? "已将用户提示词复制回输入框" : "已将消息复制回输入框");
    }

    /// <summary>Opens activity File.</summary>
    /// <param name="node">The node.</param>
    private void OpenActivityFile(RunActivityNode? node)
    {
        if (node is null || string.IsNullOrWhiteSpace(node.FilePath))
        {
            Status = "此活动没有关联文件";
            return;
        }

        if (node.IsDeleted || !File.Exists(node.FilePath))
        {
            Status = $"更改的文件已不存在：{node.FilePath}";
            return;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = node.FilePath,
                UseShellExecute = true
            });
            Status = $"Opened {node.FilePath}";
        }
        catch (Exception ex)
        {
            Status = $"无法打开更改的文件：{ex.Message}";
        }
    }

    /// <summary>Performs the begin Run Activity operation.</summary>
    /// <param name="prompt">The prompt.</param>
    /// <returns>The begin Run Activity result.</returns>
    private RunActivityNode BeginRunActivity(string prompt)
    {
        DateTimeOffset started = _timeProvider.GetLocalNow();
        RunActivityNode root = new RunActivityNode
        {
            Kind = RunActivityKind.User,
            Title = "用户请求",
            Detail = (prompt ?? string.Empty),
            StartedAt = started,
            IsExpanded = true
        };
        UpdateRunActivityElapsed(root);
        AddDefaultActivitySections(root);
        RunActivityRoots.Add(root);
        _activeRunActivity = root;
        return root;
    }

    /// <summary>Adds default Activity Sections.</summary>
    /// <param name="root">The root.</param>
    private void AddDefaultActivitySections(RunActivityNode root)
    {
        root.Children.Add(CreateSection(RunActivityKind.Agent, "智能体操作"));
        root.Children.Add(CreateSection(RunActivityKind.Mcp, "MCP 调用"));
        root.Children.Add(CreateSection(RunActivityKind.Skill, "技能调用"));
        root.Children.Add(CreateSection(RunActivityKind.Files, "文件更改"));
        root.Children.Add(CreateSection(RunActivityKind.Assistant, "助手回复"));
        root.Children.Add(CreateSection(RunActivityKind.System, "系统提示与诊断"));
    }

    /// <summary>Creates section.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="title">The title.</param>
    /// <returns>The create Section result.</returns>
    private RunActivityNode CreateSection(RunActivityKind kind, string title)
    {
        return new RunActivityNode
        {
            Kind = kind,
            Title = title,
            StartedAt = _timeProvider.GetLocalNow(),
            IsExpanded = kind == RunActivityKind.Assistant
        };
    }

    /// <summary>Performs the current Activity Root operation.</summary>
    /// <returns>The current Activity Root result.</returns>
    private RunActivityNode? CurrentActivityRoot()
    {
        return _activeRunActivity ?? RunActivityRoots.LastOrDefault();
    }

    /// <summary>Gets activity Section.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The get Activity Section result.</returns>
    private RunActivityNode GetActivitySection(RunActivityKind kind)
    {
        RunActivityNode? root = CurrentActivityRoot();
        root ??= BeginRunActivity("系统活动");

        RunActivityKind sectionKind = ((kind == RunActivityKind.File) ? RunActivityKind.Files : kind);
        RunActivityNode? section = root.Children.FirstOrDefault((child) => child.Kind == sectionKind);
        if (section is not null)
        {
            return section;
        }

        section = CreateSection(sectionKind, ActivitySectionTitle(sectionKind));
        RunActivityNode system = root.Children.FirstOrDefault((child) => child.Kind == RunActivityKind.System);
        if (system is null)
        {
            root.Children.Add(section);
        }
        else
        {
            root.Children.Insert(Math.Max(0, root.Children.IndexOf(system)), section);
        }

        return section;
    }

    /// <summary>Adds run Activity.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="title">The title.</param>
    /// <param name="detail">The detail.</param>
    /// <param name="filePath">The file Path.</param>
    /// <param name="isDeleted">The is Deleted.</param>
    /// <returns>The add Run Activity result.</returns>
    private RunActivityNode AddRunActivity(RunActivityKind kind, string title, string detail = "", string filePath = "", bool isDeleted = false)
    {
        RunActivityNode node = new RunActivityNode
        {
            Kind = (string.IsNullOrWhiteSpace(filePath) ? kind : RunActivityKind.File),
            Title = (title ?? string.Empty),
            Detail = (detail ?? string.Empty),
            FilePath = (filePath ?? string.Empty),
            IsDeleted = isDeleted,
            StartedAt = _timeProvider.GetLocalNow(),
            IsExpanded = kind == RunActivityKind.Assistant
        };
        RunOnUiThread(() =>
        {
            RunActivityNode activitySection = GetActivitySection(kind);
            activitySection.Children.Add(node);
        });
        return node;
    }

    /// <summary>Performs the append Message To Activity Tree operation.</summary>
    /// <param name="message">The message.</param>
    private void AppendMessageToActivityTree(ChatMessage message)
    {
        if (message.IsTransient)
        {
            return;
        }

        if (message.Role == CodexMessageRole.User)
        {
            bool suppressPendingActivity =
                !string.IsNullOrWhiteSpace(_pendingUserActivityPromptToSuppress) &&
                _activeRunActivity is not null &&
                string.Equals(_activeRunActivity.Detail, message.Content, StringComparison.Ordinal) &&
                string.Equals(_pendingUserActivityPromptToSuppress, message.Content, StringComparison.Ordinal);
            if (suppressPendingActivity)
            {
                _pendingUserActivityPromptToSuppress = string.Empty;
            }
            else
            {
                _ = BeginRunActivity(message.Content);
            }
        }
        else
        {
            _ = AddRunActivity(ActivityKindForRole(message.Role), ActivityTitleForRole(message.Role), message.Content);
        }
    }

    /// <summary>Performs the rebuild Activity Tree From Messages operation.</summary>
    private void RebuildActivityTreeFromMessages()
    {
        RunActivityRoots.Clear();
        _activeRunActivity = null;
        _activeProgressNode = null;
        _pendingUserActivityPromptToSuppress = string.Empty;
        foreach (ChatMessage message in Messages)
        {
            AppendMessageToActivityTree(message);
        }

        foreach (RunActivityNode root in RunActivityRoots)
        {
            CompleteRunActivityPresentation(root);
        }
    }

    /// <summary>Leaves the final response open while retaining earlier responses for inspection.</summary>
    /// <param name="root">The completed request.</param>
    private void CompleteRunActivityPresentation(RunActivityNode root)
    {
        RunActivityNode? responses = root.Children.FirstOrDefault(child => child.Kind == RunActivityKind.Assistant);
        if (responses is null)
        {
            return;
        }

        VSCodex.Core.Presentation.ResponsePresentation.Complete(
            responses.Children,
            response => response.Detail,
            (response, expanded) => response.IsExpanded = expanded,
            () =>
            {
                root.IsExpanded = true;
                responses.IsExpanded = true;
            });
    }

    /// <summary>Updates run Activity Elapsed.</summary>
    /// <param name="root">The root.</param>
    private void UpdateRunActivityElapsed(RunActivityNode? root)
    {
        if (root is null)
        {
            return;
        }

        DateTimeOffset end = root.CompletedAt ?? _timeProvider.GetLocalNow();
        root.ElapsedText = $"started {root.StartedAt.LocalDateTime:HH':'mm':'ss} | elapsed {FormatElapsed(end - root.StartedAt)}";
    }

    /// <summary>Adds changed Files Activity.</summary>
    /// <param name="files">The files.</param>
    private void AddChangedFilesActivity(IReadOnlyList<ChangedFileActivity> files)
    {
        RunOnUiThread(() =>
        {
            RunActivityNode activitySection = GetActivitySection(RunActivityKind.Files);
            activitySection.Children.Clear();
            if (files.Count == 0)
            {
                activitySection.Children.Add(new RunActivityNode
                {
                    Kind = RunActivityKind.Files,
                    Title = "未检测到文件更改",
                    Detail = "本次请求后 Git 未报告工作区文件更改。",
                    StartedAt = _timeProvider.GetLocalNow()
                });
            }
            else
            {
                foreach (ChangedFileActivity current in files.OrderBy((file) => file.RelativePath, StringComparer.OrdinalIgnoreCase))
                {
                    activitySection.Children.Add(new RunActivityNode
                    {
                        Kind = RunActivityKind.File,
                        Title = current.RelativePath,
                        Detail = current.Status,
                        FilePath = current.FullPath,
                        IsDeleted = current.IsDeleted,
                        StartedAt = _timeProvider.GetLocalNow(),
                        IsExpanded = false
                    });
                }
            }
        });
    }

    /// <summary>Handles a Codex event.</summary>
    /// <param name="ev">The ev.</param>
    private void OnCodexEvent(CodexEvent ev)
    {
        RunOnUiThread(() =>
        {
            UpdateRateLimitsFromJson(ev.RawJson);
            if (!string.IsNullOrWhiteSpace(ev.ThreadId))
            {
                ThreadId = ev.ThreadId;
            }

            if (ev.Type == "turn-started")
            {
                _activeTurnId = ev.TurnId ?? string.Empty;
                _activeOperationId = ev.OperationId ?? _activeOperationId;
                this.RaisePropertyChanged(nameof(CanSteer));
            }

            if (ev.Type == "approval-request")
            {
                JObject jsonObject = JObject.Parse(ev.RawJson);
                string text = jsonObject.Value<string>("method") ?? string.Empty;
                ApprovalRequest item = new ApprovalRequest
                {
                    Id = jsonObject["requestId"]?.ToString() ?? Guid.NewGuid().ToString("N"),
                    ServerRequestId = jsonObject["requestId"]?.DeepClone(),
                    Form = text == "mcpServer/elicitation/request" ? ElicitationForm.Create(jsonObject["params"] as JObject) : null,
                    Method = text,
                    ToolName = text,
                    ArgumentsPreview = (jsonObject["params"]?.ToString() ?? string.Empty),
                    Reason = text == "mcpServer/elicitation/request" ? "Codex 需要你的输入" : "Codex 需要你的批准"
                };
                ApprovalRequests.Add(item);
                _ = AddMessage(CodexMessageRole.Approval, ev.Message, persist: false);
                Status = item.Reason;
            }
            else if (ev.Type == "assistant-delta")
            {
                _activeStreamingResponse ??= AddRunActivity(RunActivityKind.Assistant, "Codex 回复", string.Empty);

                _activeStreamingResponse.Detail += ev.Message;
                Status = "Codex 正在回复……";
            }
            else if (ev.Type == "stdout" || ev.Type == "message")
            {
                _ = AddMessage(CodexMessageRole.Assistant, ev.Message);
            }
            else if (ev.Type == "fallback" || ev.Type == "transport-fallback" || ev.Type == "stderr" || ev.Type == "app-server-stderr" || ev.Type == "bridge-output")
            {
                _ = AddMessage(CodexMessageRole.System, $"[{ev.Type}] {ev.Message}");
            }
            else if (ev.Type == "progress")
            {
                SetRunProgress(ev.Message);
            }
            else
            {
                Status = ev.Message;
            }
        });
    }

    /// <summary>Handles an orchestration event.</summary>
    /// <param name="ev">The ev.</param>
    private void OnOrchestrationEvent(OrchestrationEvent ev)
    {
        RunOnUiThread(() =>
        {
            Status = ev.Message;
            if (ev.Type == "plan-created" && _taskOrchestrator.CurrentPlan is not null)
            {
                Replace(OrchestrationSections, _taskOrchestrator.CurrentPlan.Sections);
            }

            if (ev.Section is not null && !OrchestrationSections.Any((x) => x.Id == ev.Section.Id))
            {
                OrchestrationSections.Add(ev.Section);
            }

            _ = AddMessage(CodexMessageRole.System, $"[orchestration:{ev.Type}] {ev.Message}");
        });
    }

    /// <summary>Adds message.</summary>
    /// <param name="role">The role.</param>
    /// <param name="content">The content.</param>
    /// <param name="persist">The persist.</param>
    /// <param name="appendToActivity">The append To Activity.</param>
    /// <returns>The add Message result.</returns>
    private ChatMessage AddMessage(CodexMessageRole role, string content, bool persist = true, bool appendToActivity = true)
    {
        ChatMessage message = new ChatMessage
        {
            Role = role,
            Content = (content ?? string.Empty),
            IsTransient = !persist
        };
        RunOnUiThread(() =>
        {
            Messages.Add(message);
            if (appendToActivity)
            {
                AppendMessageToActivityTree(message);
            }

            if (message.IsTransient)
            {
                return;
            }

            _session.Messages.Add(message);
            _session.Updated = message.Timestamp;
            if (role != CodexMessageRole.User || !string.IsNullOrWhiteSpace(_session.Title))
            {
                return;
            }

            _session.Title = CompactLine(content, Numeric90);
        });
        return message;
    }

    /// <summary>Starts run Progress.</summary>
    /// <param name="stage">The stage.</param>
    /// <returns>The start Run Progress result.</returns>
    private IDisposable StartRunProgress(string stage)
    {
        _activeRunStartedAt = _timeProvider.GetLocalNow();
        _activeRunStage = stage;
        _activeProgressNode = AddRunActivity(RunActivityKind.Agent, "VSCodex 正在处理", BuildRunProgressMessage(stage));
        return Observable.Interval(TimeSpan.FromSeconds(Numeric15Point0), _uiScheduler).Subscribe(_ => RefreshRunProgress());
    }

    /// <summary>Sets run Progress.</summary>
    /// <param name="stage">The stage.</param>
    private void SetRunProgress(string stage)
    {
        RunOnUiThread(() =>
        {
            _activeRunStage = (string.IsNullOrWhiteSpace(stage) ? _activeRunStage : stage);
            Status = _activeRunStage;
            RefreshRunProgress();
        });
    }

    /// <summary>Refreshes run Progress.</summary>
    private void RefreshRunProgress()
    {
        if (_activeProgressNode is null || !IsRunning)
        {
            return;
        }

        _activeProgressNode.Title = (string.IsNullOrWhiteSpace(_activeRunStage) ? "VSCodex 正在处理" : _activeRunStage);
        _activeProgressNode.Detail = BuildRunProgressMessage(_activeRunStage);
        UpdateRunActivityElapsed(_activeRunActivity);
    }

    /// <summary>Performs the finish Run Progress operation.</summary>
    /// <param name="stage">The stage.</param>
    private void FinishRunProgress(string stage)
    {
        RunOnUiThread(() =>
        {
            if (_activeProgressNode is not null)
            {
                _activeProgressNode.Title = (string.IsNullOrWhiteSpace(stage) ? "VSCodex 已完成" : stage);
                _activeProgressNode.Detail = BuildRunProgressMessage(stage);
            }

            _activeProgressNode = null;
            _activeRunStage = string.Empty;
        });
    }

    /// <summary>Builds run Progress Message.</summary>
    /// <param name="stage">The stage.</param>
    /// <returns>The build Run Progress Message result.</returns>
    private string BuildRunProgressMessage(string stage)
    {
        TimeSpan elapsed = ((_activeRunStartedAt == default(DateTimeOffset))
            ? TimeSpan.Zero
            : (_timeProvider.GetLocalNow() - _activeRunStartedAt));
        string workspace = (string.IsNullOrWhiteSpace(_workspace.CurrentWorkspaceRoot) ? "正在等待 Visual Studio 工作区" : _workspace.CurrentWorkspaceRoot);
        string currentStage = (string.IsNullOrWhiteSpace(stage) ? "正在准备请求" : stage);
        return $"**VSCodex 正在处理**{Environment.NewLine}{Environment.NewLine}" +
            $"- Status: {currentStage}{Environment.NewLine}" +
            $"- Elapsed: {FormatElapsed(elapsed)}{Environment.NewLine}" +
            $"- Workspace: {workspace}";
    }
}
