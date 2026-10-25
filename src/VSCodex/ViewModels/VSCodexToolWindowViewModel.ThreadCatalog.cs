// Copyright (c) 2019-2026 Chris Pulman and contributors. All rights reserved.
// Chris Pulman and contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using Newtonsoft.Json.Linq;

namespace VSCodex.ViewModels;

/// <summary>Composes protocol-backed history with local display preferences.</summary>
public sealed partial class VSCodexToolWindowViewModel
{
    /// <summary>Names the protocol thread response member.</summary>
    private const string ThreadPropertyName = "thread";

    /// <summary>Names protocol identity members.</summary>
    private const string IdentifierPropertyName = "id";

    /// <summary>Names protocol text members and content kinds.</summary>
    private const string TextPropertyName = "text";

    /// <summary>Seconds between background Codex thread catalog refreshes.</summary>
    private const int ThreadSyncSeconds = 45;

    /// <summary>Stores whether archived history is visible.</summary>
    private bool _includeArchivedSessions;

    /// <summary>Guards against overlapping catalog refreshes.</summary>
    private bool _isThreadSyncRunning;

    /// <summary>Refreshes the shared Codex thread catalog while the tool window is open.</summary>
    private DispatcherTimer? _threadSyncTimer;

    /// <summary>Gets the command that opens the native active-document diff.</summary>
    public ReactiveCommand<Unit, Unit> ReviewActiveFileCommand { get; private set; }

    /// <summary>Gets the command that imports public app-server thread metadata.</summary>
    public ReactiveCommand<Unit, Unit> RefreshThreadsCommand { get; private set; }

    /// <summary>Gets the command that forks a saved conversation.</summary>
    public ReactiveCommand<SessionHistoryItem, Unit> ForkSessionCommand { get; private set; }

    /// <summary>Gets the command that toggles a conversation's archive state.</summary>
    public ReactiveCommand<SessionHistoryItem, Unit> ArchiveSessionCommand { get; private set; }

    /// <summary>Gets the command that toggles local pinning.</summary>
    public ReactiveCommand<SessionHistoryItem, Unit> PinSessionCommand { get; private set; }

    /// <summary>Gets or sets whether archived conversations are included.</summary>
    public bool IncludeArchivedSessions
    {
        get => _includeArchivedSessions;
        set
        {
            _ = this.RaiseAndSetIfChanged(ref _includeArchivedSessions, value);
            RefreshHistory();
        }
    }

    /// <summary>Gets the optional public app-server capability.</summary>
    private IThreadCatalogClient ThreadCatalog => _codex as IThreadCatalogClient
        ?? throw new NotSupportedException("线程管理需要 Codex 应用服务器传输方式。");

