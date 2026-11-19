# A1–A3 执行记录（2026-10-20，2026-10-21 补充）

> 最新状态见本文最后的 2026-10-21 部署节：已验证修复现已实装并同步 hook 缓存。此前未部署记录为历史；没有将本次部署计为其余架构问题全部修复。

对应 `docs/REVIEW-2026-10-20.md` 第七节的 A1、A3、A2。按第八节顺序先做 A1，再 A2/A3。
本轮没有替换活动安装的 EXE、没有重启助手、没有改动 Codex 本体自启、没有创建或删除真实的停用标记。
新增本文档；其余改动为源码与脚本。

## 结论

| 项 | 内容 | 状态 |
|---|---|---|
| A1 | 测试与真实运行数据隔离（R01） | 完成，已用隔离安装与快照验证 |
| A3 | 统一媒体预检（R02） | 完成，三个入口共用同一函数 |
| A2 | 安装操作有界、失败可还原（R18、R01 回滚部分） | 完成，含超时、互斥、阶段码、停用标记还原 |

未执行：B1/B2/C1/C2/D1/D2；未部署；未做真实双窗口、声音画面、重新登录/重启验收。

## A1：测试与运行数据隔离

**新增 `src/RuntimePaths.cs`**：唯一的运行数据根入口。

- `Root` 默认仍为 `%USERPROFILE%\.codex-boot-animation`。
- `Bind()` 只接受绝对目录，拒绝盘根、拒绝真实根目录、拒绝真实根目录的子目录或父目录。
- 绑定发生在 `Main` 的第一条语句（`RuntimePaths.Apply`），早于任何状态/日志类型初始化；
  绑定失败直接返回退出码 46 并把原因写到 stderr，不发生任何写入。
- 所有 `*-test` 自测模式在未绑定隔离根时一律返回 46（`--button-probe-hang-test` 是故障注入夹具，除外）。
- 绑定后写入 `CODEX_BOOT_ANIMATION_TEST_ROOT`，子进程（`--completion-delivery-peer-test`、
  `--reaction-trace-peer-test`、`--button-probe-hang-test`）自动继承，不需要重复传参。

**去掉静态初始化捕获根目录的写法**：`IntroLog.Data`、`ReactionTrace.LogPath`、
`SceneSettings.Store`、`CompletionLayoutSettings.Store`、`RuntimeData.Root` 改为按需读取
`RuntimePaths.Root`，避免类型首次使用时才取值造成顺序问题。

**`CompletionJournal.SelfTest(runtimeRoot)`**：日志夹具和 `CompletionStateStore.TestFolder`
一起注入，测试结束恢复原值；不再依赖调用方是否绑定。

**`scripts/Install-Startup.ps1`**：新增 `-RuntimeRoot` 与 `-RequireIsolatedRuntimeRoot`。
安装脚本显式接收运行数据根；测试必须传临时根，传真实根在第一次写入前就拒绝。

**`scripts/Test.ps1`**：

- 每次运行创建独立的 `codex-boot-tests-*` 临时根，所有 C# 用例以 `--test-root <临时根>` 启动；
  hook 往返测试同样带 `--test-root`，状态写在临时根。
- 新增隔离守卫用例：漏传 `--test-root` 必须退出 46；把真实根当测试根必须退出 46。
- 运行前后对真实状态做只读快照并比对：`disabled`、`paused`、`scene-settings.json`、
  `completion-layout.json`、Run 项 `CodexBootAnimation`。任一变化即失败。
- 真实媒体用例移出默认逻辑套件，改由 `scripts/Test-Media.ps1` 显式运行
  （`--intro-media-recovery-test`、`--intro-media-decoder-probe`、`--resident-scene-test`、
  隔离空库 resident）；`Test.ps1 -IncludeMedia` 串联，CI 与打包各有一处显式调用。
- `scripts/Test-Installed.ps1`、`scripts/Test-EmptyResident.ps1` 同样只写临时根。

## A3：统一媒体预检

**新增 `scripts/Media-Preflight.ps1`**：`Get-MediaPreflight` / `Assert-MediaPreflight`。

