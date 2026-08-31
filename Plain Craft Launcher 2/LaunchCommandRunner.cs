using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
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

    private static void Handle(PCL.Core.App.Cli.CommandLine command, bool isCallback)
    {
        _ = isCallback;
        var (exists, isText) = command.TryGetArgumentValue<string>("instance", out var instanceName);
        if (!exists || !isText || string.IsNullOrWhiteSpace(instanceName))
        {
            Complete("error", "arguments", null, "缺少文本参数 --instance <实例目录名>", null,
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

        new Thread(() => PrepareAndLaunch(instanceName, username!))
        {
            IsBackground = true,
            Name = "CLI Launch"
        }.Start();
    }

    private static bool IsValidUsername(string? username)
    {
        return username is { Length: >= 3 and <= 16 } &&
               username.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    }

    private static void PrepareAndLaunch(string instanceName, string username)
    {
        try
        {
            // 页面会异步读取游戏目录和实例列表，命令模式必须等待该流程结束后再覆盖选择。
            while (!Lifecycle.HasShutdownStarted && !IsLaunchPageReady())
                Thread.Sleep(50);
            if (Lifecycle.HasShutdownStarted) return;

            var instance = ModInstanceList.mcInstanceList.Values
                .SelectMany(x => x)
                .FirstOrDefault(x => string.Equals(x.Name, instanceName, StringComparison.Ordinal));
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
