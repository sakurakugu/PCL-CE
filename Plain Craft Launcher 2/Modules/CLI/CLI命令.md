# PCL CE 命令行参考

```text
PCL2.exe [全局选项] [命令] [命令选项]
```

GUI 版本也支持这些参数；未指定命令时正常打开启动器。命令结果写入当前控制台，并通过退出码表示成功或失败。查看帮助：

```powershell
& .\PCL2.exe --help
```

## 命令

| 命令       | 说明                            |
| ---------- | ------------------------------- |
| `launch`   | 启动 Minecraft 实例（离线档案） |
| `config`   | 查看或修改启动器配置            |
| `update`   | 更新器使用的内部命令            |
| `activate` | 单实例通信使用的内部命令        |
| `promote`  | 提权通信使用的内部命令          |

## launch

```text
PCL2.exe launch --instance <实例名称或路径> [--folder <游戏目录>] [--username <用户名>]
```

| 选项                | 说明                                                                  |
| ------------------- | --------------------------------------------------------------------- |
| `--instance <值>`   | 必填。实例名称，或 `versions` 下实例目录的完整路径                    |
| `--folder <路径>`   | 可选。Minecraft 游戏目录，必须存在且包含 `versions`；未登记时自动导入 |
| `--username <名称>` | 可选。离线用户名（3 至 16 位英文字母、数字或下划线），默认 `Steve`    |
| `--help`            | 显示命令帮助                                                          |

示例：

```powershell
& .\PCL2.exe launch --instance 1.20.1
& .\PCL2.exe launch --instance "C:\Games\.minecraft\versions\1.20.1" --folder "C:\Games\.minecraft" --username Alex_123
```

非交互启动结束时输出一行以 `PCL_CLI_RESULT=` 开头的 JSON，例如：

```text
PCL_CLI_RESULT={"status":"started","stage":"launch","instance":"1.20.1","pid":12345,"log":"PCL\\Log"}
```

失败时 `status` 为 `error`，`stage` 标明阶段，`message` 给出原因。

## config

```text
PCL2.exe config [--list|--get <键>|--set <键=值>|--reset <键>] [--help]
```

使用 `--list` 列出安全配置项，`--get` 查询，`--set key=value` 修改，或 `--reset` 恢复默认值。加密配置和实例级配置不会通过 CLI 暴露。

示例：

```powershell
& .\PCL2.exe config --help
& .\PCL2.exe config --get SystemTelemetry
& .\PCL2.exe config --set SystemTelemetry=true
& .\PCL2.exe config --reset SystemTelemetry
```

结果同样以 `PCL_CLI_RESULT=` 开头。已有启动器进程时，修改会转发给主进程。

## 内部命令

不建议手动调用，参数属于启动器内部协议，可能随版本调整。

```text
PCL2.exe update <旧进程 ID> <目标文件> <来源文件> <是否重启>
PCL2.exe update_finished <来源文件>
PCL2.exe update_failed <失败原因>
```

`activate` 用于请求已有进程激活主窗口；`promote` 用于建立管理员权限进程和通信管道。

## 全局选项

| 选项              | 说明                                       |
| ----------------- | ------------------------------------------ |
| `--help`          | 显示全局帮助                               |
| `--console`       | 为 GUI 程序分配控制台                      |
| `--debug`         | Debug 构建等待调试器附加；Release 构建忽略 |
| `--trace-traffic` | 启用网络流量跟踪日志                       |

## 构建产物

`dotnet publish` 后，可直接使用仓库根目录的单文件：

```text
build\PCL2_Debug.exe
build\PCL2_Release.exe
```

不要单独复制 `bin` 下的 apphost，它依赖同目录的 DLL 和运行时文件。
