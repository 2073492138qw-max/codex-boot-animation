# 托盘预览故障 — 2026-10-03

## 诊断与授权

用户报告生气不播、完成很久才播、闲置不播。只读核对源码/实装/缓存一致，助手三进程
正常；并非视频没有下载或新旧 EXE 混用。UTC 2026-10-02 16:02–16:03 的实际日志
对应北京时间 2026-10-03 00:02–00:03：

- 生气菜单请求送达驻留，但以 not-codex-foreground 拒绝两次。
- 完成预览使用正式 --task-play，等待前台 15.3 秒后播放；程序最多等待 90 秒。
- 闲置一次请求时完成 pending 互斥体还存在，生产 gate 拒绝；该路径未记录理由。
  第二次实际窗口出现后约 70ms 因 user-active 关闭，未起视频音轨/动态帧。

共性是手动预览套用自动触发规则。之前 IPC 直接播放验收不覆盖托盘菜单，这是测试缺口。
先说明最小方案和顺序，用户“那你修复好啊”授权；按生气→完成→闲置逐项处理。
不改 Codex 本体/数据库/信任，不安装依赖，不改媒体/音量/速度，不重启 Codex、关机或发布。

## 第一项：生气预览前台准备

开始 Git 状态干净，基线 `c840bc5`。仅修手动菜单，不放宽自动生气前台保护。

- `src/PreviewFocus.cs`：菜单 Close 后一次 BeginInvoke，退出点击处理再准备前台。
  只选择经进程路径验证的已有 Codex 主窗口；多实例没有明确前台目标时不猜。
  单次 SetForegroundWindow 并核实；拒绝则返回原因，不循环强抢、不使用 AttachThreadInput。
  最小化使用 ShowWindowAsync 恢复；尚未恢复/未获前台则提示，不等待或声称验收通过。
- `src/BootPlayer.cs`：只有“预览生气回应”进入上述流程；拒绝记录并给托盘提示。
  自动 reaction-angry、另两种预览、旧播放窗口和媒体释放逻辑没有更改。
- `scripts/Test.ps1`：加入 --preview-focus-test；包内 EXE 按原 Build 生成。

按 agent-reach 网页流程读取微软说明；Jina 代理 20 秒超时后，只读直接访问微软页面。
依据 [SetForegroundWindow 官方规则](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)：
不能在菜单活动时切前台，且满足条件也可能被 Windows 拒绝，故不省略关闭/结果检查。
未修改系统前台限制，不通过依赖或输入模拟绕过它。

## 测试、部署、验收边界

新增测试以假的目标/激活委托验证：无目标不激活、拒绝不播放、成功恰好一次；
真实但未显示的 WinForms 菜单/消息队列验证延后执行、失败路径和异常报告。
模拟测试不操作用户焦点，也不代替实际菜单点击。

首轮完整测试在原 --resident-protocol-test 发生一次 IOException/退出 23，未部署。
独立重跑该项两次通过；然后 PowerShell 7 与 Windows PowerShell 5.1 各 24 项完整
检查通过，新增焦点回归额外重复 10 次通过。没有改原通信测试或生产协议。
该一次性失败原因尚未证明，保留记录，不说从未失败或擅自重构修它。

原 Update-Companion 备份替换实装，只重启助手并采用当前 Codex 实例；既有缓存
hooks 一致后备份并同步 EXE。27 个媒体文件哈希、Run、hooks、Codex PID/start-ticks
前后一致；三种助手与 IPC 就绪。源包/实装/缓存一致 SHA256：
`F9054641DCB4246E15DF7425BD01386E8665354FB8365A484423652A1A4AA7BF`。

用户从真实托盘点击生气预览后确认“测试了没有问题”。北京时间 00:38:22 的菜单
准备日志之后约 68ms 窗口出现，记录原速/音轨、动态帧及正常结束；实际菜单验收通过。
真实最小化/多实例/另一台电脑验收未做。本项独立提交 `665cdd8`，再动下一项。

## 第一项备份与回退

`%USERPROFILE%\.codex-boot-animation\backup`：

- 实装：`update-97ae8a3bc2d14db1a96a8367f7508d10/BootPlayer.exe`。
- 缓存：`anger-preview-cache-b1712f39c0744204a483f10ad2d09f13/BootPlayer.exe`。

两份都是本次之前已修启动/空库/片头中断的版本，SHA256：
`B6E693437E8CA748EED30C5F6B391FECF7FA581FAB08D7D1FEBF479A34C2582F`。
只停本安装助手、成对恢复 EXE、采用已有 Codex 实例再核对 IPC；不动媒体/Run/hooks，
不回退到启动事故前版本。备份不删除。

## 第一项结束时剩余两项（历史）

完成预览的自动等待、闲置预览的自动输入关闭仍未修。本项真实托盘验收、单独提交后，
再逐项修；不能把已授权三项当作可一次重构全部播放系统。

## 第二项：任务完成预览等待

第一项用户验收、提交 `665cdd8` 与回执 `12433bf` 后，工作区干净开始第二项。
只将手动菜单与正式完成等待分开，不改变自动任务识别、`--task-play`、90 秒规则或闲置。

- `src/BootPlayer.cs`：完成菜单复用 `PreviewFocus`，菜单关闭后核验前台，启动
  `--task-preview`。子进程非零结束异步回托盘提示（不阻塞点击），先取 PID/记日志再
  开启退出通知，避免快速退出释放对象后再读 PID。使用既有完成 pending 互斥体；
  已有完成播放器时立即拒绝，不增加排队。
- 新 `src/CompletionPreview.cs`：单次创建/显示边界，失败返回 2，绝不重试或等待前台。
  仍用既有完成窗口和当前场景库、比例、音量、速度、焦点保护。
