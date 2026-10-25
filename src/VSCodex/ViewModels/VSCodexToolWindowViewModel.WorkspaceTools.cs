// Copyright (c) 2019-2026 Chris Pulman and contributors. All rights reserved.
// Chris Pulman and contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace VSCodex.ViewModels;

/// <summary>Coordinates workspace, MCP, skill, voice, and prerequisite features.</summary>
public sealed partial class VSCodexToolWindowViewModel
{
    /// <summary>Refreshes workspace Identity For Startup.</summary>
    private void RefreshWorkspaceIdentityForStartup()
    {
        try
        {
            _workspace.RefreshWorkspaceIdentity();
            RaiseWorkspaceDisplayProperties();
            string currentIdentity = _workspace.CurrentWorkspaceIdentity.Id;
            if (!string.IsNullOrWhiteSpace(currentIdentity) && !string.Equals(_lastWorkspaceSettingsId, currentIdentity, StringComparison.OrdinalIgnoreCase))
            {
                _lastWorkspaceSettingsId = currentIdentity;
                ApplySettingsFromStore(_settingsStore.LoadForWorkspace(_workspace.CurrentWorkspaceIdentity));
            }

            _lastWorkspaceIdentityId = currentIdentity;
            if (!string.IsNullOrWhiteSpace(_workspace.CurrentWorkspaceRoot))
            {
                _memoryStore.LoadWorkspace(_workspace.CurrentWorkspaceRoot);
            }

            Status = (string.IsNullOrWhiteSpace(_workspace.CurrentWorkspaceRoot) ? "VSCodex 已就绪；请打开解决方案或仓库文件夹" : ($"VSCodex 已就绪：{_workspace.CurrentWorkspaceRoot}"));
        }
        catch (Exception ex)
        {
            Status = $"VSCodex 启动上下文已延后：{ex.Message}";
        }
    }

    /// <summary>Performs the raise Workspace Display Properties operation.</summary>
    private void RaiseWorkspaceDisplayProperties()
    {
        this.RaisePropertyChanged(nameof(CurrentWorkspaceDisplay));
    }

    /// <summary>Performs the scan Project Memory operation.</summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task ScanProjectMemoryAsync()
    {
        WorkspaceIdentity identity = _workspace.CurrentWorkspaceIdentity;
        if (identity is null || string.IsNullOrWhiteSpace(identity.RootPath))
        {
            Status = "扫描项目记忆前请先打开解决方案或仓库文件夹";
            return;
        }

        Status = "正在使用 ReactiveMemory ProjectMiner 扫描项目记忆……";
        ReactiveMemoryCallResult scan = await Task.Run(
            () => _reactiveMemory.ScanWorkspaceAsync(identity))
            .ConfigureAwait(continueOnCapturedContext: false);
        await _joinableTaskFactory.SwitchToMainThreadAsync(default(CancellationToken));
        Status = scan.Message;
        if (scan.Success)
        {
            return;
        }

        _ = AddMessage(CodexMessageRole.System, scan.Message);
    }

    /// <summary>Ensures workspace Ready For Run.</summary>
    /// <returns><see langword="true"/> when ensure Workspace Ready For Run succeeds; otherwise, <see langword="false"/>.</returns>
    private bool EnsureWorkspaceReadyForRun()
    {
        string root = _workspace.CurrentWorkspaceRoot;
        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root) && !root.StartsWith(LocalPaths.ExtensionInstallRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string message = "VSCodex 暂时无法运行，因为 Visual Studio 尚未提供解决方案或仓库文件夹的项目根目录。" +
            "请等待项目加载完成、打开解决方案或仓库文件夹，或在项目可用后使用 @ 引用。" +
            "已安装的 VSIX 文件夹不会被用作执行根目录。";
        _ = AddMessage(CodexMessageRole.System, message);
        Status = "VSCodex 正在等待 Visual Studio 项目上下文";
        return false;
    }

