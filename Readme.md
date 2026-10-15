About iSCSI Console:
====================
iSCSI Console is a Free, Open Source, User-Mode iSCSI Target Server written in C#.
iSCSI Console is cross-platform, portable and requires no installation.
iSCSI Console can serve physical and virtual disks to multiple clients.

This fork / 本分支更新
=====================

1.5.9：BSCX 无盘与便携服务版
--------------------------

本次提供两个包，命令行和管理管道协议保持一致：

| 下载包 | 使用环境 |
| --- | --- |
| `iSCSIConsole-net472.zip` | 原图形界面和命令行；需要 .NET Framework 4.7.2，适合既有 Windows 环境。 |
| `iSCSIConsole-headless-win-x64.zip` | 无界面 x64 单文件，自带 .NET 10 运行时，无需安装 .NET，也不依赖 WinForms；适合受支持的 Windows 10/11 和对应 Server。 |

无界面包解压后的 `ISCSIConsole.exe` 可以用于 BSCX 的 `share` 目录，继续使用原来的 `/start`、`/addtarget`、`/removetarget /force`、`/list`、`/save`、`/stop` 等参数。直接双击只显示帮助，不打开图形窗口。BSCX 的调用代码无需改动。

这里的免安装版是 **self-contained**：运行时随 EXE 携带，并非完全不含 .NET 的 NativeAOT 程序；文件明显大于 Framework 版，首次运行会解包运行时原生组件到临时目录（需要可写空间）。可用 `DOTNET_BUNDLE_EXTRACT_BASE_DIR` 指定解包目录。不需要给 PE 安装 .NET 可选组件，但不能承诺所有精简 PE 都可运行：仍需 x64 系统 API、网络、可读写文件系统等。尤其旧版、删减组件的 PE 要实际验证；Windows 7/8.1、32 位 PE 不适用此包。

本次有明确依据的稳定性与性能改进：

- 缓存命中时复用 LRU 节点，减少频繁小块读取时的对象分配；合并读缓存先完整入账再淘汰。
- 未启用日志时跳过日志字符串格式化；修复关闭日志与写日志并发时的锁对象竞争。
- VHD/VHDX 打开或解析失败立即释放已打开的文件句柄，避免失败重试后无法重新打开、移动或删除镜像。
- 镜像逻辑数据读取不足时报 I/O 错误，避免把异常短读补零后当作成功数据返回。合法稀疏零块仍由 DiscUtils 正常处理。
- 保留 FUA、SYNCHRONIZE CACHE 的真实落盘语义及写入缓存失效，不以延迟落盘换取表面速度。

性能边界：仍是用户态 iSCSI + DiscUtils 文件系统镜像实现，一目标内串行执行 SCSI 命令以保持写入顺序，目标之间可并行。共同父本依靠 Windows 文件缓存复用，`/cachemb` 是每个目标的缓存上限，不能按整个机房只计算一次。网络带宽、服务器存储随机 I/O、差分链深度和客户机同时启动数，通常比运行时本身更决定性能。没有实测依据不承诺倍数提升；也不会修复客户机缺驱动、iBFT 接管失败等 Windows 启动问题。

仓库 CI 同时检查两个运行时下的缓存、FUA/落盘、差分子本写入、80 目标释放和管理管道，并对实际发布的无界面 EXE 验证启动、目标增删、TCP iSCSI 读写和停止。机房并发启动及具体 PE 兼容性需要实机测试。

本分支主要更新：

1. 中文界面汉化，便于中文 Windows 环境直接使用。
2. 增加 VHDX 支持，可创建、打开并作为 iSCSI LUN 提供给客户端。
3. 修复 VHDX 写入时 `DiscUtils.Vhdx.ContentStream.Flush()` 未实现导致的写入异常。
4. 打开被占用的 VHDX 时支持只读回退，避免直接崩溃。
5. 新增命令行启动模式，可不打开 GUI，直接用一个 VHD/VHDX 文件启动 iSCSI Target。
6. 命令行启动时会尝试自动添加 Windows 防火墙 TCP 3260 入站规则，减少首次启动时的手动干预。
7. GUI 可保存当前服务配置，命令行可按配置文件启动或停止服务。
8. 后台服务支持运行中添加、移除、查看和保存 VHD/VHDX Target，无需重启服务。
9. 命令行、后台服务和 GUI 为 VHD/VHDX 磁盘镜像提供只读块缓存；单磁盘模式默认 256MB，动态服务目标默认 16MB，写入会自动清理相关缓存块，可用 `/cachemb 0` 关闭。
10. 修正 READ(6) / WRITE(6) 长度为 0 时应表示 256 个块的兼容性问题。
11. 增强虚拟磁盘目标的 SCSI 兼容性，补充 READ/WRITE/VERIFY(12)、SYNCHRONIZE CACHE(16) 等常见命令处理。
12. 改进 `/stop`，停止请求会立即关闭服务；若服务进程短时间内未退出，会按状态文件中的进程号终止。
13. 修复写缓存语义：SYNCHRONIZE CACHE(10/16) 和 FUA 写入会将 VHD/VHDX 数据真正刷新到物理存储。
14. 增加 `/singleclient`，按首个发起端来源 IP 锁定可写 Target，允许同一客户机在 iPXE 到 Windows 交接期间重连，并拒绝其它机器。
15. 动态目标可用 `/initiator <iqn>` 绑定唯一发起端 IQN，适合一台客户机对应一个差分 VHDX 的无盘机房。
16. 修复动态移除目标后 SCSI 工作线程不退出的问题；现在会先停止并排空该目标命令队列，再释放 VHD/VHDX 句柄。
17. 管理管道支持并发请求，并为动态移除增加显式 `/force`，便于集中启动或回收 50-80 个独立客户机目标。

