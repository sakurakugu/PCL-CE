using System.Diagnostics;
using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.App.Essentials;
using PCL.Core.App.IoC;
using PCL.Core.Utils.OS;

namespace PCL;

internal static class Program
{
    /// <summary>
    /// Program startup point
    /// </summary>
    [STAThread]
    public static void Main()
    {
        // 如果是CLI打开，如果不开新的控制台，就将数据传入到父进程的CLI中
        if (Basics.CommandLineArguments.Contains("--console")) KernelInterop.AllocateConsole();
        else if (!KernelInterop.TryAttachParentConsole()) KernelInterop.RefreshConsoleStreams();
        if (CliHelp.TryHandleStandalone()) return;
#if DEBUG
        if (Basics.CommandLineArguments.Contains("--debug"))
        {
            Console.WriteLine("Waiting for debugger...");
            while (!Debugger.IsAttached) Thread.Sleep(50);
        }
#endif
        Console.WriteLine("Welcome to Plain Craft Launcher 2 Community Edition!");
        // Preloading tasks
        ApplicationService.Loading = static () =>
        {
            var app = new Application();
            app.InitializeComponent();
            return app;
        };
        MainWindowService.Loading = static () =>
        {
            var form = new FormMain();
            return form;
        };
        // From dotnet/wpf #2393: fix tablet devices broken on .NET Core 3.0+
        _ = Tablet.TabletDevices;
        // 等待窗口初始化回调完成后再注册并处理游戏启动命令。
        Lifecycle.When(LifecycleState.Running, LaunchCommandRunner.TryHandle);
        // Start lifecycle
        Lifecycle.OnInitialize();
    }
}