- 目录清单来自 `videos/media-catalog.json`；拒绝绝对路径、`..`、盘符和扩展名不符的条目。
- **视频必需，首帧封面可选**：缺视频是致命错误，错误信息写明 `Required release media is missing (video): <相对路径>`；
  缺封面只警告 `Optional first-frame cover is absent (black transition)`，与现有缺封面运行路径一致。
- `Install.ps1`、`Test.ps1`、`Package-Release.ps1` 调用同一函数，结论不可能再分叉。

**新增 `scripts/Test-MediaPreflight.ps1`**：视频/缺视频 × 封面/缺封面四种组合、
中文与空格路径、只有封面但缺视频、非法清单路径，逐项断言；全部通过。

**边界**：解压后发行目录的复检需要干净提交才能打包（`Package-Release.ps1` 拒绝脏树），
因此本轮只验证仓库路径与中文/空格路径；解压包一致性属于 D2。

## A2：安装操作有界、失败可还原

**`scripts/Install-Support.ps1`**：

- `New-InstallBudget` / `Get-InstallRemainingMs` / `Get-InstallRemainingSeconds`：一个绝对预算，
  每一步只取剩余额度。
- `Invoke-BoundedCompanion`：用可限时的 `WaitForExit` 取代无界 `-Wait`；超时只终止本次启动的子进程，
  句柄在 `finally` 释放。
- `Wait-CodexCompanion`：lifetime 检查与 pipe 探测都受同一预算约束，循环睡眠取剩余时间；超预算返回 false，
  不会在检查之后再判断时限。

**`scripts/Install-Startup.ps1`**：

- 事务开始时保存 `disabled` 原始字节；正常安装清标记，任一失败路径按原字节还原；
  若期间标记被用户重建，则保留用户的新文件并告警，不覆盖新操作。
- 单安装互斥 `Local\CodexBootAnimationInstallV1`，拿不到锁直接失败。
- 阶段码：`preflight` / `registry` / `pause-flag` / `adopt` / `launch` / `readiness`；
  失败信息形如 `Install failed at stage 'readiness': ...`。
- `-InstallBudgetSeconds`（默认 60）覆盖 adopt、launch、readiness；adopt 超时也算失败。
- 回滚仍只删除本次新增的 Run 项与本次启动的 `--supervise/--watch/--resident` 进程。

## 验收证据

本机：Windows PowerShell 5.1（未安装 PowerShell 7；两套运行时由 CI 覆盖）。

| 命令 | 结果 |
|---|---|
| `scripts\Build.ps1` | 通过，`plugins\codex-boot-animation\hooks\BootPlayer.exe` 已重建 |
| `scripts\Test.ps1` | 全部通过，见 `build/logs/a1-a3-test-20261020.log` |
| `scripts\Test-Media.ps1` | 全部通过，见 `build/logs/a1-a3-media-20261020.log` |
| `scripts\Install.ps1 -CheckOnly` | 通过（此前因缺封面失败）；输出 15 个视频、4 个缺可选封面 |
| `scripts\Test-MediaPreflight.ps1` | 5 项通过（四组合 + 非法路径） |

安装测试新增用例（`scripts/Test-Install.ps1`，全部通过）：

- `installer-rejects-real-runtime-root`：真实根被拒绝，Run 值未写入。
- `installer-healthy-install-clears-pause-flag`：临时根中的标记被清除。
- `installer-pause-flag-transaction`：adopt、launch、readiness 三种失败后标记按原字节还原。
- `installer-hanging-probe-respects-budget`：探测挂起时 1 秒预算内返回。
- `installer-adopt-timeout-is-bounded`：adopt 永不返回时预算内失败、只杀本次子进程、标记还原。
- `real-profile-state-unchanged`：真实 `disabled`、`paused`、两份个人设置和 Run 项字节不变。

真实运行状态复核（测试后）：

- `%USERPROFILE%\.codex-boot-animation\disabled` 不存在；`paused` 不存在。
- Run 项仍为 `"C:\Users\Administrator\plugins\codex-boot-animation\hooks\BootPlayer.exe" --supervise`。
- `scene-settings.json`、`completion-layout.json` 哈希与运行前一致。

