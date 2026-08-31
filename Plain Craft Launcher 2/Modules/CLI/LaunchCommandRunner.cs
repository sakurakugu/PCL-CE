using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using PCL.Core.App;
using PCL.Core.App.Cli;
using PCL.Core.App.Essentials;
using PCL.Core.App.IoC;
using PCL.Core.Minecraft.Profile;
using PCL.Core.Minecraft.Profile.Models;

namespace PCL;

/// <summary>
/// 处理非交互式游戏启动命令。
/// </summary>
internal static class LaunchCommandRunner
{
    private const string DefaultUsername = "Steve";
    private const string ResultPrefix = "PCL_CLI_RESULT=";
    private static int _isCompleted;

    /// <summary>
    /// 注册 launch 命令回调并处理启动时已经收到的命令。
    /// </summary>
    public static void TryHandle()
    {
        StartupService.TryHandleCommand("launch", Handle, registerCallback: true);
    }

    private static void Handle(CommandLine command, bool isCallback)
    {
        _ = isCallback;
        if (IsHelp(command))
        {
            CliHelp.ShowLaunchHelp();
            return;
        }

        var (exists, isText) = command.TryGetArgumentValue<string>("instance", out var instanceName);
        if (!exists || !isText || string.IsNullOrWhiteSpace(instanceName))
        {
            Complete("error", "arguments", null, "缺少文本参数 --instance <实例目录名或路径>", null,
                ModBase.ProcessReturnValues.Cancel);
            return;
        }
        instanceName = instanceName.Trim();

        var (hasFolder, isFolderText) = command.TryGetArgumentValue<string>("folder", out var folderPath);
        folderPath = hasFolder ? folderPath?.Trim() : null;
        if (hasFolder && (!isFolderText || string.IsNullOrWhiteSpace(folderPath)))
        {
            Complete("error", "arguments", instanceName, "--folder 必须为有效的 Minecraft 游戏目录路径", null,
                ModBase.ProcessReturnValues.Cancel);
            return;
        }

        var (hasUsername, isUsernameText) = command.TryGetArgumentValue<string>("username", out var username);
        username = hasUsername ? username?.Trim() : DefaultUsername;
        if (hasUsername && (!isUsernameText || !IsValidUsername(username)))
        {
            Complete("error", "arguments", instanceName,
                "--username 必须为 3 到 16 位英文字母、数字或下划线", null,
                ModBase.ProcessReturnValues.Cancel);
            return;
        }

        new Thread(() => PrepareAndLaunch(instanceName!, username!, folderPath))
        {
            IsBackground = true,
            Name = "CLI Launch"
        }.Start();
    }

    private static bool IsHelp(CommandLine command)
    {
        var (exists, isTypeMatch) = command.TryGetArgumentValue<bool>("help", out var help);
        return exists && isTypeMatch && help;
    }

    private static bool IsValidUsername(string? username)
    {
        return username is { Length: >= 3 and <= 16 } &&
               username.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    }

    private static void PrepareAndLaunch(string instanceName, string username, string? folderSelector)
    {
        try
        {
            // 页面会异步读取游戏目录和实例列表，命令模式必须等待该流程结束后再覆盖选择。
            while (!Lifecycle.HasShutdownStarted && !IsLaunchPageReady())
                Thread.Sleep(50);
            if (Lifecycle.HasShutdownStarted) return;

            if (!string.IsNullOrWhiteSpace(folderSelector))
            {
                if (!EnsureMinecraftFolder(folderSelector, out _, out var folderError))
                {
                    Complete("error", "folder", instanceName, folderError, null,
                        ModBase.ProcessReturnValues.Cancel);
                    return;
                }
            }

            var instance = FindInstance(instanceName);
            if (instance is null && string.IsNullOrWhiteSpace(folderSelector) &&
                TryGetInstanceLocation(instanceName, out var instancePath, out var inferredFolder))
            {
                if (!EnsureMinecraftFolder(inferredFolder, out _, out var folderError))
                {
                    Complete("error", "folder", instanceName, folderError, null,
                        ModBase.ProcessReturnValues.Cancel);
                    return;
                }

                instance = FindInstance(instancePath);
            }

            if (instance is null)
            {
                Complete("error", "instance", instanceName, "未找到指定的 Minecraft 实例", null,
                    ModBase.ProcessReturnValues.Cancel);
                return;
            }

            ModBase.RunInUi(() => Start(instance, username));
        }
        catch (Exception ex)
        {
            Complete("error", "prepare", instanceName, ex.Message, null, ModBase.ProcessReturnValues.Fail);
        }
    }

