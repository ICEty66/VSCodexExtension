// Copyright (c) 2019-2026 Chris Pulman and contributors. All rights reserved.
// Chris Pulman and contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace VSCodex.ViewModels;

/// <summary>Composes owned local Git worktree operations with the tool window.</summary>
public sealed partial class VSCodexToolWindowViewModel
{
    /// <summary>Stores checkout ownership outside repository contents.</summary>
    private readonly ManagedWorktreeService _worktrees = new(Path.Combine(LocalPaths.AppRoot, "managed-worktrees"));

    /// <summary>Stores the new branch name.</summary>
    private string _newWorktreeBranch = "CP_";

    /// <summary>Gets the visible repository checkouts.</summary>
    public ObservableCollection<ManagedWorktree> Worktrees { get; } = new();

    /// <summary>Gets or sets the branch to create.</summary>
    public string NewWorktreeBranch
    {
        get => _newWorktreeBranch;
        set => this.RaiseAndSetIfChanged(ref _newWorktreeBranch, value);
    }

    /// <summary>Gets the checkout refresh command.</summary>
    public ReactiveCommand<Unit, Unit> RefreshWorktreesCommand { get; private set; } = null!;

    /// <summary>Gets the checkout creation command.</summary>
    public ReactiveCommand<Unit, Unit> CreateWorktreeCommand { get; private set; } = null!;

    /// <summary>Gets the checkout archive/restore command.</summary>
    public ReactiveCommand<ManagedWorktree, Unit> ArchiveWorktreeCommand { get; private set; } = null!;

    /// <summary>Gets the checkout pin command.</summary>
    public ReactiveCommand<ManagedWorktree, Unit> PinWorktreeCommand { get; private set; } = null!;

    /// <summary>Gets the command to open a checkout in the file browser.</summary>
    public ReactiveCommand<ManagedWorktree, Unit> OpenWorktreeCommand { get; private set; } = null!;

    /// <summary>Initializes checkout commands on the UI scheduler.</summary>
    private void InitializeWorktreeCommands()
    {
        RefreshWorktreesCommand = ReactiveCommand.CreateFromTask(() => RunWorktreeOperationAsync(null), outputScheduler: _uiScheduler);
        CreateWorktreeCommand = ReactiveCommand.CreateFromTask(CreateWorktreeAsync, outputScheduler: _uiScheduler);
        ArchiveWorktreeCommand = ReactiveCommand.CreateFromTask<ManagedWorktree>(item => RunWorktreeOperationAsync(root => _worktrees.ToggleArchive(root, item.Id)), outputScheduler: _uiScheduler);
        PinWorktreeCommand = ReactiveCommand.CreateFromTask<ManagedWorktree>(item => RunWorktreeOperationAsync(root => _worktrees.TogglePin(root, item.Id)), outputScheduler: _uiScheduler);
        OpenWorktreeCommand = ReactiveCommand.Create<ManagedWorktree>(OpenWorktree, outputScheduler: _uiScheduler);
    }

    /// <summary>Runs an operation against a captured workspace and reports failures explicitly.</summary>
    /// <param name="operation">The mutation, or null to refresh only.</param>
    /// <returns>A task that completes after the UI is refreshed.</returns>
    private async Task RunWorktreeOperationAsync(Action<string>? operation)
    {
        if (!CanEditSettings)
        {
            Status = "请等待当前任务结束再管理工作树。";
            return;
        }

        string workspace = _workspace.CurrentWorkspaceRoot;
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
        {
            Status = "管理工作树前请先打开仓库文件夹或解决方案。";
            return;
        }

        try
        {
            Status = "正在更新本地 Git 工作树……";
            IReadOnlyList<ManagedWorktree> items = await Task.Run(() =>
            {
                operation?.Invoke(workspace);
                return _worktrees.List(workspace);
            });
            await _joinableTaskFactory.SwitchToMainThreadAsync(default(CancellationToken));
            if (!string.Equals(workspace, _workspace.CurrentWorkspaceRoot, StringComparison.OrdinalIgnoreCase))
            {
                Status = "工作区已变更；请为新工作区刷新工作树。";
                return;
            }

            Replace(Worktrees, items);
            Status = "本地工作树已刷新。归档会保留完整的恢复快照与检出。";
        }
        catch (Exception exception)
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync(default(CancellationToken));
            Status = $"工作树操作失败：{exception.Message}";
        }
    }

    /// <summary>Captures the user-entered branch before background work begins.</summary>
    /// <returns>The creation operation.</returns>
    private Task CreateWorktreeAsync()
    {
        string branch = NewWorktreeBranch;
        return RunWorktreeOperationAsync(root => _worktrees.Create(root, branch));
    }

    /// <summary>Opens a checkout directory and reports shell failures in the status bar.</summary>
    /// <param name="item">The checkout.</param>
    private void OpenWorktree(ManagedWorktree item)
    {
        try
        {
            OpenPath(item.Path);
        }
        catch (Exception exception)
        {
            Status = $"无法打开工作树文件夹：{exception.Message}";
        }
    }
}