    /// <summary>Starts background synchronization with the shared Codex thread catalog.</summary>
    public void StartThreadAutoSync()
    {
        if (_threadSyncTimer is null)
        {
            _threadSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ThreadSyncSeconds) };
            _threadSyncTimer.Tick += OnThreadSyncTick;
        }

        if (!_threadSyncTimer.IsEnabled)
        {
            _threadSyncTimer.Start();
        }

        OnThreadSyncTick(this, EventArgs.Empty);
    }

    /// <summary>Stops background synchronization with the shared Codex thread catalog.</summary>
    public void StopThreadAutoSync() => _threadSyncTimer?.Stop();

    /// <summary>Opens the native editor comparison without changing repository contents.</summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    private async Task ReviewActiveFileAsync()
    {
        try
        {
            await new NativeDiffReviewService().ReviewActiveFileAsync(_workspace.CurrentWorkspaceRoot);
            Status = "已打开原生 HEAD 对比";
        }
        catch (Exception exception)
        {
            Status = $"无法打开文件审阅：{exception.Message}";
        }
    }

    /// <summary>Refreshes the catalog so conversations created by the desktop app or another client appear automatically.</summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    private async Task RefreshThreadsAsync()
    {
        await RefreshThreadsAsync(silent: false);
    }

    /// <summary>Refreshes the shared Codex thread catalog.</summary>
    /// <param name="silent">Whether status updates are suppressed for background synchronization.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    private async Task RefreshThreadsAsync(bool silent)
    {
        if (_isThreadSyncRunning)
        {
            return;
        }

        _isThreadSyncRunning = true;
        try
        {
            SaveCurrentSessionIfNeeded();
            await ImportThreadCatalogAsync(false);
            if (IncludeArchivedSessions)
            {
                await ImportThreadCatalogAsync(true);
            }

            RefreshHistory();
            if (!silent)
            {
                Status = "已刷新 Codex 线程历史";
            }
        }
        catch (Exception exception)
        {
            if (!silent)
            {
                Status = $"无法刷新 Codex 线程：{exception.Message}";
            }
        }
        finally
        {
            _isThreadSyncRunning = false;
        }
    }

    /// <summary>Runs one background catalog refresh so desktop conversations show up without a manual refresh.</summary>
    /// <param name="sender">The timer that raised the tick.</param>
    /// <param name="e">The event arguments.</param>
    private void OnThreadSyncTick(object? sender, EventArgs e)
    {
        if (_isThreadSyncRunning || RefreshThreadsCommand is null)
        {
            return;
        }

        var command = (System.Windows.Input.ICommand)RefreshThreadsCommand;
        if (!command.CanExecute(null))
        {
            return;
        }

        TaskObserver.FireAndForget(RefreshThreadsAsync(silent: true));
    }

    /// <summary>Imports a catalog page group for every Codex conversation.</summary>
    /// <param name="archived">The archive state.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    private async Task ImportThreadCatalogAsync(bool archived)
    {
        string workspaceRoot = _workspace.CurrentWorkspaceRoot;
        JObject catalog = await ThreadCatalog.ListThreadsAsync(archived);
        if (!string.Equals(workspaceRoot, _workspace.CurrentWorkspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("加载历史时工作区已变更，请再次刷新。");
        }

        List<CodexSessionDocument> existing = _sessionStore.LoadRecent(int.MaxValue).ToList();
        foreach (JObject thread in catalog["data"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
        {
            ImportThreadMetadata(thread, archived, existing);
        }
    }

    /// <summary>Imports metadata for every Codex conversation, including ones created by the desktop app in another folder.</summary>
    /// <param name="thread">The protocol metadata.</param>
    /// <param name="archived">The archive state.</param>
    /// <param name="existing">The existing local documents.</param>
    private void ImportThreadMetadata(JObject thread, bool archived, List<CodexSessionDocument> existing)
    {
        string id = thread.Value<string>(IdentifierPropertyName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        string title = thread.Value<string>("name") ?? thread.Value<string>("preview") ?? id;
        long? updatedSeconds = thread["updatedAt"]?.Type == JTokenType.Integer ? thread.Value<long>("updatedAt") : null;
        WorkspaceIdentity identity = _workspace.CurrentWorkspaceIdentity;
        CodexSessionDocument? known = existing.FirstOrDefault(x => x.ThreadId == id);
        if (IsCatalogEntryUnchanged(known, archived, title, updatedSeconds, identity))
        {
            return;
        }

        CodexSessionDocument session = known ?? _sessionStore.Create();
        session.ThreadId = id;
        session.IsArchived = archived;
        session.Title = title;

        ApplyWorkspaceToSession(session, identity);
        if (updatedSeconds is not null)
        {
            session.Updated = DateTimeOffset.FromUnixTimeSeconds(updatedSeconds.Value);
        }

        _sessionStore.Save(session, preserveUpdated: true);
    }

    /// <summary>Loads public persisted turns before opening a catalog-only entry.</summary>
    /// <param name="item">The history entry.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    private async Task LoadHistoryItemAsync(SessionHistoryItem item)
    {
        try
        {
            CodexSessionDocument? session = _sessionStore.Load(item.Id);
            if (session?.Messages.Count == 0 && !string.IsNullOrWhiteSpace(session.ThreadId))
            {
                JObject response = await ThreadCatalog.ReadThreadAsync(session.ThreadId!);
                ImportThreadMessages(session, response[ThreadPropertyName] as JObject);
                _sessionStore.Save(session, preserveUpdated: true);
            }

            LoadHistoryItem(item);
        }
        catch (Exception exception)
        {
            Status = $"无法加载 Codex 线程：{exception.Message}";
        }
    }

    /// <summary>Forks server-side history without reusing the source thread identifier.</summary>
    /// <param name="item">The source history entry.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    private async Task ForkSessionAsync(SessionHistoryItem item)
    {
        try
        {
            if (item is null || string.IsNullOrWhiteSpace(item.ThreadId))
            {
                Status = "该历史项没有可分叉的 Codex 线程";
                return;
            }

            SaveCurrentSessionIfNeeded();
            WorkspaceIdentity workspace = _workspace.CurrentWorkspaceIdentity;
            JObject result = await ThreadCatalog.ForkThreadAsync(item.ThreadId!);
            JObject thread = result[ThreadPropertyName] as JObject ?? throw new InvalidOperationException("Codex 未返回分叉后的线程。");
            CodexSessionDocument fork = _sessionStore.Create();
            fork.ThreadId = thread.Value<string>(IdentifierPropertyName) ?? throw new InvalidOperationException("Codex 未返回分叉标识。");
            fork.Title = $"{item.Title} (fork)";
            ApplyWorkspaceToSession(fork, workspace);
            CodexSessionDocument? source = _sessionStore.Load(item.Id);
            if (source?.Messages.Count > 0)
            {
                fork.Messages.AddRange(source.Messages.Where(message => !message.IsTransient).Select(CloneMessage));
            }
            else
            {
                ImportThreadMessages(fork, thread);
            }

            _sessionStore.Save(fork);
            RefreshHistory();
            if (!string.Equals(workspace.Id, _workspace.CurrentWorkspaceIdentity.Id, StringComparison.OrdinalIgnoreCase))
            {
                Status = "分叉已保存到源工作区历史";
                return;
            }

            await LoadHistoryItemAsync(BuildHistoryItem(fork));
        }
        catch (Exception exception)
        {
            Status = $"无法分叉 Codex 线程：{exception.Message}";
        }
    }

    /// <summary>Updates the server first so failed mutations leave local metadata intact.</summary>
    /// <param name="item">The history entry.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    private async Task ArchiveSessionAsync(SessionHistoryItem item)
    {
        try
        {
            CodexSessionDocument? session = _sessionStore.Load(item.Id);
            if (session is null)
            {
                return;
            }

            bool archived = !session.IsArchived;
            if (!string.IsNullOrWhiteSpace(session.ThreadId))
            {
                _ = await ThreadCatalog.SetThreadArchivedAsync(session.ThreadId!, archived);
            }

            session.IsArchived = archived;
            _sessionStore.Save(session);
            if (_session.Id == session.Id)
            {
                _session.IsArchived = archived;
            }

            RefreshHistory();
            Status = archived ? "已归档会话" : "已恢复会话";
        }
        catch (Exception exception)
        {
            Status = $"无法更新归档状态：{exception.Message}";
        }
    }

    /// <summary>Persists pinning as an extension-local display preference.</summary>
    /// <param name="item">The history entry.</param>
    private void PinSession(SessionHistoryItem item)
    {
        CodexSessionDocument? session = _sessionStore.Load(item.Id);
        if (session is null)
        {
            return;
        }

        session.IsPinned = !session.IsPinned;
        _sessionStore.Save(session);
        if (_session.Id == session.Id)
        {
            _session.IsPinned = session.IsPinned;
        }

        RefreshHistory();
        Status = session.IsPinned ? "已在本地固定会话" : "已取消固定会话";
    }
}
