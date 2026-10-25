// Copyright (c) 2019-2026 Chris Pulman and contributors. All rights reserved.
// Chris Pulman and contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace VSCodex.Models;

/// <summary>Describes a flat MCP form and validates the user's typed response.</summary>
public sealed class ElicitationForm
{
    /// <summary>Gets the fields requested by the server.</summary>
    /// <returns>The operation result.</returns>
    public List<ElicitationField> Fields { get; } = new();

    /// <summary>Gets the server's prompt.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>Gets an actionable explanation for unsupported schemas.</summary>
    public string UnsupportedReason { get; private set; } = string.Empty;

    /// <summary>Creates a form from MCP elicitation parameters.</summary>
    /// <param name="parameters">The parameters value.</param>
    /// <returns>The operation result.</returns>
    public static ElicitationForm Create(JObject? parameters)
    {
        try
        {
            return CreateCore(parameters);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is InvalidCastException || ex is OverflowException)
        {
            return new ElicitationForm { UnsupportedReason = "服务器提供的表单架构格式有误。请选择“拒绝”或“取消”以继续。" };
        }
    }

    /// <summary>Validates all fields and builds correctly typed JSON content.</summary>
    /// <param name="content">The content value.</param>
    /// <param name="error">The error value.</param>
    /// <returns>The operation result.</returns>
    public bool TryBuildContent(out JObject? content, out string error)
    {
        content = null;
        error = UnsupportedReason;
        if (error.Length > 0)
        {
            return false;
        }

        var result = new JObject();
        foreach (ElicitationField field in Fields)
        {
            if (!field.TryRead(out JToken? value, out error))
            {
                return false;
            }

            if (value is not null)
            {
                result[field.Name] = value;
            }
        }

        content = result;
        return true;
    }

    /// <summary>Performs CreateCore.</summary>
    /// <param name="parameters">The parameters value.</param>
    /// <returns>The operation result.</returns>
    private static ElicitationForm CreateCore(JObject? parameters)
    {
        var form = new ElicitationForm { Message = parameters?.Value<string>("message") ?? string.Empty };
        JObject? schema = parameters?["requestedSchema"] as JObject;
        if (!IsFormMode(parameters) || schema?.Value<string>("type") != "object"
            || schema["properties"] is not JObject properties)
        {
            form.UnsupportedReason = "该请求使用了不受支持的表单或 URL 模式。请选择“拒绝”或“取消”以继续。";
            return form;
        }

        var allowedRoot = new[] { "type", "properties", "required", "title", "description", "$schema", "additionalProperties" };
        if (schema.Properties().Any(p => !allowedRoot.Contains(p.Name)))
        {
            form.UnsupportedReason = "该表单使用了不受支持的对象约束。请选择“拒绝”或“取消”以继续。";
            return form;
        }

        PopulateFields(form, schema, properties);
        return form;
    }

    /// <summary>Checks the requested elicitation mode.</summary>
    /// <param name="parameters">The request parameters.</param>
    /// <returns>Whether the server requested form mode.</returns>
    private static bool IsFormMode(JObject? parameters) => (parameters?.Value<string>("mode") ?? "form") == "form";

    /// <summary>Performs PopulateFields.</summary>
    /// <param name="form">The form value.</param>
    /// <param name="schema">The schema value.</param>
    /// <param name="properties">The properties value.</param>
    private static void PopulateFields(ElicitationForm form, JObject schema, JObject properties)
    {
        var required = schema["required"] as JArray ?? new JArray();
        foreach (JProperty property in properties.Properties())
        {
            if (property.Value is not JObject definition || !ElicitationField.IsSupported(definition))
            {
                form.UnsupportedReason = $"字段“{property.Name}”使用了不受支持的架构。请选择“拒绝”或“取消”以继续。";
                return;
            }

            form.Fields.Add(new(property.Name, definition, required.Any(r => r.Value<string>() == property.Name)));
        }

        if (required.Any(r => !properties.ContainsKey(r.Value<string>() ?? string.Empty)))
        {
            form.UnsupportedReason = "该表单需要一个未定义字段。请选择“拒绝”或“取消”以继续。";
        }
    }
}