- `scripts/Test.ps1`：新增单次成功/三种失败路径回归；使用真实未显示的 WPF 窗口
  检查创建、关闭路径。包内 EXE 沿用原 Build；无需新依赖。

PowerShell 7、Windows PowerShell 5.1 各 25 项检查通过，新回归重复 10 次通过；
原完成过滤/流程/日志回归保持通过。模拟用例不播放，不代替真实托盘菜单验收。
原安全更新脚本备份/只重启助手，采用已有 Codex 实例；缓存 hook 相同才备份/同步 EXE。
27 个媒体文件、Run、hook 定义、Codex PID/start-ticks 前后一致；三种助手和两处 IPC
探测通过。源包/实装/缓存 SHA256：
`ED8B87A34CB2409AF6F4EC719AF2DCA6E0515870587FC2398F6F72B5CC26F3CB`。

备份在 `%USERPROFILE%\.codex-boot-animation\backup`：

- 实装：`update-2150ba85d85a4cff8ccfbef43f6f5861/BootPlayer.exe`。
- 缓存：`completion-preview-cache-fe81f7da6b794fa690758e9953987ead/BootPlayer.exe`。

两份为第一项已验收版本，SHA256 `F9054641DCB4246E15DF7425BD01386E8665354FB8365A484423652A1A4AA7BF`。
回退只停本安装助手、成对恢复 EXE，再采用现有 Codex 实例并探测 IPC；不重启 Codex，
不动媒体/Run/hooks，不删除备份。不用事故前旧版回退。
用户直接点击真实完成菜单后确认“很快出现，画面声音正常”。北京时间 00:50:13
菜单准备至窗口出现约 240ms，首个动态帧约 611ms；原速/音轨、正常结束均已记录，
没有旧 deferred 等待。本项手动验收通过，先单独提交再处理第三项闲置；
不把本次预览当作正式任务结束事件验收。第二项独立提交 `aa59b39`。

## 第三项：闲置手动预览不出现/立即关闭

第二项用户手测、提交 `aa59b39`、回执 `457dc73` 后，工作区干净开始第三项。
保持自动闲置场景优先级、15 分钟闲置/返回安静 2 秒/60 秒机会，以及新输入关闭规则。

- `src/BootPlayer.cs`：闲置菜单经已验收 `PreviewFocus`，去掉手动等待切窗 10 秒。
  专用 `__idle_preview_request__` 在驻留线程派发到 WPF，接受/拒绝均回复，菜单异步
  显示拒绝提示，不阻塞鼠标/消息线程。旧自动返回请求/幂等 token 不变。
  `TryPlayIdle` 增加失败原因与仅 `preview-idle` 使用的 manualPreview 参数，仍执行
  原有 busy/完成 pending 保护；不重叠、不增加自动重试。
  `CodexVideoWindow` 的 passiveIdle 仅对自动闲置为真：保持输入取消、不可激活/
  点击穿透。手动闲置为假，不采集输入 tick、不被菜单鼠标活动关掉，显示跳过，
  允许激活收 Esc。其余场景默认参数不变，比例/音量/速度、owner 消失/最小化保护不变。
  驻留助手仍拥有此窗口，已修片头启动前关闭旧闲置的行为继续生效。
- 新 `src/IdlePreview.cs`：私有原协议上的单次请求，连接 80ms、回复最多等 500ms，
  失败返回 null；超时关闭连接并观察异步任务异常。没有长时间等前台或 UIA。
- `scripts/Test.ps1`：新增测试，隔离管道验证接受、busy、缺视频的响应与无助手失败；
  真实未显示 WPF 窗口验证手动/自动输入策略、激活设置、native passive 样式隔离、
  跳过按钮关闭。测试不操作用户焦点或播放视频，不代替实际菜单验收。
- README/ARCHITECTURE/CHANGELOG 与本记录、当前交接说明同步，包内 EXE 原 Build 生成。

PowerShell 7 和 Windows PowerShell 5.1 各 26 项完整检查通过，新回归重复 10 次；
既有自动闲置、窗口焦点、片头中断、完成、生气及 IPC 回归保持通过。
原安全更新脚本只重启本安装助手/采用已有实例，缓存 hook 一致后备份同步 EXE。
27 个实际媒体文件、Run、hooks、Codex PID/start-ticks 前后一致；三助手/两处 IPC 正常。
源包/实装/缓存 SHA256 `8549D320CA992E6528E520EB0FC3E981B06D1A7EAF6C6D31234430C0869CCA58`。

备份在 `%USERPROFILE%\.codex-boot-animation\backup`：

- 实装：`update-e80f463aa49f42f295b0b3740b4fc63b/BootPlayer.exe`。
- 缓存：`idle-preview-cache-9d9d42b692f547f7a91d02cdafff0026/BootPlayer.exe`。

两份为前两项已验收版本，SHA256 `ED8B87A34CB2409AF6F4EC719AF2DCA6E0515870587FC2398F6F72B5CC26F3CB`。
只停本安装助手、成对恢复 EXE、采用已有实例再探测 IPC；媒体/Run/hooks 与 Codex 不动，
不删除备份，不回退到启动事故前旧版。

用户实际菜单测试后确认“播放正常，移动鼠标不消失，跳过有效”。北京时间 00:59:21
与 00:59:47 两次请求至窗口出现分别约 54ms、45ms；均有动态帧、音轨/原速和正常结束，
没有 user-active 提前取消。跳过有效依据用户确认和未显示 WPF 按钮回归；这两次
实际播放日志是 ended，没有现场 skip-button 记录，不假称日志证明了点击跳过。
第三项已手动验收并单独提交 `6d6524c`。本轮三个故障收尾，不扩范围或冒充
真实 15 分钟闲置返回、任务结束事件、Windows 重启与另一台电脑验收。
