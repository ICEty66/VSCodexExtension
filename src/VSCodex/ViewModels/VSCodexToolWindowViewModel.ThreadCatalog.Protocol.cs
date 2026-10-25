// Copyright (c) 2019-2026 Chris Pulman and contributors. All rights reserved.
// Chris Pulman and contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace VSCodex.ViewModels;

/// <summary>Reads display text from public protocol history.</summary>
public sealed partial class VSCodexToolWindowViewModel
{
    /// <summary>Determines whether an already imported conversation still matches the catalog entry.</summary>
    /// <param name="known">The imported document, if any.</param>
    /// <param name="archived">The archive state.</param>
    /// <param name="title">The catalog title.</param>
    /// <param name="updatedSeconds">The catalog update stamp.</param>
    /// <param name="identity">The current workspace identity.</param>
    /// <returns><see langword="true"/> when no local rewrite is required.</returns>
    private static bool IsCatalogEntryUnchanged(
        CodexSessionDocument? known,
        bool archived,
        string title,
        long? updatedSeconds,
        WorkspaceIdentity identity)
    {
        if (known is null || known.IsArchived != archived || !string.Equals(known.Title, title, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(known.WorkspaceIdentityId, identity?.Id ?? string.Empty, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return updatedSeconds is null || known.Updated.ToUnixTimeSeconds() == updatedSeconds.Value;
    }

    /// <summary>Imports user and assistant text while retaining opaque server history on the server.</summary>
    /// <param name="session">The local display document.</param>
    /// <param name="thread">The public protocol thread.</param>
    private static void ImportThreadMessages(CodexSessionDocument session, JObject? thread)
    {
        foreach (JObject turn in thread?["turns"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
        {
            foreach (JObject item in turn["items"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                string? type = item.Value<string>("type");
                string text = ReadThreadItemText(item, type);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    session.Messages.Add(new ChatMessage { Role = type == "userMessage" ? CodexMessageRole.User : CodexMessageRole.Assistant, Content = text });
                }
            }
        }
    }

    /// <summary>Reads the textual display portion of a public thread item.</summary>
    /// <param name="item">The protocol item.</param>
    /// <param name="type">The item kind.</param>
    /// <returns>The user or assistant text, or an empty string for tools.</returns>
    private static string ReadThreadItemText(JObject item, string? type)
    {
        if (type == "agentMessage")
        {
            return item.Value<string>(TextPropertyName) ?? string.Empty;
        }

        if (type != "userMessage")
        {
            return string.Empty;
        }

        IEnumerable<JObject> content = item["content"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>();
        return string.Join(Environment.NewLine, content.Where(x => x.Value<string>("type") == TextPropertyName).Select(x => x.Value<string>(TextPropertyName)));
    }
}
