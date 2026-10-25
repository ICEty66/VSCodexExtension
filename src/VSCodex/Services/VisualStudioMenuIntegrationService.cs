// Copyright (c) 2019-2026 Chris Pulman and contributors. All rights reserved.
// Chris Pulman and contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using VSCodex.Commands;

namespace VSCodex.Services;

/// <summary>Provides the visual Studio Menu Integration Service implementation.</summary>
internal static class VisualStudioMenuIntegrationService
{
    /// <summary>Named number used by this type.</summary>
    private const int Numeric10 = 10;

    /// <summary>Named number used by this type.</summary>
    private const int Numeric2 = 2;

    /// <summary>Defines the mso Control Popup.</summary>
    private const int MsoControlPopup = Numeric10;

    /// <summary>Stores the open Tool Window.</summary>
    private static readonly MenuCommandSpec OpenToolWindow = new("View.VSCodex", "VSCodex", CodexCommandIds.OpenToolWindowCommandId);

    /// <summary>Stores the core Actions.</summary>
    private static readonly MenuCommandSpec[] CoreActions =
    {
        OpenToolWindow,
        new("VSCodex.Settings", "VSCodex 设置", CodexCommandIds.OpenOptionsCommandId),
        new("VSCodex.AddToChat", "添加到 VSCodex 对话", CodexCommandIds.AskCodexCommandId),
        new("VSCodex.Explain", "Explain", CodexCommandIds.ExplainSelectionCommandId),
        new("VSCodex.FixWithVSCodex", "使用 VSCodex 修复", CodexCommandIds.FixActiveErrorCommandId),
        new("VSCodex.FixSelection", "修复选中内容", CodexCommandIds.FixSelectionCommandId),
        new("VSCodex.Review", "审阅选中内容", CodexCommandIds.ReviewSelectionCommandId),
        new("VSCodex.Optimize", "优化选中内容", CodexCommandIds.OptimizeSelectionCommandId),
        new("VSCodex.GenerateComments", "生成注释", CodexCommandIds.GenerateDocsCommandId),
        new("VSCodex.GenerateTests", "生成测试", CodexCommandIds.CreateTestFromSelectionCommandId),
        new("VSCodex.Debug", "使用 VSCodex 调试", CodexCommandIds.DebugWithCodexCommandId),
        new("VSCodex.FixActiveException", "修复活动异常", CodexCommandIds.FixActiveExceptionCommandId),
        new("VSCodex.FixTestFailure", "修复测试失败", CodexCommandIds.FixTestFailureCommandId),
        new("VSCodex.CreatePlan", "创建智能体计划", CodexCommandIds.CreatePlanCommandId),
        new("VSCodex.ReactiveMemorySetup", "VSCodex ReactiveMemory 配置", CodexCommandIds.ConfigureMemoryCommandId)
    };

    /// <summary>Stores whether runtime menus have been installed.</summary>
    private static bool _installed;

    /// <summary>Initializes the operation.</summary>
    /// <param name="package">The package.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    internal static async Task InitializeAsync(AsyncPackage package)
    {
        if (_installed)
        {
            return;
        }

        for (var attempt = 1; attempt <= Numeric10 && !_installed; attempt++)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            try
            {
                var dte = await package.GetServiceAsync(typeof(DTE)).ConfigureAwait(true) as DTE;
                if (dte?.CommandBars is null)
                {
                    _ = ActivityLog.TryLogWarning(nameof(VisualStudioMenuIntegrationService), "DTE command bars were unavailable; VSCodex runtime menu bridge will retry.");
                }
                else if (InstallMainMenus(dte))
                {
                    InstallContextMenus();
                    _installed = true;
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(Numeric2), package.DisposalToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (package.DisposalToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _ = ActivityLog.TryLogError(nameof(VisualStudioMenuIntegrationService), ex.ToString());
                await Task.Delay(TimeSpan.FromSeconds(Numeric2), package.DisposalToken).ConfigureAwait(false);
            }
        }

        if (_installed)
        {
            return;
        }

        _ = ActivityLog.TryLogWarning(
            nameof(VisualStudioMenuIntegrationService),
            "VSCodex static VSCT menu entries are available, but runtime DTE menu repair did not resolve the commands during startup.");
    }

    /// <summary>Performs the install Main Menus operation.</summary>
    /// <param name="dte">The dte.</param>
    /// <returns><see langword="true"/> when install Main Menus succeeds; otherwise, <see langword="false"/>.</returns>
    private static bool InstallMainMenus(DTE dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var menuBar = TryGetCommandBar(dte.CommandBars, "MenuBar");
        if (menuBar is null)
        {
            return false;
        }

        AddTopLevelMenu(dte, menuBar);
        var openCommandVisible = AddViewMenuEntries(dte, menuBar);
        AddToolsMenuEntries(dte, menuBar);
        AddDebugMenuEntries(dte, menuBar);
        AddTestMenuEntries(dte, menuBar);
        return openCommandVisible;
    }

    /// <summary>Adds the top-level VSCodex menu.</summary>
    /// <param name="dte">The DTE.</param>
    /// <param name="menuBar">The menu bar.</param>
    private static void AddTopLevelMenu(DTE dte, object menuBar)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var topLevel = EnsurePopup(menuBar, "VSCodex");
        if (topLevel is null)
        {
            return;
        }

        _ = AddCommands(dte, GetPopupCommandBar(topLevel), CoreActions);
    }

