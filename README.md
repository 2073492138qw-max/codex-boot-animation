# 🎬 Codex Boot Animation — Windows 版 Codex 桌面互动陪伴助手

[![Platform](https://img.shields.com/badge/Platform-Windows%2010%20%2F%2011-0078D6?logo=windows)](https://github.com/2073492138qw-max/codex-boot-animation/releases/latest)
[![Release](https://img.shields.com/badge/Release-v0.8.0--beta.1-2ea44f)](https://github.com/2073492138qw-max/codex-boot-animation/releases/latest)
[![Architecture](https://img.shields.com/badge/Hook--Free-免配置即插即用-ff69b4)](START-HERE.md)
[![Memory](https://img.shields.com/badge/Idle%20RAM-~13%20MB-blueviolet)](#5-低内存与自愈稳定性设计)
[![License](https://img.shields.com/badge/Code-MIT-blue)](LICENSE)
[![Media License](https://img.shields.com/badge/Media-CC%20BY%204.0-orange)](plugins/codex-boot-animation/videos/LICENSE.md)

**免安装插件、免配置 Hook、解压双击即用**的 Windows 版 **Codex Desktop** 桌面动画伴侣。
通过原生 WPF 图层在 **冷启动、新建对话、高敏感中文怒气/吐槽回应、智能任务完成庆祝（含后台角落召回）、拟人化闲置桌宠陪伴** 五大场景下触发可跳过的互动视频。**不修改 Codex 本体、不依赖实验性插件钩子、绝不记录或上传任何聊天正文。**

> 📥 **新手直接下载使用**：前往 **[Releases 页面](https://github.com/2073492138qw-max/codex-boot-animation/releases/latest)** 下载最新的 `codex-boot-animation-0.8.0-beta.1-windows.zip`，解压后双击 `install.cmd` 即可！图文教程见 **[30 秒新手上手指南 (START-HERE.md)](START-HERE.md)**。

---

## ✨ 为什么选择 `v0.8.0`？（核心优势与升级亮点）

| 痛点 / 场景 | 旧版 (`v0.7.x` Alpha) | **全新 `v0.8.0` 免 Hook 独立版** |
| --- | --- | --- |
| **部署与兼容** | 依赖 Codex CLI `plugin_hooks` 实验开关，需手动 `/hooks` 审核信任；Codex 升级移除 Hook 后易失效 | **100% 免 Hook 独立引擎**：直接增量监听本地会话与导航日志，**双击 `install.cmd` 30 秒即用**，无惧 Codex 更新 |
| **冷启动画面** | 首播前偶发 ~150ms 旧帧残影或二次 Seek 卡顿 | **`IntroCoverGate` 首帧门控**：未就绪前隐藏加载，确认回零并推进首帧后再撤除 `-first.png` 封面，**零闪帧零卡顿** |
| **后台内存占用** | 常驻预加载 4 个媒体解码器，空闲占用 **~165 MB** | **按需懒加载 + 90 秒空闲自动回收**：平时常驻物理内存仅 **~13.6 MB**（**直降 92%**） |
| **生气/吐槽触发** | 仅匹配少量重度脏话，日常吐槽/阴阳怪气难以触发 | **`AngerLexicon` 超高敏中文词库**：覆盖 **291 类**国人日常直接开怼、嫌弃、反讽阴阳怪气与死循环暴躁表达，105 类技术讨论 **0 误触** |
| **任务完成提醒** | 每次普通回复结束都容易弹窗，频繁打扰思路 | **`CompletionGate` 真人感偏克制门控**：重活（工具 ≥3 次或耗时 ≥45s）才庆祝；切到后台干活时（工具 ≥1 次或 ≥15s）右下角静默召回；普通闲聊与中途反问自动静默 |
| **闲置桌宠陪伴** | 需严格离开 15 分钟且回来静止 2 秒，极难遇到 | **`IdleReturnPolicy` 三段式拟人陪伴**：前台发呆 **5 分钟**主动卖萌、切去别处冷落 **10 分钟**切回迎接、离座 **5 分钟**归来停顿 **0.8 秒**即欢迎，带 **1.2 秒**开场防手抖保护 |
| **素材完整度** | 11 个示例视频，部分缺少首帧封面图 | **15 套 CC BY 4.0 高清视频 + 15 张完整 `-first.png` 首帧封面**（通过 `media-preflight` 100% 校验） |

---

## 🚀 1. 30 秒极速上手

1. 在 **[Releases 页面](https://github.com/2073492138qw-max/codex-boot-animation/releases/latest)** 下载 `codex-boot-animation-0.8.0-beta.1-windows.zip`。
2. **完整解压**到一个固定文件夹（如 `D:\Tools\codex-boot-animation`，不要在压缩包预览窗口内直接双击）。
3. 根据你的喜好选择运行方式：
   - **一键安装 + 登录自启（推荐）**：双击 **`install.cmd`**（无需管理员权限，自动部署到 `%USERPROFILE%\plugins\codex-boot-animation` 并立即在后台启动守护；**绝不会**让 Codex 本体开机自启）。
   - **绿色免安装单次体验**：双击 **`start.cmd`**（仅本次运行，不写任何启动项）。
   - **停止并卸载自启**：双击 **`uninstall.cmd`**（干净停止后台进程并移除启动项，保留你的自定义视频和音量设置）。
4. 启动后，Windows 右下角系统托盘会出现 **“Codex 片头助手”** 图标，右键即可随时预览五大场景、打开视频文件夹或调节每个场景的音量与开关。

---

## 🎭 2. 五大互动触发场景详解

所有场景在播放时均可随时按 **`Esc`** 或点击右上角 **“跳过”** 立即关闭：

| 场景目录 | 触发时机与人性化策略 |
| --- | --- |
| **`videos/冷启动/`** | **打开 Codex 迎宾片头**：当新的 Codex Desktop 主程序启动时播放一次（1.2 倍速、默认 85% 音量，`IntroCoverGate` 首帧无缝衔接）。 |
| **`videos/新聊天/`** | **新建对话转场**：在 Codex 中点击“新聊天”切入空白会话时播放（1.2 倍速、默认 65% 音量，支持多窗口独立路由去重）。 |
| **`videos/生气回应/`** | **高敏感中文情绪安抚/回怼**：纯内存检测你刚发送的 `UserMessage`，支持直接开骂、日常嫌弃（“写的什么玩意/一团糟/笨死了”）、反讽阴阳怪气（“真有你的/大聪明/听君一席话”）、死循环抓狂（“又错了/到底行不行/说了多少遍”）等 **291 种表达**，立即在窗口内弹出互动回应。 |
| **`videos/任务完成/`** | **偏克制大活庆祝 + 后台角落召回**：<br>• **重活必贺**：单轮调用工具 `>= 3` 次或耗时 `>= 45 秒`，完成后自动播放庆祝视频；<br>• **后台召回**：你切去浏览器/微信/IDE 时，只要 Codex 完成了实操（调用工具 `>= 1` 次或耗时 `>= 15 秒`），在屏幕右下角弹出不抢焦点的完成提醒叫你回来；<br>• **克制防烦**：前台几秒钟的简短问答、执行报错、或 AI 中途反问等你拍板（如“需要我继续吗？”）绝不乱弹窗；同会话 60 秒冷却。 |
| **`videos/闲置互动/`** | **三段式桌宠陪伴**：<br>• **前台发呆陪伴**：Codex 在前台连续 **5 分钟**无键鼠操作，主动出来陪伴；<br>• **后台冷落回归**：切到其他软件忙满 **10 分钟**没看 Codex，切回 Codex 窗口稳定 **0.8 秒**后立即迎接；<br>• **离座归来欢迎**：整机离开/锁屏满 **5 分钟**，回到电脑前切回 Codex 停顿 **0.8 秒**即触发欢迎；<br>• **防误触设计**：开播前 **1.2 秒**保护期防止刚摸鼠标手抖误关，1.2 秒后任意键鼠操作自动收起；触发后冷却 15 分钟。 |

---

## 🎨 3. 自定义你自己的视频与声音

想换成你喜欢的动漫角色、爱豆或搞笑片段？完全零门槛：

1. 右键右下角托盘图标 **“Codex 片头助手”** → 点击 **“打开场景视频文件夹”**。
2. 打开任意场景子目录（`冷启动` / `新聊天` / `生气回应` / `任务完成` / `闲置互动`）：
   - 放入 **1 个 `.mp4`**：每次固定播放该视频；
   - 放入 **多个 `.mp4`**：每次随机轮播（生气回应采用不连重洗牌轮播）；
   - **清空文件夹**：仅关闭该场景，不影响其他场景。
3. 文件放入后约 **1.5 秒自动热加载生效**，无需重启助手或 Codex！
4. 右键托盘还可随时打开：
   - **场景设置（音量 / 静音 / 开关）**：独立调节 5 个场景的音量（0–100%）与自动触发开关；
   - **完成提醒设置（位置 / 大小）**：自由切换后台角落提醒的尺寸（大/中/小）和位置（右下角/左下角）。

---

## 🪶 4. 低内存与自愈稳定性设计

- **按需懒加载 + 90 秒空闲自回收**：`--resident` 常驻进程启动时不预创建重型媒体解码器，空闲物理内存（Working Set）仅 **~13.6 MB**；播放结束空闲 90 秒后自动优雅退出释放 Windows Media Foundation 非托管显存/内存，并由 `--watch` 守护无缝拉起新实例。
- **指数退避守护 (`RestartBackoff`)**：`--supervise` 与 `--watch` 采用 `2s -> 4s -> 8s -> 15s -> 30s` 指数退避重启保护，防止异常死循环占满 CPU。
- **自动垃圾清理 (`RuntimeCleanup`)**：自动清理超过 7~14 天的历史会话状态文件与多余配置备份，长期运行不留磁盘垃圾。
- **绝对隐私安全**：所有怒气与完成判定均在内存中即时完成，`%USERPROFILE%\.codex-boot-animation\playback.log`（2 MB 循环日志）仅记录时间戳、进程 PID 与触发原因，**绝不保存或联网发送任何聊天内容**。

---

## 🛠️ 5. 目录结构与从源码构建

要求：Windows 10/11、系统自带的 .NET Framework 4.8+ 与 Windows PowerShell 5.1（**无需安装 Visual Studio 或任何第三方依赖**）。

```text
install.cmd                            一键独立安装 + Windows 登录自启
start.cmd                              绿色免安装单次启动（不改注册表）
uninstall.cmd                          一键停止助手并移除自启
check.cmd                              只读运行健康诊断
check-videos.cmd                       视频与首帧封面完整性检查
plugins/codex-boot-animation/
  hooks/BootPlayer.exe                 预编译 .NET 4.8 WPF/WinForms 核心程序
  videos/                              5 大场景文件夹（内置 15 套 CC BY 4.0 视频与首帧图）
src/                                   C# 核心源码与单元测试（零外部 NuGet 依赖）
scripts/                               编译、测试、独立安装、热更新与发版打包脚本
docs/                                  架构设计、安装、日志与发版文档
```

```powershell
# 1. 从源码零警告编译 BootPlayer.exe
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1

# 2. 运行全部 70+ 项自动化逻辑测试 + 真实媒体解码测试
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1 -IncludeMedia

# 3. 打包生成 dist/ 下的发布版 ZIP 与 SHA256 校验文件
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-Release.ps1
```

---

## 🌐 English Overview

**Codex Boot Animation (`v0.8.0-beta.1`)** is a lightweight, **100% hook-free** Windows desktop companion for **Codex Desktop**.
- **Zero Hook Dependency**: Directly monitors local Codex session/navigation journals in real time—works out of the box on modern Codex builds where `plugin_hooks` was removed.
- **5 Interactive Scenes**: Cold Startup (`IntroCoverGate` zero-stutter cover gate), New Chat, High-Sensitivity Anger/Roast Reaction (`AngerLexicon` with 291 patterns & 0 false positives), Restrained Task Completion (`CompletionGate` with non-focus-stealing background corner recall), and 3-Mode Human-Centered Idle Companionship (`IdleReturnPolicy`).
- **Ultra-Low Memory**: ~13.6 MB idle working set (-92% vs v0.7) with lazy media slot loading, 90s post-playback idle recycling, exponential restart backoff, and automatic runtime state cleanup.
- **30-Second Setup**: Download the latest ZIP from **[Releases](https://github.com/2073492138qw-max/codex-boot-animation/releases/latest)**, extract, and double-click **`install.cmd`** (or **`start.cmd`** for portable mode).

---

## 📄 开源协议 (License)

- **源码与文档**：[MIT License](LICENSE)
- **内置 15 套视频与首帧封面**（详见 [media-catalog.json](plugins/codex-boot-animation/videos/media-catalog.json)）：[CC BY 4.0](plugins/codex-boot-animation/videos/LICENSE.md)（署名：**Codex Boot Animation contributors**）
- **托盘图标素材**：[CC BY 4.0](assets/README.md)
- 本项目为独立开源社区作品，非 OpenAI 官方出品。如果这个小助手让你的日常写代码变得更有趣，欢迎点亮右上角的 **⭐ Star** 支持！