    private static McInstance? FindInstance(string selector)
    {
        var instances = ModInstanceList.mcInstanceList.Values.SelectMany(x => x);
        if (LooksLikePath(selector) && TryNormalizePath(selector, out var path))
            return instances.FirstOrDefault(x => PathsEqual(x.PathInstance, path));

        var byName = instances.FirstOrDefault(x => string.Equals(x.Name, selector, StringComparison.Ordinal));
        if (byName is not null) return byName;

        if (!TryNormalizePath(selector, out var normalizedPath)) return null;
        return instances.FirstOrDefault(x => PathsEqual(x.PathInstance, normalizedPath));
    }

    private static bool LooksLikePath(string value)
    {
        return value.StartsWith("$", StringComparison.Ordinal) ||
               value.Contains(Path.DirectorySeparatorChar) ||
               value.Contains(Path.AltDirectorySeparatorChar) ||
               Path.IsPathRooted(value);
    }

    private static bool TryGetInstanceLocation(
        string selector,
        out string instancePath,
        out string folderPath)
    {
        instancePath = string.Empty;
        folderPath = string.Empty;
        if (!TryNormalizePath(selector, out var candidate) || !Directory.Exists(candidate)) return false;

        var instanceDirectory = new DirectoryInfo(candidate);
        var versionsDirectory = instanceDirectory.Parent;
        var minecraftDirectory = versionsDirectory?.Parent;
        if (versionsDirectory is null || minecraftDirectory is null ||
            !string.Equals(versionsDirectory.Name, "versions", StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(versionsDirectory.FullName))
            return false;

        instancePath = EnsureTrailingSeparator(instanceDirectory.FullName);
        folderPath = EnsureTrailingSeparator(minecraftDirectory.FullName);
        return true;
    }

    private static bool EnsureMinecraftFolder(
        string selector,
        out string? folderPath,
        out string? error)
    {
        folderPath = null;
        error = null;
        try
        {
            if (!TryNormalizePath(selector, out var normalizedPath))
            {
                error = "--folder 必须为有效的 Minecraft 游戏目录路径";
                return false;
            }

            var path = EnsureTrailingSeparator(normalizedPath);
            if (!Directory.Exists(path))
            {
                error = "Minecraft 游戏目录不存在：" + path;
                return false;
            }

            if (!Directory.Exists(Path.Combine(path, "versions")))
            {
                error = "指定目录不是有效的 Minecraft 游戏目录（缺少 versions 文件夹）：" + path;
                return false;
            }

            if (!ModBase.CheckPermission(path))
            {
                error = "PCL 没有访问 Minecraft 游戏目录的权限：" + path;
                return false;
            }

            if (path.Contains('!') || path.Contains(';'))
            {
                error = "Minecraft 游戏目录路径不能包含 ! 或 ;：" + path;
                return false;
            }

            var folders = new List<string>(
                ((string?)States.Game.Folders ?? string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries));
            var isListed = folders.Any(item =>
            {
                var separator = item.IndexOf('>');
                return separator >= 0 && PathsEqual(
                    item[(separator + 1)..].Replace("$", ModBase.exePath), path);
            });
            if (!isListed)
            {
                var displayName = ModBase.GetFolderNameFromPath(path);
                if (string.IsNullOrWhiteSpace(displayName) || displayName.Contains('>') || displayName.Contains('|'))
                {
                    error = "无法为 Minecraft 游戏目录生成有效的显示名称：" + path;
                    return false;
                }

                folders.Add($"{displayName}>{path}");
                States.Game.Folders = string.Join('|', folders);
            }

            States.Game.SelectedFolder = path.Replace(ModBase.exePath, "$");
            ModFolder.mcFolderSelected = path;
            ModFolder.mcFolderListLoader.WaitForExit(isForceRestart: true);
            ModLoader.LoaderFolderRun(ModInstanceList.mcInstanceListLoader, path,
                ModLoader.LoaderFolderRunType.ForceRun, 1, @"versions\", waitForExit: true);
            folderPath = path;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryNormalizePath(string value, out string path)
    {
        path = string.Empty;
        try
        {
            var candidate = value.Trim().Trim('"');
            if (candidate.Length == 0) return false;
            if (candidate.StartsWith("$", StringComparison.Ordinal))
                candidate = ModBase.exePath + candidate[1..];
            path = Path.GetFullPath(candidate);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
               Path.DirectorySeparatorChar;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            EnsureTrailingSeparator(left),
            EnsureTrailingSeparator(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLaunchPageReady()
    {
        var ready = false;
        ModBase.RunInUiWait(() =>
        {
            ready = ModInstanceList.mcInstanceListLoader.State == ModBase.LoadState.Finished &&
                    ModMain.frmLaunchLeft is not null;
        });
        return ready;
    }

    private static void Start(McInstance instance, string username)
    {
        // 非交互模式一律使用/创建离线档案，直接跳过正版（微软）账号的 OAuth 校验，
        // 防止测试的时候被网络等情况卡住
        SelectOfflineProfile(username);

        ModLoader.LoaderBase.OnStateChangedUiEventHandler? handler = null;
        handler = (_, newState, _) =>
        {
            if (newState is not (ModBase.LoadState.Finished or ModBase.LoadState.Failed or ModBase.LoadState.Aborted))
                return;
            ModLaunch.mcLaunchLoader.OnStateChangedUi -= handler;
            var error = newState == ModBase.LoadState.Failed ? ModLaunch.mcLaunchLoader.Error?.Message : null;
            var status = newState == ModBase.LoadState.Finished ? "started" : "error";
            var code = newState == ModBase.LoadState.Finished
                ? ModBase.ProcessReturnValues.Success
                : newState == ModBase.LoadState.Aborted
                    ? ModBase.ProcessReturnValues.Cancel
                    : ModBase.ProcessReturnValues.Fail;
            Complete(status, "launch", instance.Name, error, ModLaunch.mcLaunchProcess?.Id, code);
        };
        ModLaunch.mcLaunchLoader.OnStateChangedUi += handler;
        ModMain.frmMain.Hide();
        if (ModLaunch.McLaunchStart(new ModLaunch.McLaunchOptions
            {
                instance = instance,
                IsNonInteractive = true,
            }))
            return;

        ModLaunch.mcLaunchLoader.OnStateChangedUi -= handler;
        Complete("error", "launch", instance.Name, "启动器当前无法开始新的启动任务", null,
            ModBase.ProcessReturnValues.Fail);
    }

    private static void SelectOfflineProfile(string username)
    {
        ProfileService.Load();
        var profile = ProfileService.Profiles.FirstOrDefault(item =>
            item.ProfileType == ProfileType.Offline &&
            string.Equals(item.UserName, username, StringComparison.Ordinal));
        if (profile is not null)
        {
            ProfileService.Select(profile);
            ModLaunch.McLaunchLog($"非交互模式已选择离线档案 {username}");
            return;
        }

        var uuid = ProfileUi.GetOfflineUuid(username);
        ProfileService.Add(new McProfile
        {
            ProfileType = ProfileType.Offline,
            Uuid = uuid,
            UserName = username,
            Description = string.Empty,
            AccessToken = uuid,
            ClientToken = uuid,
            ProfileId = Guid.NewGuid().ToString("N")
        });
        ModLaunch.McLaunchLog($"非交互模式已创建并选择离线档案 {username}");
    }

    private static void Complete(
        string status,
        string stage,
        string? instance,
        string? message,
        int? pid,
        ModBase.ProcessReturnValues exitCode)
    {
        if (Interlocked.Exchange(ref _isCompleted, 1) != 0) return;
        try
        {
            message = message is null ? null : McLogFilter.FilterUserName(McLogFilter.FilterAccessToken(message, '*'), '*');
            var result = JsonSerializer.Serialize(new { status, stage, instance, message, pid, log = @"PCL\Log" });
            Console.WriteLine(ResultPrefix + result);
        }
        finally
        {
            Lifecycle.Shutdown((int)exitCode);
        }
    }
}
