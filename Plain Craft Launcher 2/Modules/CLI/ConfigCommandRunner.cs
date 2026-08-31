using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using PCL.Core.App;
using PCL.Core.App.Cli;
using PCL.Core.App.Configuration;
using PCL.Core.App.Essentials;
using PCL.Core.App.IoC;

namespace PCL;

/// <summary>
/// 处理启动器配置项的查看与修改命令。
/// </summary>
internal static class ConfigCommandRunner
{
    private const string ResultPrefix = "PCL_CLI_RESULT=";
    private const string GetKey = "get";
    private const string SetKey = "set";
    private const string ResetKey = "reset";
    private const string ListKey = "list";

    // CLI 结果面向终端用户显示，默认使用缩进格式并保留中文字符。
    private static readonly JsonSerializerOptions CliJsonSerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 注册 config 命令回调并处理启动时已经收到的命令。
    /// </summary>
    public static void TryHandle()
    {
        StartupService.TryHandleCommand("config", Handle, registerCallback: true);
    }

    private static void Handle(CommandLine command, bool isCallback)
    {
        if (IsHelp(command))
        {
            CliHelp.ShowConfigHelp(shutdown: !isCallback);
            return;
        }

        var operations = new List<(string key, string? value)>();
        if (TryGetText(command, GetKey, out var getKey)) operations.Add((GetKey, getKey));
        if (TryGetText(command, SetKey, out var setValue)) operations.Add((SetKey, setValue));
        if (TryGetText(command, ResetKey, out var resetKey)) operations.Add((ResetKey, resetKey));
        if (TryGetBool(command, ListKey, out var list) && list) operations.Add((ListKey, null));

        if (operations.Count == 0)
        {
            CliHelp.ShowConfigHelp(shutdown: !isCallback);
            return;
        }

        if (operations.Count > 1)
        {
            CompleteData("error", null, "一次只能执行一个 config 操作", isCallback, ModBase.ProcessReturnValues.Cancel);
            return;
        }

        var operation = operations[0];
        switch (operation.key)
        {
            case ListKey:
                CompleteData("ok", ListSafeItems(), null, isCallback, ModBase.ProcessReturnValues.Success);
                return;
            case GetKey:
                if (!TryGetSafeItem(operation.value!, out var getItem, out var getError))
                {
                    CompleteData("error", null, getError, isCallback, ModBase.ProcessReturnValues.Cancel);
                    return;
                }
                CompleteData("ok", new { key = getItem!.Key, type = getItem.Type.Name, value = getItem.GetValueNoType()?.ToString(), is_default = getItem.IsDefault() },
                    null, isCallback, ModBase.ProcessReturnValues.Success);
                return;
            case ResetKey:
                if (!TryGetSafeItem(operation.value!, out var resetItem, out var resetError))
                {
                    CompleteData("error", null, resetError, isCallback, ModBase.ProcessReturnValues.Cancel);
                    return;
                }
                resetItem!.Reset();
                CompleteData("ok", new { key = resetItem.Key, value = resetItem.GetValueNoType()?.ToString(), is_default = true },
                    null, isCallback, ModBase.ProcessReturnValues.Success);
                return;
            case SetKey:
                string? assignmentError = null, setError = null, convertError = null;
                ConfigItem? setItem = null;
                object? converted = null;
                if (!TryParseAssignment(operation.value!, out var assignmentKey, out var assignmentValue, out assignmentError))
                {
                    CompleteData("error", null, assignmentError, isCallback, ModBase.ProcessReturnValues.Cancel);
                    return;
                }
                if (!TryGetSafeItem(assignmentKey!, out setItem, out setError))
                {
                    CompleteData("error", null, setError, isCallback, ModBase.ProcessReturnValues.Cancel);
                    return;
                }
                if (!TryConvertValue(setItem!.Type, assignmentValue!, out converted, out convertError))
                {
                    CompleteData("error", null, convertError, isCallback, ModBase.ProcessReturnValues.Cancel);
                    return;
                }
                if (!setItem!.SetValueNoType(converted!))
                {
                    CompleteData("error", null, "配置项拒绝了该值", isCallback, ModBase.ProcessReturnValues.Cancel);
                    return;
                }
                CompleteData("ok", new { key = setItem.Key, value = setItem.GetValueNoType()?.ToString(), is_default = setItem.IsDefault() },
                    null, isCallback, ModBase.ProcessReturnValues.Success);
                return;
        }
    }

    private static bool TryGetText(CommandLine command, string key, out string? value)
    {
        foreach (var arg in command.Arguments)
            if (string.Equals(arg.Key, key, StringComparison.OrdinalIgnoreCase))
                return (value = arg.Value.ValueText).Length > 0;
        value = null;
        return false;
    }

    private static bool TryGetBool(CommandLine command, string key, out bool value)
    {
        var (exists, valid) = command.TryGetArgumentValue<bool>(key, out value);
        return exists && valid;
    }

    private static bool TryGetSafeItem(string key, out ConfigItem? item, out string? error)
    {
        item = null;
        error = null;
        if (string.IsNullOrWhiteSpace(key)) { error = "配置键不能为空"; return false; }
        if (!ConfigService.TryGetConfigItemNoType(key, out item)) { error = "未知配置键：" + key; return false; }
        if (item.Source != ConfigSource.Shared && item.Source != ConfigSource.Local)
        {
            error = "该配置项不支持 CLI 操作";
            item = null;
            return false;
        }
        return true;
    }

    private static IReadOnlyDictionary<string, string?> ListSafeItems() => ConfigService.KeySet
        .Where(key => ConfigService.TryGetConfigItemNoType(key, out var item) &&
                      item!.Source is ConfigSource.Shared or ConfigSource.Local)
        .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            key => key,
            key =>
            {
                ConfigService.TryGetConfigItemNoType(key, out var item);
                return item!.GetValueNoType()?.ToString();
            },
            StringComparer.OrdinalIgnoreCase);

    private static bool TryParseAssignment(string text, out string? key, out string? value, out string? error)
    {
        var separator = text.IndexOf('=');
        if (separator <= 0) { key = value = null; error = "--set 需要使用 key=value 格式"; return false; }
        key = text[..separator].Trim();
        value = text[(separator + 1)..];
        error = null;
        return key.Length > 0;
    }

    private static bool TryConvertValue(Type type, string text, out object? value, out string? error)
    {
        value = null;
        error = null;
        try
        {
            if (type == typeof(string)) value = text;
            else if (type == typeof(bool))
            {
                if (!bool.TryParse(text, out var boolValue) && text is not ("0" or "1")) throw new FormatException();
                value = text is "1" || (bool.TryParse(text, out boolValue) && boolValue);
            }
            else if (type.IsEnum) value = Enum.Parse(type, text, ignoreCase: true);
            else value = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            error = $"无法将 '{text}' 转换为 {type.Name}";
            return false;
        }
    }

    private static void CompleteData(string status, object? data, string? message, bool isCallback, ModBase.ProcessReturnValues exitCode)
    {
        if (message is not null) Console.WriteLine(message);
        Console.WriteLine(ResultPrefix + JsonSerializer.Serialize(new { status, command = "config", data, message }, CliJsonSerializerOptions));
        if (!isCallback) Lifecycle.Shutdown((int)exitCode);
    }

    private static bool IsHelp(CommandLine command)
    {
        var (exists, isTypeMatch) = command.TryGetArgumentValue<bool>("help", out var help);
        return exists && isTypeMatch && help;
    }

}