    /// <summary>Selects mcp Server.</summary>
    /// <param name="server">The server.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task SelectMcpServerAsync(McpServerDefinition server)
    {
        if (server is null)
        {
            return;
        }

        await _joinableTaskFactory.SwitchToMainThreadAsync(default(CancellationToken));
        SelectedMcpServer = server;
        Status = $"正在发现 {server.Name} 的 MCP 工具……";
        IReadOnlyList<McpToolDefinition> tools = await Task.Run(
            () => _mcpTools.DiscoverToolsAsync(server))
            .ConfigureAwait(continueOnCapturedContext: false);
        await _joinableTaskFactory.SwitchToMainThreadAsync(default(CancellationToken));
        Replace(McpToolSuggestions, tools);
        Replace(McpToolInputFields, []);
        SelectedMcpTool = null;
        Status = ((tools.Count == 0) ? "未发现 MCP 工具" : $"已为 {server.Name} 发现 {tools.Count} 个 MCP 工具");
    }

    /// <summary>Selects mcp Tool.</summary>
    /// <param name="tool">The tool.</param>
    private void SelectMcpTool(McpToolDefinition tool)
    {
        if (tool is null)
        {
            return;
        }

        SelectedMcpTool = tool;
        Replace(McpToolInputFields, tool.InputFields.Select(CloneField));
        McpInputPrompt = ((tool.InputFields.Count == 0) ? "无需输入。" : "请为下方字段填写内容。可选字段会在字段名后标注“可选”。");
        Status = $"已选择 MCP 工具 {tool.DisplayName}";
    }

    /// <summary>Performs the insert Mcp Tool Invocation operation.</summary>
    private void InsertMcpToolInvocation()
    {
        if (SelectedMcpServer is null || SelectedMcpTool is null)
        {
            ShowMcpServerList();
            return;
        }

        SelectedMcpTool.InputFields.Clear();
        SelectedMcpTool.InputFields.AddRange(McpToolInputFields.Select(CloneField));
        string invocation = _mcpTools.BuildInvocationPrompt(SelectedMcpServer, SelectedMcpTool);
        Prompt = (string.IsNullOrWhiteSpace(Prompt) ? invocation : (Prompt.TrimEnd() + Environment.NewLine + invocation));
        Status = "已将 MCP 工具调用插入提示词";
    }

    /// <summary>Adds mcp Server.</summary>
    /// <param name="transportType">The transport Type.</param>
    private void AddMcpServer(string transportType)
    {
        if (!CanEditSettings)
        {
            Status = VSCodexSettingsAreLockedWhileATaskIsRunnText;
            return;
        }

        McpServerDefinition server = _mcpConfig.CreateTemplate(transportType);
        McpServers.Add(server);
        SelectedMcpServer = server;
        Status = "已添加 MCP 服务器草稿。请填写细节后保存 MCP 服务器。";
    }

    /// <summary>Removes mcp Server.</summary>
    /// <param name="server">The server.</param>
    private void RemoveMcpServer(McpServerDefinition server)
    {
        if (server is null || !CanEditSettings)
        {
            return;
        }

        _ = McpServers.Remove(server);
        if (SelectedMcpServer == server)
        {
            SelectedMcpServer = McpServers.FirstOrDefault();
        }

        Status = "已移除 MCP 服务器草稿。保存 MCP 服务器以更新 Codex 配置。";
    }

    /// <summary>Saves mcp Servers.</summary>
    private void SaveMcpServers()
    {
        if (!CanEditSettings)
        {
            Status = VSCodexSettingsAreLockedWhileATaskIsRunnText;
            return;
        }

        _mcpConfig.Save(McpServers.ToList());
        Status = $"已将 MCP 服务器保存到 {LocalPaths.UserCodexConfig}";
    }