    /// <summary>Adds View-menu entries.</summary>
    /// <param name="dte">The DTE.</param>
    /// <param name="menuBar">The menu bar.</param>
    /// <returns><see langword="true"/> when the open command is visible.</returns>
    private static bool AddViewMenuEntries(DTE dte, object menuBar)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var viewMenu = FindControlByCaption(menuBar, "视图", "View");
        if (viewMenu is null)
        {
            return false;
        }

        var viewCommandBar = GetPopupCommandBar(viewMenu);
        var openCommandVisible = viewCommandBar is not null && AddCommands(dte, viewCommandBar, [OpenToolWindow]) > 0;
        AddViewActionsPopup(dte, viewCommandBar);
        AddOtherWindowsEntry(dte, viewCommandBar);
        return openCommandVisible;
    }

    /// <summary>Adds the View-menu actions popup.</summary>
    /// <param name="dte">The DTE.</param>
    /// <param name="viewCommandBar">The View command bar.</param>
    private static void AddViewActionsPopup(DTE dte, object? viewCommandBar)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (viewCommandBar is null)
        {
            return;
        }

        var viewPopup = EnsurePopup(viewCommandBar, "VSCodex 操作");
        if (viewPopup is null)
        {
            return;
        }

        _ = AddCommands(dte, GetPopupCommandBar(viewPopup), CoreActions);
    }

    /// <summary>Adds the Other Windows menu entry.</summary>
    /// <param name="dte">The DTE.</param>
    /// <param name="viewCommandBar">The View command bar.</param>
    private static void AddOtherWindowsEntry(DTE dte, object? viewCommandBar)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (viewCommandBar is null)
        {
            return;
        }

        var otherWindowsPopup = FindControlByCaption(viewCommandBar, "其他窗口", "Other Windows");
        if (otherWindowsPopup is null)
        {
            return;
        }

        SetVisible(otherWindowsPopup);
        _ = AddCommands(dte, GetPopupCommandBar(otherWindowsPopup), [OpenToolWindow]);
    }

    /// <summary>Adds Tools-menu entries.</summary>
    /// <param name="dte">The DTE.</param>
    /// <param name="menuBar">The menu bar.</param>
    private static void AddToolsMenuEntries(DTE dte, object menuBar)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var toolsMenu = FindControlByCaption(menuBar, "工具", "Tools");
        if (toolsMenu is null)
        {
            return;
        }

        var toolsCommandBar = GetPopupCommandBar(toolsMenu);
        if (toolsCommandBar is null)
        {
            return;
        }

        var toolsPopup = EnsurePopup(toolsCommandBar, "VSCodex");
        if (toolsPopup is null)
        {
            return;
        }

        _ = AddCommands(dte, GetPopupCommandBar(toolsPopup), CoreActions);
    }

    /// <summary>Adds Debug-menu entries.</summary>
    /// <param name="dte">The DTE.</param>
    /// <param name="menuBar">The menu bar.</param>
    private static void AddDebugMenuEntries(DTE dte, object menuBar)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var debugMenu = FindControlByCaption(menuBar, "调试", "Debug");
        if (debugMenu is null)
        {
            return;
        }

        _ = AddCommands(
            dte,
            GetPopupCommandBar(debugMenu),
            [
                new MenuCommandSpec("VSCodex.Debug", "使用 VSCodex 调试", CodexCommandIds.DebugWithCodexCommandId),
                new MenuCommandSpec("VSCodex.FixActiveException", "修复活动异常", CodexCommandIds.FixActiveExceptionCommandId),
                new MenuCommandSpec("VSCodex.FixWithVSCodex", "使用 VSCodex 修复", CodexCommandIds.FixActiveErrorCommandId)
            ]);
    }

    /// <summary>Adds Test-menu entries.</summary>
    /// <param name="dte">The DTE.</param>
    /// <param name="menuBar">The menu bar.</param>
    private static void AddTestMenuEntries(DTE dte, object menuBar)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var testMenu = FindControlByCaption(menuBar, "测试", "Test");
        if (testMenu is null)
        {
            return;
        }

        _ = AddCommands(
            dte,
            GetPopupCommandBar(testMenu),
            [
                new MenuCommandSpec("VSCodex.GenerateTests", "生成测试", CodexCommandIds.CreateTestFromSelectionCommandId),
                new MenuCommandSpec("VSCodex.FixTestFailure", "修复测试失败", CodexCommandIds.FixTestFailureCommandId)
            ]);
    }

    /// <summary>Performs the install Context Menus operation.</summary>
    private static void InstallContextMenus()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // Context menus are declared through VSCT only. Runtime DTE insertion can
        // create duplicate editor entries after the command table is refreshed.
    }

    /// <summary>Adds commands.</summary>
    /// <param name="dte">The dte.</param>
    /// <param name="commandBar">The command Bar.</param>
    /// <param name="commands">The commands.</param>
    /// <returns>The add Commands result.</returns>
    private static int AddCommands(DTE dte, object? commandBar, IEnumerable<MenuCommandSpec> commands)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (commandBar is null)
        {
            return 0;
        }

        var position = 1;
        var placed = 0;
        foreach (var command in commands)
        {
            var dteCommand = TryGetCommand(dte, command);
            if (dteCommand is null)
            {
                _ = ActivityLog.TryLogWarning(nameof(VisualStudioMenuIntegrationService), $"VSCodex command '{command.CanonicalName}' was not available for runtime menu placement.");
                continue;
            }

            try
            {
                var existing = FindControlByCaption(commandBar, command.Caption);
                if (existing is not null)
                {
                    SetVisible(existing);
                    placed++;
                    continue;
                }

                var addedControl = dteCommand.AddControl(commandBar, position);
                position++;
                if (addedControl is not null)
                {
                    SetVisible(addedControl);
                    placed++;
                }
            }
            catch (Exception ex)
            {
                _ = ActivityLog.TryLogWarning(nameof(VisualStudioMenuIntegrationService), $"Could not add '{command.Caption}' to a Visual Studio menu: {ex.Message}");
            }
        }

        return placed;
    }

    /// <summary>Attempts to get Command.</summary>
    /// <param name="dte">The dte.</param>
    /// <param name="command">The command.</param>
    /// <returns>The try Get Command result.</returns>
    private static Command? TryGetCommand(DTE dte, MenuCommandSpec command)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return dte.Commands.Item(command.CanonicalName);
        }
        catch (Exception ex)
        {
            _ = ActivityLog.TryLogWarning(nameof(VisualStudioMenuIntegrationService), ex.Message);
        }

        try
        {
            return dte.Commands.Item(CodexCommandIds.CommandSetGuidString, command.CommandId);
        }
        catch (Exception ex)
        {
            _ = ActivityLog.TryLogWarning(nameof(VisualStudioMenuIntegrationService), ex.Message);
            return null;
        }
    }

    /// <summary>Attempts to get Command Bar.</summary>
    /// <param name="commandBars">The command Bars.</param>
    /// <param name="name">The name.</param>
    /// <returns>The try Get Command Bar result.</returns>
    private static object? TryGetCommandBar(object commandBars, string name)
    {
        try
        {
            return commandBars.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, commandBars, [name]);
        }
        catch (Exception ex)
        {
            _ = ActivityLog.TryLogWarning(nameof(VisualStudioMenuIntegrationService), ex.Message);
            return null;
        }
    }

    /// <summary>Ensures popup.</summary>
    /// <param name="commandBar">The command Bar.</param>
    /// <param name="caption">The caption.</param>
    /// <returns>The ensure Popup result.</returns>
    private static object? EnsurePopup(object commandBar, string caption)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var existing = FindControlByCaption(commandBar, caption);
        if (existing is not null)
        {
            SetVisible(existing);
            return existing;
        }

        try
        {
            var controls = GetControls(commandBar);
            if (controls is null)
            {
                return null;
            }

            var popup = controls.GetType().InvokeMember(
                "Add",
                BindingFlags.InvokeMethod,
                null,
                controls,
                [MsoControlPopup, Type.Missing, Type.Missing, Type.Missing, true]);

            SetProperty(popup, "Caption", caption);
            SetProperty(popup, "TooltipText", caption);
            SetVisible(popup);
            return popup;
        }
        catch (Exception ex)
        {
            _ = ActivityLog.TryLogWarning(nameof(VisualStudioMenuIntegrationService), $"Could not create '{caption}' menu popup: {ex.Message}");
            return null;
        }
    }

    /// <summary>Finds control By Caption. Accepts several candidate captions so the lookup works on both localized and English shells.</summary>
    /// <param name="commandBar">The command Bar.</param>
    /// <param name="captions">The candidate captions, tried in order.</param>
    /// <returns>The find Control By Caption result.</returns>
    private static object? FindControlByCaption(object commandBar, params string[] captions)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var controls = GetControls(commandBar);
        if (controls is null)
        {
            return null;
        }

        foreach (var control in EnumerateControls(controls))
        {
            var controlCaption = GetStringProperty(control, "Caption");
            foreach (var caption in captions)
            {
                if (string.Equals(NormalizeCaption(controlCaption), NormalizeCaption(caption), StringComparison.OrdinalIgnoreCase))
                {
                    return control;
                }
            }
        }

        return null;
    }

    /// <summary>Gets popup Command Bar.</summary>
    /// <param name="popup">The popup.</param>
    /// <returns>The get Popup Command Bar result.</returns>
    private static object? GetPopupCommandBar(object popup)
    {
        return GetProperty(popup, "CommandBar");
    }

    /// <summary>Gets controls.</summary>
    /// <param name="commandBar">The command Bar.</param>
    /// <returns>The get Controls result.</returns>
    private static object? GetControls(object commandBar)
    {
        return GetProperty(commandBar, "Controls");
    }

    /// <summary>Gets property.</summary>
    /// <param name="target">The target.</param>
    /// <param name="propertyName">The property Name.</param>
    /// <returns>The get Property result.</returns>
    private static object? GetProperty(object target, string propertyName)
    {
        try
        {
            return target.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, target, []);
        }
        catch (Exception ex)
        {
            _ = ActivityLog.TryLogWarning(nameof(VisualStudioMenuIntegrationService), ex.Message);
            return null;
        }
    }

    /// <summary>Gets string Property.</summary>
    /// <param name="target">The target.</param>
    /// <param name="propertyName">The property Name.</param>
    /// <returns>The get String Property result.</returns>
    private static string GetStringProperty(object target, string propertyName)
    {
        return (GetProperty(target, propertyName) as string) ?? string.Empty;
    }

    /// <summary>Sets property.</summary>
    /// <param name="target">The target.</param>
    /// <param name="propertyName">The property Name.</param>
    /// <param name="value">The value.</param>
    private static void SetProperty(object target, string propertyName, object value)
    {
        _ = target.GetType().InvokeMember(propertyName, BindingFlags.SetProperty, null, target, [value]);
    }

    /// <summary>Sets visible.</summary>
    /// <param name="target">The target.</param>
    private static void SetVisible(object target)
    {
        try
        {
            SetProperty(target, "Visible", true);
        }
        catch (Exception ex)
        {
            _ = ActivityLog.TryLogWarning(nameof(VisualStudioMenuIntegrationService), ex.Message);
        }
    }

    /// <summary>Performs the enumerate Controls operation.</summary>
    /// <param name="controls">The controls.</param>
    /// <returns>The enumerate Controls result.</returns>
    private static IEnumerable<object> EnumerateControls(object controls)
    {
        var count = 0;
        try
        {
            count = Convert.ToInt32(GetProperty(controls, "Count"));
        }
        catch (Exception ex)
        {
            _ = ActivityLog.TryLogWarning(nameof(VisualStudioMenuIntegrationService), ex.Message);
        }

        for (var i = 1; i <= count; i++)
        {
            object? control = null;
            try
            {
                control = controls.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, controls, [i]);
            }
            catch (Exception ex)
            {
                _ = ActivityLog.TryLogWarning(nameof(VisualStudioMenuIntegrationService), ex.Message);
            }

            if (control is not null)
            {
                yield return control;
            }
        }
    }

    /// <summary>Performs the normalize Caption operation.</summary>
    /// <param name="caption">The caption.</param>
    /// <returns>The normalize Caption result.</returns>
    private static string NormalizeCaption(string caption)
        => caption.Replace("&", string.Empty).Trim();

    /// <summary>Provides the menu Command Spec implementation.</summary>
    private sealed class MenuCommandSpec
    {
        /// <summary>Initializes a new instance of the <see cref="MenuCommandSpec"/> class.</summary>
        /// <param name="canonicalName">The canonical Name.</param>
        /// <param name="caption">The caption.</param>
        /// <param name="commandId">The command Id.</param>
        public MenuCommandSpec(string canonicalName, string caption, int commandId)
        {
            CanonicalName = canonicalName;
            Caption = caption;
            CommandId = commandId;
        }

        /// <summary>Gets the canonical Name.</summary>
        public string CanonicalName { get; }

        /// <summary>Gets the caption.</summary>
        public string Caption { get; }

        /// <summary>Gets the command Id.</summary>
        public int CommandId { get; }
    }
}