Command line target mode:
=========================
Use a VHD or VHDX path as the first argument to serve that image as one iSCSI target:

```bat
iSCSIConsole.exe D:\VHDSYS\example.vhdx
```

The default target IQN suffix is the disk image file name without extension. The command above uses:

```text
iqn.1991-05.com.microsoft:example
```

You can set the IQN suffix with the second argument:

```bat
iSCSIConsole.exe D:\VHDSYS\example.vhdx pc01
```

This uses:

```text
iqn.1991-05.com.microsoft:pc01
```

You can also pass a full IQN as the second argument:

```bat
iSCSIConsole.exe D:\VHDSYS\example.vhdx iqn.2026-07.local.lab:pc01
```

Optional arguments:

- `/listen <ip>`: listen address. Use `0.0.0.0` for all interfaces. Default is all interfaces.
- `/port <port>`: TCP port. Default is `3260`.
- `/readonly`: open the disk image read-only.
- `/singleclient`: lock this target to the first initiator source IP until the target process stops. This is recommended for a writable boot/update image.
- `/cachemb <MB>`: read cache size for VHD / VHDX disk images. Default is `256`; use `0` to disable.
- `/status <path>`: write `READY ...` or `ERROR ...` status text for scripts.
- `/stopfile <path>`: exit when this file appears.

When `/log <path>` is enabled, cache statistics are written when a cached disk is released.

A writable VHD/VHDX must not be mounted by several Windows clients at the same time. Use one differencing disk per client for concurrent diskless boot, or use `/singleclient` when only one machine may boot the selected writable image for maintenance and update. `/singleclient` only limits login ownership; when the selected image is a differencing VHDX, writes remain in that child and its parent chain is opened read-only.

Saved service configuration:
============================
The GUI can save the current target list to `iSCSIConsole.service.xml` in the program directory.
The saved configuration includes the listen address, TCP port, target IQNs and supported disks.
For VHD / VHDX disk images, the GUI can also save the read cache size.

Supported saved disk types:

- VHD / VHDX disk image path.
- Physical disk index.
- Windows volume GUID.

Start the saved service configuration without opening the GUI:

```bat
iSCSIConsole.exe /start
```

Use a custom configuration file:

```bat
iSCSIConsole.exe /start /config D:\iSCSI\targets.xml
```

Stop a service started from the same configuration:

```bat
iSCSIConsole.exe /stop
iSCSIConsole.exe /stop /config D:\iSCSI\targets.xml
```

`/stop` can also stop a single-disk service started by passing a VHD / VHDX path directly.

Add a VHD / VHDX target while the saved service is already running:

```bat
iSCSIConsole.exe /addtarget D:\iSCSI\001122AABBCC.vhdx pc-001122aabbcc
```

For a classroom target, bind the child disk to the initiator IQN assigned to that client's MAC address:

```bat
iSCSIConsole.exe /addtarget D:\iSCSI\children\001122AABBCC.vhdx pc-001122aabbcc /initiator iqn.2026-08.cn.bscx:mac-001122aabbcc
```

The iPXE script for that client must set the same `initiator-iqn` before `sanboot`. A future controller can create/delete the differencing VHDX itself, then use `/addtarget` and `/removetarget` only to control the corresponding IQN service.

Use `/cachemb <MB>` with command line target mode or `/addtarget` to change the read cache size for that disk image. `/addtarget` defaults to 32MB so that 80 targets have at most about 2.5GB of user-space read cache; Windows can still share cached pages from the common read-only parent file. Sequential reads coalesce adjacent cache misses into up to 512KB reads, reducing repeated differencing VHDX metadata lookups. Saved service configuration stores this value as `CacheSizeMB` on each disk image entry.

`/singleclient` is intended for the current single-machine maintenance workflow. It is not needed for classroom targets when every client has a unique child target protected by `/initiator`.

Remove, list, or save runtime targets:

```bat
iSCSIConsole.exe /removetarget pc-001122aabbcc
iSCSIConsole.exe /list
iSCSIConsole.exe /save
```

Normal removal refuses a target that still has an active or retained iSCSI session. A controller that has intentionally stopped the client can use `/removetarget <target> /force`; this disconnects the remaining session, drains queued SCSI commands, flushes the child image and releases its file handles. Dynamic classroom targets will normally also use `/nosave` so stale per-client targets are not restored after a service restart.

About the iSCSI library:
========================
The iSCSI library utilized by iSCSI Console was designed to give developers an easy way to serve block storage via iSCSI.
Any storage object you wish to share needs to implement the abstract Disk class, and the library will take care of the rest.
The library was written with extensibility in mind, and was designed to fit multitude of projects.
iSCSI Console is merely a demo project that exposes some of the capabilities of this library.

A NuGet package of the library [is available](https://www.nuget.org/packages/ISCSI/).

Notes:
------
In addition to a full fledged iSCSI Target server implementation, the iSCSI library also contain a very basic iSCSI initiator implementation.

What this program can do:
===================================
1. Serve virtual disks (VHD / VHDX / VMDK / IMG).
2. Serve physical disks.
3. Serve basic volumes as disks.
4. Serve dynamic volumes as disks.
5. Create VHDs.
6. Can run under Windows PE using Mono.
7. Can run under Linux / OSX using Mono (use the release targeting .NET Framework 4.7.2)

![iSCSI Console UI](ISCSIConsole_UI.png)

Contact:
========
If you have any question, feel free to contact me.
Tal Aloni <tal.aloni.il@gmail.com>