    /// <summary>Creates skill.</summary>
    private void CreateSkill()
    {
        if (!CanEditSettings)
        {
            Status = VSCodexSettingsAreLockedWhileATaskIsRunnText;
            return;
        }

        try
        {
            _ = Directory.CreateDirectory(LocalPaths.UserSkillsRoot);
            string skillPath = _skillIndex.CreateSkill(LocalPaths.UserSkillsRoot, NewSkillName, NewSkillDescription);
            NewSkillName = string.Empty;
            NewSkillDescription = string.Empty;
            Refresh();
            Status = $"已创建技能 {skillPath}";
            OpenPath(skillPath);
        }
        catch (Exception ex)
        {
            Status = $"创建技能失败：{ex.Message}";
        }
    }

    /// <summary>Saves skill Selection.</summary>
    private void SaveSkillSelection()
    {
        if (!CanEditSettings)
        {
            Status = VSCodexSettingsAreLockedWhileATaskIsRunnText;
            return;
        }

        ExtensionSettings settings = _settingsStore.Current;
        settings.EnabledSkillPaths = (from x in Skills
                                      where x.IsEnabled
                                      select x.MarkdownPath into x
                                      where !string.IsNullOrWhiteSpace(x)
                                      select x).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        SaveSettingsForCurrentWorkspace(settings);
        Status = $"已保存 {settings.EnabledSkillPaths.Count} 个已启用技能";
        UpdateAnalytics(Prompt);
    }

    /// <summary>Adds skill Root.</summary>
    private void AddSkillRoot()
    {
        if (!CanEditSettings)
        {
            Status = VSCodexSettingsAreLockedWhileATaskIsRunnText;
            return;
        }

        string path = SkillRootPathInput.Trim();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            Status = "请输入一个已存在的文件夹以添加技能根目录";
            return;
        }