## 产物

- `plugins\codex-boot-animation\hooks\BootPlayer.exe` 与 `build\BootPlayer.exe` SHA256 相同：
  `AE4B14BCD59257AE68BABD4CAF6EE37A762D9B1EECA513CB36540F944806B18C`
- 该二进制未部署到 `C:\Users\Administrator\plugins\codex-boot-animation`；版本元数据仍为
  `0.7.0-alpha.5`，一致性属于 D2。

## 已知不足（本轮回看，2026-10-20 13:10）

本节是对上表“完成”口径的校正：三条验收标准并未全部达成，下面是仍未闭环的部分。

### 已在本轮修掉

1. `Diagnose.ps1` 的 IPC 检查传 `-TimeoutSeconds 0`；预算改为绝对截止后 0 表示“已超时”，
   诊断会恒为 FAIL。改为 1 秒，`Diagnose.ps1` 实测“助手通信 … 正常”。
2. 预检的封面命名先用 `ChangeExtension(...).TrimEnd('.')`，运行时 `MediaLibrary.FirstFrame`
   用 `GetFileNameWithoutExtension`。已统一为运行时规则；因清单校验禁止任意 `..`，
   两者实际不可达分歧，属防御性修正。补“名字含 `..` 被拒”用例。

### 覆盖缺口（未验证）

- 只有 Windows PowerShell 5.1 实测；pwsh 7 未在本地跑（本机未安装，仅 CI 覆盖）。
- `scripts/Test-Installed.ps1` 已改但未执行（活动安装仍是旧 EXE，没有可指向的安装包）。
- 解压包路径的预检未执行（脏树使 `Package-Release.ps1` 拒绝打包）。
- 并发安装（互斥分支）、注册表写入失败、真实 adopt、真实 resident 假死：无测试。
- C# 侧只拦 `*-test` 后缀；`--prompt-hook`/`--stop-hook` 等写状态的模式仍靠 `Test.ps1` 自觉传
  `--test-root`。新增一个不以 `-test` 结尾的测试入口会绕过隔离。
- 真实状态快照只覆盖 `disabled`/`paused`/两份设置/Run 项；`turns`、`completion-deliveries`、
  `desktop-instances`、`settings-backups` 不在快照内（常驻进程正在写），故“测试往真实根写了
  turn/claim”这类越界不会被发现。
- 隔离判定是路径字符串比较，junction/symlink 指向真实根不会被识破（未取最终句柄路径）。
- 绑定用环境变量继承：合法但陈旧的临时根会被静默接受，没有所有权/水印校验。
- PS 5.1 读无 BOM 的 UTF-8 会按 ANSI 解码，中文字节对可吞掉收尾引号导致整脚本解析失败
  （本轮编辑中踩到两次）。目前只有 `Test.ps1` 内的 BOM 检查，没有 .gitattributes/编辑器护栏。

### 设计妥协

- 封面策略选了“可选”，是产品取舍；若发行包要求必有封面需另行加开关。
- 真实媒体用例移出默认套件后，CI 的 PowerShell 5.1 一条腿不再跑媒体（仅 pwsh 腿跑）。
- 预算只覆盖启动事务：`Resolve-CodexInstallerCli` 仍是 5 次 CLI 调用 × 15 s（每候选最多 75 s）
  无共享截止，`Install.ps1` 还会串行跑整个 `Test.ps1`；“安装总时长有界”仍未成立。
- `Invoke-BoundedCompanion` 只终止直接子进程，没有 Job Object；超时的 adopt 可能留下孙进程。
- 停用标记还原是 `Test-Path` → `WriteAllBytes`，存在 TOCTOU 窗口（未被锁保护）。
- 注册表回滚按“值等于本命令”判断，没有操作身份/代次。
- 阶段码只在异常文本里，没有独立退出码或结构化输出。
- readiness 秒数取 `Ceiling`，最坏可超预算约 1 秒；测试只断言上界，未精确校验。

### 流程与范围

