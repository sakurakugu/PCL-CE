using System;
using System.Linq;
using PCL.Core.App;
using PCL.Core.App.Essentials;
using PCL.Core.App.IoC;

namespace PCL;

/// <summary>
/// 全局命令行帮助及命令帮助文本。
/// </summary>
internal static class CliHelp
{
    private static readonly string[] KnownCommands = ["launch", "config", "update", "activate", "promote"];

    /// <summary>
    /// 处理不带子命令的全局 --help。返回是否已处理。
    /// </summary>
    public static bool TryHandleStandalone()
    {
        var args = Basics.CommandLineArguments;
        if (!args.Any(arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)) ||
            args.Any(arg => KnownCommands.Any(command =>
                string.Equals(arg, command, StringComparison.OrdinalIgnoreCase))))
            return false;

        ShowGlobalHelp();
        return true;
    }

    public static void ShowGlobalHelp()
    {
        Console.WriteLine(
            "PCL - Minecraft 启动器\n" +
            "用法：PCL <命令> [选项]\n" +
            "命令：\n" +
            "  launch      启动 Minecraft 实例\n" +
            "  config      查看或修改启动器配置项\n" +
            "  update      执行更新操作\n" +
            "  activate    激活启动器\n" +
            "  promote     执行推广操作\n" +
            "使用 PCL <命令> --help 查看对应命令的详细帮助。");
    }

    public static void ShowLaunchHelp()
    {
        Console.WriteLine(
            "PCL launch - 启动 Minecraft 实例\n" +
            "用法：PCL launch --instance <实例目录名或路径> [--folder <游戏目录>] [--username <用户名>]\n" +
            "选项：\n" +
            "  --instance  要启动的实例名称，或 versions 下实例目录的完整路径（必填）\n" +
            "  --folder    Minecraft 游戏目录；未登记时会自动导入并切换到该目录\n" +
            "  --username  离线用户名（默认 Steve）\n" +
            "  --help      显示此帮助信息");
        Lifecycle.Shutdown((int)ModBase.ProcessReturnValues.Success);
    }

    public static void ShowConfigHelp(bool shutdown = true)
    {
        Console.WriteLine(
            "PCL config - 查看或修改启动器配置项\n" +
            "用法：PCL config [操作] [--help]\n" +
            "操作：\n" +
            "  --list                      列出可通过 CLI 管理的配置项\n" +
            "  --get <键>                  查看配置项\n" +
            "  --set <键=值>               修改配置项\n" +
            "  --reset <键>                恢复配置项默认值\n" +
            "  --help                      显示此帮助信息\n" +
            "仅支持非敏感的全局配置；加密配置和实例级配置不可通过 CLI 修改。\n");
        if (shutdown) Lifecycle.Shutdown((int)ModBase.ProcessReturnValues.Success);
    }
}