        ExtensionSettings settings = _settingsStore.Current;
        if (!settings.SkillRoots.Any((x) => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
        {
            settings.SkillRoots.Add(path);
            SaveSettingsForCurrentWorkspace(settings);
        }

        SkillRootPathInput = string.Empty;
        Refresh();
        Status = $"已添加技能根目录 {path}";
    }

    /// <summary>Opens skills Folder.</summary>
    private void OpenSkillsFolder()
    {
        _ = Directory.CreateDirectory(LocalPaths.UserSkillsRoot);
        OpenPath(LocalPaths.UserSkillsRoot);
    }

    /// <summary>Opens codex Config.</summary>
    private void OpenCodexConfig()
    {
        string directory = Path.GetDirectoryName(LocalPaths.UserCodexConfig);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        if (!File.Exists(LocalPaths.UserCodexConfig))
        {
            File.WriteAllText(LocalPaths.UserCodexConfig, string.Empty);
        }

        OpenPath(LocalPaths.UserCodexConfig);
    }

    /// <summary>Handles the prompt Changed event.</summary>
    /// <param name="prompt">The prompt.</param>
    private void OnPromptChanged(string prompt)
    {
        int revision = Interlocked.Increment(ref _promptChangeRevision);
        TaskObserver.FireAndForget(_joinableTaskFactory.RunAsync(async () =>
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync(default(CancellationToken));
            if (revision != Volatile.Read(ref _promptChangeRevision) || !string.Equals(prompt, Prompt, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                UpdateReferenceSuggestions(prompt);
                UpdatePromptSuggestions(prompt);
                UpdateAnalytics(prompt);
                if (IsMcpDiscoveryPrompt(prompt))
                {
                    ShowMcpServerList();
                }
            }
            catch (Exception ex)
            {
                Status = $"提示上下文更新失败：{ex.Message}";
            }
        }).Task);
    }

    /// <summary>Performs the show Mcp Server List operation.</summary>
    private void ShowMcpServerList()
    {
        Replace(McpToolSuggestions, []);
        Replace(McpToolInputFields, []);
        McpInputPrompt = "请选择一个 MCP 服务器以列出工具，然后选择一个工具并填写必填输入字段；可选字段会标注“可选”。";
        Status = ((McpServers.Count == 0) ? ".codex/config.toml 中未配置任何 MCP 服务器" : "请在 MCP 标签页中选择一个 MCP 服务器");
    }

    /// <summary>Attempts to handle Local Slash Command.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when try Handle Local Slash Command succeeds; otherwise, <see langword="false"/>.</returns>
    private bool TryHandleLocalSlashCommand(string value)
    {
        string command = (value ?? string.Empty).Trim();
        if (command.Length == 0 || command[0] != '/')
        {
            return false;
        }

        string commandName = (command.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty).ToLowerInvariant();
        Action? action = commandName switch
        {
            "/threads" or "/history" => ShowHistory,
            "/models" or "/settings" => () => Status = "打开“工具 > 选项 > VSCodex”以更改设置",
            "/refresh" => Refresh,
            _ => null
        };
        if (action is null)
        {
            return TryHandleToolPanelSlashCommand(commandName);
        }

        action();
        Prompt = string.Empty;
        return true;
    }

    /// <summary>Attempts to handle a tool Panel Slash Command.</summary>
    /// <param name="commandName">The command Name.</param>
    /// <returns><see langword="true"/> when a tool panel command was handled; otherwise, <see langword="false"/>.</returns>
    private bool TryHandleToolPanelSlashCommand(string commandName)
    {
        (int TabIndex, string Title)? panel = commandName switch
        {
            "/setup" or "/prerequisites" => (1, "VSCodex 前置条件"),
            "/context" or "/files" or "/selection" => (Numeric2, "VSCodex 上下文"),
            "/skills" => (Numeric3, "VSCodex 技能"),
            "/tools" or "/mcp" => (Numeric4, "VSCodex MCP 工具"),
            "/memory" => (Numeric5, "VSCodex 记忆"),
            "/agents" => (Numeric6, "VSCodex 智能体"),
            "/attachments" => (Numeric7, "VSCodex 附件"),
            _ => null
        };
        if (!panel.HasValue)
        {
            return false;
        }

        if (panel.Value.TabIndex == Numeric4)
        {
            ShowMcpServerList();
        }

        ShowToolPanel(panel.Value.TabIndex, panel.Value.Title);
        Prompt = string.Empty;
        return true;
    }

    /// <summary>Performs the show Tool Panel operation.</summary>
    /// <param name="tabIndex">The tab Index.</param>
    /// <param name="status">The status.</param>
    private void ShowToolPanel(int tabIndex, string status)
    {
        IsToolPanelOpen = true;
        SelectedToolTabIndex = tabIndex;
        Status = status;
    }

    /// <summary>Adds memory.</summary>
    /// <param name="scope">The scope.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task AddMemoryAsync(string scope)
    {
        string text = Prompt;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        ReactiveMemoryCallResult memory = await _reactiveMemory.AddMemoryAsync(text, scope, _workspace.CurrentWorkspaceIdentity).ConfigureAwait(continueOnCapturedContext: false);
        await _joinableTaskFactory.SwitchToMainThreadAsync(default(CancellationToken));
        if (memory.Success)
        {
            _memoryStore.Add(text, scope);
            Status = memory.Message;
        }
        else
        {
            Status = $"ReactiveMemory 未保存记忆：{memory.Message}";
        }
    }

    /// <summary>Adds image Attachment.</summary>
    private void AddImageAttachment()
    {
        OpenFileDialog dialog = new OpenFileDialog
        {
            Title = "为 VSCodex 附加文件",
            Filter = "Supported files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.pdf;*.doc;" +
                "*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.txt;*.md;*.cs;*.xaml;*.json;*.xml|" +
                "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|Documents|*.pdf;*.doc;" +
                "*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.txt;*.md|All files|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        AttachFiles(dialog.FileNames);
    }

    /// <summary>Performs the append Voice Transcript operation.</summary>
    /// <param name="text">The text.</param>
    private void AppendVoiceTranscript(string text)
    {
        string transcript = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return;
        }

        bool shouldSubmit = TryExtractVoiceSubmit(ref transcript);
        if (!string.IsNullOrWhiteSpace(transcript))
        {
            Prompt = (string.IsNullOrWhiteSpace(Prompt) ? transcript : ($"{Prompt.TrimEnd()} {transcript}"));
            VoiceTranscriptRevision++;
            Status = (shouldSubmit ? "已添加语音指令；正在发送 VSCodex 请求" : "已添加语音转写");
        }
        else if (shouldSubmit)
        {
            Status = (string.IsNullOrWhiteSpace(Prompt) ? "已听到语音指令；请先口述提示词，再说“发送”" : "语音指令正在发送 VSCodex 请求");
        }

        if (!shouldSubmit || string.IsNullOrWhiteSpace(Prompt))
        {
            return;
        }

        _voiceInput.Stop();
        this.RaisePropertyChanged(nameof(IsListeningToVoice));
        TaskObserver.FireAndForget(_joinableTaskFactory.RunAsync(
            async () => await SubmitPromptAsync().ConfigureAwait(continueOnCapturedContext: true)).Task);
    }

    /// <summary>Updates voice Input Status.</summary>
    /// <param name="status">The status.</param>
    private void UpdateVoiceInputStatus(string status)
    {
        VoiceInputStatus = status;
        this.RaisePropertyChanged(nameof(IsListeningToVoice));
        this.RaisePropertyChanged(nameof(IsVoiceInputAvailable));
    }

    /// <summary>Performs the schedule Startup Checks In Background operation.</summary>
    private void ScheduleStartupChecksInBackground()
    {
        TaskObserver.FireAndForget(_joinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Numeric4), _lifetime.Token).ConfigureAwait(continueOnCapturedContext: false);
                await _joinableTaskFactory.SwitchToMainThreadAsync(_lifetime.Token);
                Status = "正在后台加载 VSCodex 工具并检查配置……";
                string workspaceRoot = _workspace.CurrentWorkspaceRoot;
                List<string> skillRoots = _settingsStore.Current.SkillRoots.Concat([Path.Combine(workspaceRoot ?? string.Empty, ".codex", "skills")]).ToList();
                await Task.Run(
                    () =>
                {
                    _mcpConfig.Refresh();
                    _skillIndex.Refresh(skillRoots);
                },
                    _lifetime.Token).ConfigureAwait(continueOnCapturedContext: false);
                await CheckPrerequisitesAsync(showSystemMessage: false).ConfigureAwait(continueOnCapturedContext: true);
                await RefreshRuntimeModelsAsync().ConfigureAwait(continueOnCapturedContext: false);
                await Task.Delay(TimeSpan.FromSeconds(Numeric6), _lifetime.Token).ConfigureAwait(continueOnCapturedContext: false);
                await RefreshRateLimitsAsync().ConfigureAwait(continueOnCapturedContext: true);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception ex2)
            {
                await _joinableTaskFactory.SwitchToMainThreadAsync(default(CancellationToken));
                Status = $"VSCodex 后台启动检查失败：{ex2.Message}";
            }
        }).Task);
    }

    /// <summary>Checks prerequisites.</summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private Task CheckPrerequisitesAsync()
    {
        return CheckPrerequisitesAsync(showSystemMessage: true);
    }

    /// <summary>Checks prerequisites.</summary>
    /// <param name="showSystemMessage">The show System Message.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task CheckPrerequisitesAsync(bool showSystemMessage)
    {
        await _joinableTaskFactory.SwitchToMainThreadAsync(default(CancellationToken));
        Status = CheckingVSCodexPrerequisitesText;
        CodexSetupSummary = CheckingVSCodexPrerequisitesText;
        CodexEnvironmentReport report = await _environment.CheckAsync(_settingsStore.Current).ConfigureAwait(continueOnCapturedContext: false);
        await _joinableTaskFactory.SwitchToMainThreadAsync(default(CancellationToken));
        ApplyEnvironmentReport(report, showSystemMessage && !report.CanRunSdkBridge);
    }

    /// <summary>Refreshes rate Limits In Background.</summary>
    private void RefreshRateLimitsInBackground()
    {
        TaskObserver.FireAndForget(_joinableTaskFactory.RunAsync(
            async () => await RefreshRateLimitsAsync().ConfigureAwait(continueOnCapturedContext: true)).Task);
    }

    /// <summary>Refreshes rate Limits.</summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task RefreshRateLimitsAsync()
    {
        CodexEnvironmentReport? report = _lastEnvironmentReport;
        if (report?.CanRunSdkBridge == false)
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync(default(CancellationToken));
            SetRateLimitsUnavailable("Codex SDK 不可用");
            return;
        }

        await _joinableTaskFactory.SwitchToMainThreadAsync(default(CancellationToken));
        SetRateLimitRows("正在获取 Codex 遥测", 0, string.Empty);
        RateLimitUpdatedAt = "正在检查 Codex 速率限制遥测";
        try
        {
            JObject? rateLimits = await _codex.GetRateLimitsAsync().ConfigureAwait(continueOnCapturedContext: false);
            await _joinableTaskFactory.SwitchToMainThreadAsync(default(CancellationToken));
            if (rateLimits is null)
            {
                SetRateLimitsUnavailable("Codex 遥测不可用");
                return;
            }

            UpdateRateLimitsFromJson(rateLimits.ToString());
            SetRateLimitsUnavailable("Codex 未报告该账户限额");
        }
        catch (Exception ex)
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync(default(CancellationToken));
            SetRateLimitsUnavailable("Codex 遥测不可用");
            RateLimitUpdatedAt = $"Codex 速率限制检查失败：{ex.Message}";
        }
    }

    /// <summary>Sets rate Limits Unavailable.</summary>
    /// <param name="text">The text.</param>
    private void SetRateLimitsUnavailable(string text)
    {
        foreach (RateLimitWindowStatus row in RateLimits)
        {
            if (row.HasTelemetry)
            {
                if (!row.ToolTip.Contains("最近一次"))
                {
                    row.ToolTip += " 刷新时若未包含该窗口的数据，则保留最近一次收到的账户遥测。";
                }
            }
            else
            {
                row.Remaining = "不可用";
                row.ResetText = string.Empty;
                row.ToolTip = $"{text}。请刷新账户状态或检查 Codex 登录。";
            }
        }

        if (!RateLimits.Any(row => row.HasTelemetry))
        {
            RateLimitUpdatedAt = text;
        }
    }

    /// <summary>Sets rate Limit Rows.</summary>
    /// <param name="remaining">The remaining.</param>
    /// <param name="usagePercent">The usage Percent.</param>
    /// <param name="resetText">The reset Text.</param>
    private void SetRateLimitRows(string remaining, int usagePercent, string resetText)
    {
        foreach (RateLimitWindowStatus rateLimit in RateLimits)
        {
            if (rateLimit.HasTelemetry)
            {
                continue;
            }

            rateLimit.Remaining = remaining;
            rateLimit.UsagePercent = usagePercent;
            rateLimit.ResetText = resetText;
        }
    }

    /// <summary>Ensures codex Sdk Ready For Run.</summary>
    /// <returns>A task whose result contains the operation result.</returns>
    private async Task<bool> EnsureCodexSdkReadyForRunAsync()
    {
        CodexEnvironmentReport? report = _lastEnvironmentReport;
        if (report?.CanRunSdkBridge != true)
        {
            report = await _environment.CheckAsync(_settingsStore.Current).ConfigureAwait(continueOnCapturedContext: false);
            await _joinableTaskFactory.SwitchToMainThreadAsync(default(CancellationToken));
            ApplyEnvironmentReport(report, !report.CanRunSdkBridge);
        }

        if (Transport == CodexTransportKind.CliFallback ? report.IsCliReady : report.CanRunSdkBridge)
        {
            return true;
        }

        _ = AddMessage(CodexMessageRole.System, CodexSetupInstructions);
        Status = "需要完成 VSCodex 配置。请打开“工具 > 选项 > VSCodex”调整运行时路径。";
        return false;
    }

    /// <summary>Applies environment Report.</summary>
    /// <param name="report">The report.</param>
    /// <param name="showSystemMessage">The show System Message.</param>
    private void ApplyEnvironmentReport(CodexEnvironmentReport report, bool showSystemMessage)
    {
        _lastEnvironmentReport = report;
        Replace(Prerequisites, report.Items);
        CodexSetupSummary = report.Summary;
        CodexSetupInstructions = report.Instructions;
        Status = (report.CanRunSdkBridge ? "VSCodex 前置条件已就绪" : "需要完成 VSCodex 配置");
        if (!report.CanRunSdkBridge)
        {
            IsToolPanelOpen = true;
            SelectedToolTabIndex = 1;
        }

        if (!showSystemMessage)
        {
            return;
        }

        _ = AddMessage(CodexMessageRole.System, report.Summary + Environment.NewLine + Environment.NewLine + report.Instructions);
    }

    /// <summary>Copies prerequisite Command To Clipboard.</summary>
    /// <param name="prerequisite">The prerequisite.</param>
    private void CopyPrerequisiteCommandToClipboard(PrerequisiteStatus? prerequisite)
    {
        string? command = prerequisite?.ActionCommand;
        if (prerequisite is null || command is null || command.Trim().Length == 0)
        {
            Status = "没有可复制的前置条件命令";
            return;
        }

        try
        {
            Clipboard.SetText(command);
            Status = $"已复制 {prerequisite.Name} 的前置条件命令";
        }
        catch (Exception ex)
        {
            Status = $"无法复制前置条件命令：{ex.Message}";
        }
    }

    /// <summary>Starts prerequisite Update.</summary>
    /// <param name="prerequisite">The prerequisite.</param>
    private void StartPrerequisiteUpdate(PrerequisiteStatus? prerequisite)
    {
        string? command = prerequisite?.ActionCommand;
        if (prerequisite is null || command is null || command.Trim().Length == 0)
        {
            Status = "没有可用的前置条件更新命令";
            return;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/d /s /k {QuoteForCmd($"{command} && echo. && echo VSCodex prerequisite command finished. Restart Visual Studio if PATH changed, then run Check again.")}",
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                UseShellExecute = true
            });
            Status = $"已启动 {prerequisite.Name} 的前置条件更新";
        }
        catch (Exception ex)
        {
            Status = $"无法启动前置条件更新：{ex.Message}";
        }
    }

    /// <summary>Starts new Thread.</summary>
    private void StartNewThread()
    {
        SaveCurrentSessionIfNeeded();
        _session = _sessionStore.Create();
        Prompt = string.Empty;
        ThreadId = null;
        Messages.Clear();
        RunActivityRoots.Clear();
        _activeRunActivity = null;
        _activeProgressNode = null;
        _pendingUserActivityPromptToSuppress = string.Empty;
        _pausedCheckpoint = null;
        _pauseRequested = false;
        _stopRequested = false;
        IsPaused = false;
        Attachments.Clear();
        OrchestrationSections.Clear();
        Status = "新建 VSCodex 线程";
        RefreshHistory();
        UpdateAnalytics(Prompt);
    }

    /// <summary>Applies current Workspace To Session.</summary>
    private void ApplyCurrentWorkspaceToSession()
    {
        WorkspaceIdentity identity = _workspace.CurrentWorkspaceIdentity;
        if (identity is null || string.IsNullOrWhiteSpace(identity.Id))
        {
            return;
        }

        _session.WorkspaceIdentityId = identity.Id;
        _session.WorkspaceName = (string.IsNullOrWhiteSpace(identity.Name) ? _workspace.CurrentWorkspaceName : identity.Name);
        _session.WorkspaceRoot = identity.RootPath;
        _session.WorkspaceSolutionPath = identity.SolutionPath;
    }
}