- 未提交：三项改动叠在上一轮脏树上，无法按 A1/A3/A2 分离成独立提交，与“每项独立提交”不符。
- 版本与清单仍为 `0.7.0-alpha.5`，EXE 已变；`plugins` 下 EXE 未提交，`Package-Release.ps1`
  现在会因脏树直接失败 —— 发布链路仍被 D2 阻塞。
- 本轮只做了 20 项中的 3 项；P1 的 R03（resident 退出/假死恢复）仍未动，其日常影响高于 A2。
- `Test-MediaCheck.ps1` 每次运行残留 `%TEMP%\cba-media-check-*`（既有问题），本轮只记录未修。

## 变更文件

新增：`src/RuntimePaths.cs`、`scripts/Media-Preflight.ps1`、`scripts/Test-Media.ps1`、
`scripts/Test-MediaPreflight.ps1`、`docs/EXECUTION-2026-10-20-A1-A3.md`。

修改：`src/BootPlayer.cs`、`src/RuntimeData.cs`、`src/ReactionTrace.cs`、`src/SceneSettings.cs`、
`src/CompletionLayoutSettings.cs`、`src/CompletionJournal.cs`、`scripts/Install.ps1`、
`scripts/Install-Startup.ps1`、`scripts/Install-Support.ps1`、`scripts/Package-Release.ps1`、
`scripts/Test.ps1`、`scripts/Test-Install.ps1`、`scripts/Test-Installed.ps1`、
`scripts/Test-EmptyResident.ps1`、`.github/workflows/ci.yml`。

## 边界与回退

- 本轮未提交 Git：工作区仍包含上一轮未提交改动（REVIEW 已指出），无法把 A1–A3 单独隔离成干净提交；
  提交与来源绑定属于 D2。
- 未重新登录/重启、未做真实双窗口与声音画面验收；这些仍按第八节由人工验收确认。
- 回退：必须使用各轮修改前的配套文件备份，逐项核对差异；禁止对上述文件统一执行 `git checkout`，否则会丢失前轮尚未提交的工作。测试使用 `codex-boot-tests-*` /
  `codex-installed-tests-*` / `codex-boot-media-*` 临时目录，安装测试只写自建夹具目录。
- `Test-MediaCheck.ps1` 运行后仍打印一条 `Fixture retained: %TEMP%\cba-media-check-*`（既有行为，未改）。

## 2026-10-21：A1 路径隔离和子进程传递补修

### 授权、范围与基线

用户要求“按规则执行”。按单问题流程继续 A1；开始时已存在 A1–A3 实现及前轮修复，共 33 个已修改/未跟踪路径。没有将这些既有改动归为本次成果，也没有覆盖或重置它们。

本次修复的具体问题：C# Bind 先执行 GetFullPath，导致相对路径被当作绝对路径接受；PowerShell 隔离只比较真实根本身，没有拒绝其父子目录；两端都没有检查目录联接及已有子路径；部分测试助手入口未要求隔离根。

### 改动

- 新增 `src/IsolatedRuntimeRoot.cs`：C# 与安装测试共用同一校验实现。仅允许绝对本地盘路径，拒绝盘根、真实运行根的父子目录、尾点等歧义组件；展开已有 8.3 短名后再次比较；拒绝路径祖先和既有子树中的重解析点，校验总条目预算 4096。任何不确定的访问失败均拒绝，校验后才创建目录。
- `src/RuntimePaths.cs`：Bind 使用公共校验；`--intro-media-decoder-probe`、`--repair-log-child`、`--lifetime-parent/probe`、`--test-fail/hang`、`--button-probe-hang-test` 均要求隔离根。
- `scripts/Install-Support.ps1`：新增 Resolve-IsolatedRuntimeRoot；仅测试模式编译并调用同一校验源码。正常安装不新增源码依赖。
- `scripts/Install-Startup.ps1`：RequireIsolatedRuntimeRoot 模式在路径规范化、目录创建和注册表操作之前校验原始输入。
- 新增 `scripts/Test-RuntimeIsolation.ps1`，并由 Test.ps1 调用；Test-Install.ps1 补安装入口父子目录和相对路径拒绝测试。联接仅指向自建夹具，清理先删除联接本身，再核验绝对夹具路径后删除夹具。
- `src/CompanionLifetimeTests.cs`：WMI 子进程显式带 `--test-root`；测试同时断言普通子进程与 WMI 子进程使用相同隔离根。

