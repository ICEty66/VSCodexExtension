// Copyright (c) 2019-2026 Chris Pulman and contributors. All rights reserved.
// Chris Pulman and contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace VSCodex.Services;

/// <summary>Composes a Git HEAD snapshot with Visual Studio's native diff editor.</summary>
public sealed class NativeDiffReviewService
{
    /// <summary>Compares the active editor document against its committed HEAD version.</summary>
    /// <param name="workspaceRoot">The current workspace root.</param>
    /// <returns>A task that completes when the native review window opens.</returns>
    public async Task ReviewActiveFileAsync(string workspaceRoot)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var provider = ServiceProvider.GlobalProvider;
        var dte = provider.GetService(typeof(DTE)) as DTE;
        string file = dte?.ActiveDocument?.FullName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            throw new InvalidOperationException("请先在 Visual Studio 编辑器中打开一个已保存的仓库文件。");
        }

        string root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(file);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("当前活动文件不在该工作区中。");
        }

        string relative = fullPath.Substring(root.Length).Replace('\\', '/');
        string snapshotDirectory = Path.Combine(Path.GetTempPath(), "VSCodex", "diff", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(snapshotDirectory);
        string snapshot = Path.Combine(snapshotDirectory, Path.GetFileName(file));
        try
        {
            await WriteHeadSnapshotAsync(root, relative, snapshot);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var difference = provider.GetService(typeof(SVsDifferenceService)) as IVsDifferenceService
                ?? throw new NotSupportedException("Visual Studio 差异比较服务不可用。");
            _ = difference.OpenComparisonWindow2(
                snapshot,
                fullPath,
                "VSCodex 审阅：{1}",
                "将 HEAD 与当前编辑器文档比较",
                "HEAD",
                "工作文档",
                string.Empty,
                string.Empty,
                (uint)__VSDIFFSERVICEOPTIONS.VSDIFFOPT_LeftFileIsTemporary);
        }
        catch
        {
            if (File.Exists(snapshot))
            {
                File.Delete(snapshot);
            }

            Directory.Delete(snapshotDirectory);
            throw;
        }
    }

    /// <summary>Writes a byte-preserving Git snapshot without shell command interpretation.</summary>
    /// <param name="root">The working directory.</param>
    /// <param name="relative">The workspace-relative file path.</param>
    /// <param name="snapshot">The destination temporary file.</param>
    /// <returns>A task that completes after Git exits successfully.</returns>
    private static async Task WriteHeadSnapshotAsync(string root, string relative, string snapshot)
    {
        var start = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            Arguments = $"show \"HEAD:./{relative}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("无法启动 Git。");
        Task<string> error = process.StandardError.ReadToEndAsync();
        using (var output = File.Create(snapshot))
        {
            await process.StandardOutput.BaseStream.CopyToAsync(output).ConfigureAwait(false);
        }

        await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
        string detail = await error.ConfigureAwait(false);
        if (process.ExitCode == 0)
        {
            return;
        }

        throw new InvalidOperationException($"无法从 Git HEAD 读取此文件。审阅新文件的基线前，请先保存并提交。{detail.Trim()}");
    }
}