### 实际验证与一次发现后修复

第一次默认测试在 lifetime 用例返回 17：新的入口守卫发现 WMI 启动不继承调用者环境变量。没有放宽守卫；仅修测试夹具的参数传递。定向复测确认正常子进程随测试 Job 退出，独立子进程存活，二者均在同一隔离根。然后重新执行完整默认测试。

| 验证 | 结果 |
|---|---|
| 本地 Build.ps1 | 通过；保留既有 NewChatDiscovery.Result.Gate 未赋值编译警告 |
| 路径隔离回归 | 12 组 PASS：含无绑定助手、真实根/父/子、盘根、相对/盘符相对、尾点、联接根/祖先/子树、合法写入与继承 |
| PowerShell 7 默认 Test.ps1 | 64 条 PASS，退出 0 |
| Windows PowerShell 5.1 默认 Test.ps1 | 64 条 PASS，退出 0 |
| 保护快照 | disabled、paused、scene-settings、completion-layout、助手 Run、Codex 启动状态、实装 EXE 均与执行前一致 |
| Codex 本体自启动 | 读回 State=1，仍禁用 |

当前仓库包及 build EXE SHA256 均为 `9C2D7C47393E0B126BA20D6A9D861EDD1C662A8E713ED3496708C077ED7FAA34`；属于未部署的测试构建，不能沿用上方旧产物哈希。

更正前文“本机未安装 PowerShell 7”：本次使用了 Codex 随附的 pwsh，已在本机实测两套运行时。默认套件内部仍有 WPF 控件和媒体缓存构造；不能把脚本打印的“All non-UI tests passed”理解为完全没有媒体初始化。本次没有运行 Test-Media.ps1 或宣称真实画面、声音、新聊天点击验收通过。

### 备份、回退与未完成项

本次逐文件修改前备份及测试日志：`build/a1-isolation-3711aef9fb3d4227baddc619ea8a7421/`，其中包含 before/after 保护快照、最初失败日志、两套最终成功日志和本轮增量补丁。备份根在仓库内，未覆盖活动安装或缓存。

回退本次时，只将该备份内与本轮修改对应的旧文件恢复；新增 IsolatedRuntimeRoot.cs 和 Test-RuntimeIsolation.ps1 需先核对没有后续改动再移除。本轮没有更改个人视频、实装/缓存 EXE、正式播放器启动逻辑或 Windows 设置。不得通过恢复整个仓库/用户运行目录回退。

未 Git 提交：本次 RuntimePaths 和若干脚本依赖开始时已存在的未提交 A1–A3 实现；把它们直接提交会混入旧工作，只提交新文件则不能形成可独立构建的提交。已保存本轮相对修改前备份的增量补丁，后续整理前轮基线后按单问题提交，不能宣称提交门槛已完成。

本次不是整个 A1–D2 计划完成。仍待处理：测试根会话归属/陈旧继承值、完整副作用边界、默认媒体测试分类，以及 A2/A3 旧记录中的剩余缺口。路径校验防止误配置，不宣称可抵御同一用户在校验后并发替换目录或预置硬链接的对抗行为。B1/B2/C1/C2/D1/D2 未实施。

## 2026-10-21：已验证修复完成本机部署

### 用户授权与部署范围

用户在“为什么不部署”后明确授权“你看着去完成就行了，我相信你”。据此完成本机备份、更新和验证，不再把混合工作区作为阻止部署的理由。没有将多轮改动混入一个 Git 提交；采用包含实际工作区的冻结源码快照、逐文件 SHA256、明确的构建 ID 和配套旧文件备份提供本次本地交付来源。

构建 ID：`deploy-20261021-bcbc0c1f90c6420ab995b9215e49d044`。

- 冻结源快照 180 个文件，含编译源码、脚本、资源及已验证 EXE；`includesWorkingTreeChanges=true`，不是干净 Git 提交发布。
- EXE SHA256：`9C2D7C47393E0B126BA20D6A9D861EDD1C662A8E713ED3496708C077ED7FAA34`。
- 本次更新 61 个文件：实装 EXE、配套安装/测试/卸载脚本、源码，hook 缓存 EXE，以及两处 deployment.json 回执。
- `scripts/Update-Companion.ps1` 的 adopt 操作改用现有有界等待函数，10 秒到期失败；这是更新流程实际需要的最小补修，不代表整个安装链路的 A2 剩余问题全部解决。
- 只停止并重启实装路径下的动画助手。Codex 本体未重启；个人视频、设置、启动项、hooks.json 和宿主 manifest 未修改。

### 版本和来源

源 manifest 是 `0.7.0-alpha.5`，宿主已注册目录标签仍为 `0.6.4+codex.20260930053228`。本次为已注册安装上的本地修复部署，没有改写宿主目录标签来伪装版本升级，也没有修改 hook 信任。`deployment.json` 明确分别记录 SourceManifestVersion、HostCatalogVersion、BuildId、EXE 哈希与源码快照位置。正式版本统一/发布流程仍属于 D2，不能宣称这次已经完成。

### 验证结果

| 检查 | 结果 |
|---|---|
| 同一 EXE 的 PS7 / PS5.1 默认套件 | 前一阶段各 64 条 PASS |
| 冻结快照更新器部署前默认套件 | 再次通过，日志 update.log |
| 冻结快照显式媒体套件 | 4 项 PASS，含空库夹具 |
| 实装 tests/run-tests.ps1 | 7/7 PASS，实际从安装目录执行 |
| 实装媒体恢复/解码/场景测试 | 3/3 PASS，使用实装媒体目录 |
| 仓库、build、实装、hook 缓存 EXE | 相同 SHA256 |
| 缓存 EXE --hook | 返回 0，找到实装监督进程 |
| 真实 readiness / 无播放 IPC 检查 | 通过，三个角色各一份，supervisor 不受宿主 Job 绑定 |
| 61 个交付文件 | 全部哈希一致 |
| 67 项保护数据 | 前后相同，含两处视频/清单/hook、个人设置、助手 Run 和 Codex Startup State |

实际运行日志确认：watcher `raw-mouse-input-ready`、两个日志监听根与三个快速补读文件就绪、completion journal 就绪；resident 已加载实装媒体，30 秒空闲后 `resident-idle-media-released count=3`。这些日志证明相应模块启动和缓存释放，不证明真实点击命中率、首画面主观流畅度或内存优化目标已经达成。

部署后角色进程：supervisor 6768（--outside-host）、watch 39284、resident 16324。PID 只作为当次回执，后续恢复可变化。Codex 本体启动仍 `State=1`，保持用户禁用。

### 备份与回退

持久备份目录：`C:\Users\Administrator\.codex-boot-animation\backups\deploy-20261021-bcbc0c1f90c6420ab995b9215e49d044`。

包含旧文件、deployment-files.json、源码/产物清单、测试日志、前后保护快照、进程回执、source.zip 和 Rollback.ps1。源码 ZIP SHA256：`02BDA65A6E955E68FEE4E46C23E5313C0D8AD044B1F5C1C6A245DFF04FC3F4CD`。

Rollback.ps1 先拒绝覆盖部署后出现的新文件修改，再只恢复清单列出的文件、重启动画助手，并保留用户视频/设置/启动状态。脚本语法与全部旧文件完整性已检查；没有为演示回退而再打断已正常运行的助手。回退脚本从普通 Windows PowerShell 执行，不在测试绑定环境运行。

### 当前交付边界

本次已完成“把已验证修复部署到实际插件”的交付。真实新聊天点击、多窗口交替、声音/跳过和长期稳定性仍需在正常使用中验收；未将隔离媒体测试替代人工体验结论。剩余 B1/B2/C1/C2/D1/D2 问题继续按审查报告逐项处理，不声称全部问题已解决。没有公开上传、发布或合并 Git 提交。
