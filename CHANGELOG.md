# Changelog

## 0.8.0-beta.1 (2026-11-19)

- **GitHub 轻松使用与免钩子绿色安装**：新增 `start.cmd`（免修改注册表直接试用）、`uninstall.cmd`（一键停止并移除自启）和 `scripts/Install-Standalone.ps1`，并将默认 `install.cmd` 升级为免钩子独立安装模式（无需 Codex CLI、插件商店注册或钩子信任即可直接使用）；重写 `README.md` 与 `START-HERE.md` 面向新用户 30 秒上手。
- **补齐全部 15 个内置视频的首帧封面图**：使用本地 FFmpeg 为缺失首帧图的 4 个视频（`冷启动/MiniMax_H3_00359_.mp4`、`任务完成/MiniMax_H3_00333_ (1).mp4`、`闲置互动/MiniMax_H3_00359_.mp4`、`闲置互动/在吗，在吗.mp4`）生成并纳入对应 `-first.png`，`media-preflight` 零警告（`videos=15 covers-missing=0`）。

## Unreleased

- 2026-11-19 全面体验与稳定性升级：
  1. **冷启动首帧防卡顿**：新增 `IntroCoverGate` 状态机，取消未预热视频打开前的 150 ms 干等静态遮罩，将撤罩时机从写死的 `Position > 0.20s` 改为确认归零附近后真实推进（≥ 0.05s 且增量 ≥ 0.03s，16 ms 加密检测），同时拦截复用缓存时的旧位置（如 `0.416s`/`0.6187s`）闪帧；用户实机冷启动确认流畅。
  2. **后台内存与稳定性优化**：`--resident` 改为按需懒加载视频、隐藏后压缩工作集，并在播完闲置 2 分钟后优雅退出重建（`resident-idle-recycle`），物理内存从 164.6 MB 降至 11–14 MB；新增 `RestartBackoff` 为 `--watch`/`--resident` 提供 2s 启动保护窗与指数退避恢复；管道 `Dispatcher.Invoke` 补齐 2 秒超时；补齐 `Process.Start`/`StreamWriter` 释放并清零 `CS0649` 警告；新增 `RuntimeCleanup` 启动时自动清理过期临时状态文件（14 天前 `.claim`、7 天前 `turns`/`completion-deliveries`）。
  3. **脱离插件钩子恢复生气与完成触发**：定位到 Codex `0.167.0-alpha.4.1` 已移除 `plugin_hooks`，扩展 `CompletionCursor`/`CompletionJournal`/`CompletionStateStore` 实时监听会话日志 `event_msg/item_completed (UserMessage)` 并跨进程 `turn_id` 去重，5 大触发场景全部脱离 Codex 钩子依赖。
  4. **高敏中文生气词库**：新增 `AngerLexicon.cs` 覆盖国人日常开怼、阴阳怪气、讽刺反话、嫌弃、不耐烦、短句与常见缩写/英文脏话，291 条正例全命中、105 条反例 0 误触发，用户实测确认通过。
  5. **偏克制任务完成与人性化闲置互动**：新增 `CompletionGate.cs` 结合每轮工具调用次数与耗时实行“偏克制档”（重活工具 ≥ 3 或 ≥ 45 秒必播、后台切走 ≥ 15 秒/工具 ≥ 1 角落召回、前台小活不打扰、拦截反问/失败、同会话 60 秒冷却）；重构 `IdleReturnPolicy.cs` 为前台发呆 5 分钟主动互动、后台冷落 10 分钟回来 0.8 秒迎接、离座 5 分钟回来 5 分钟内切回触发、开播 1.2 秒防手抖宽限与 15 分钟冷却。
  6. **磁盘清理与同步**：清理实装废弃旧视频（`assets/videos`）与历史备份/诊断目录共释放 754.5 MB（保留 `tools/ffmpeg`、自启回执和最近 2 份回退备份），同步实装 README 与三处 `BootPlayer.exe`（SHA256 `B130CFBF…032F07D3`）。全套 70+ 逻辑/安装测试 + 4 项真实媒体解码测试 + 实装 7/7 全部零警告通过。

- 2026-10-30：新增冷启动首帧两秒/20 条上限的异步元数据诊断，记录回零、显示、播放及遮罩前状态；不改变播放参数或遮罩规则。隔离探针的暂停帧刷新对照未证实改善，未部署该候选。两套默认各 70 PASS、媒体 4 项、实装 7/7，7 文件部署、170 项保护一致，EXE 153245D6…D5E0C1EB。卡顿根因及修复仍待确认，详见 REVIEW 第十六节。

- 2026-10-29 点击/日志仲裁：点击仅催读既有日志，统一由确认事件派发新聊天动画，取消两个来源独立播放及全局时间冷却；按窗口/事件指纹识别连续新建、拒绝重复和较旧记录，普通会话路由正确复位。日志缺失时不再独立点击播放。真实入口隔离并发及两套默认各 70 PASS、实装 7/7；部署 8 文件、168 项保护一致，EXE 68D3860E…F6AC706B。用户确认“及时且只播一次，声画正常”，启动偏好保持。详见 REVIEW 第十五节。

- 2026-10-29：修复多窗口日志共用空白路由状态导致的漏播及重复触发，状态按进程创建身份/windowId 隔离，有效身份/时间检查、512 条容量与退出清理，保留启动首页保护。PS7/5.1 完整默认各 70 PASS、实装 7/7；9 文件部署、166 项保护一致，EXE 23D55922…519575BE。用户确认本轮触发、画面和声音正常。Codex 禁止自启、助手允许自启保持；点击/日志双向关联与全局时间抑制仍待单独修复，详见 REVIEW 第十四节。

- 2026-10-27：按用户纠正将启动偏好拆开：Codex 本体保持禁止自启，动画助手允许登录启动；只改助手批准和策略，旧任务保持禁用。修复助手刚启动时片头请求在 80 ms 连接超时后丢失的问题，desktop-start 最多等待 2 秒，其他请求不变。新增延迟管道/单次命令/超时及独立启动偏好回归，两套默认各 70 PASS、实装 7/7；7 文件部署、166 项保护一致，EXE 97E735E4…DA525E78。用户确认补播画面和声音正常；未重启 Codex 或重新登录。

- 2026-10-25 导航初始化：历史边界扫描限定 65,537 字节，超长旧半行尾段不误作新事件；就绪扫描改为有界头尾窗口及 64 KiB 单行缓冲。先监听、逐文件初始化并补读，坏文件独立记录，恢复时不重播历史完整事件。定向 2/2、两套默认各 69 PASS、实装 7/7；10 文件部署、162 项保护一致，EXE 78A93EC9…2FBE799B，自启动保持禁用。未作声画或重新登录实测，详见 REVIEW 第十二节。

- 2026-10-25：修复完成日志 CopyTo 无界读取及未换行时重复读尾部。新增按文件的有界扫描游标，半行只保留元数据；完整候选保持原 16 Mi 字符解析契约，超限记录原因并恢复后续行。最多两个工作者、512 个文件状态，去重通知并自动轮转积压，移除全局锁内 I/O。两套默认各 69 PASS，实装默认 7/7 与专项 1/1；8 文件部署，162 项保护一致，EXE 1BE2BF19…AB353482，自启动保持禁用。未作声画/登录实测，边界和失败重试记录见 REVIEW 第十一节。

- 2026-10-24 日志串接：NavigationCursor 绑定实际文件身份和短边界锚点，重写时清理旧半行及丢弃状态，读取异常恢复重试状态；启动种子同时绑定身份。旧失败样例及真实临时文件回归通过，PS7/PS5.1 默认各 69 PASS，部署后 7/7 PASS；8 文件同步至实装/缓存，EXE 6BECEAB2…CF03161。160 项保护核对一致，实际 Windows 自启动仍禁用；未作声画或登录实测。详见 REVIEW 第十节。

- 2026-10-24 晚：纠正“自启动已关闭”的隔离视图误判。独立 Windows 进程发现实际 Codex StartupTask 仍为 2、助手缺少禁用批准，已在实际视图设置 1/3 并由另一外部进程确认，旧任务 Disabled。共用启动检查拒绝包内读回，补回归并同步实装支持脚本；PS7/PS5.1 定向各 15 PASS。未注销或重启实测，详见 HANDOFF 最新事故纠正记录。

- 2026-10-24：修复维护脚本把 Run 安装登记误当登录启动许可的问题。安装/更新读取 startup-policy.json 和实际禁用批准，拒绝不明确或冲突状态；更新的 KeepStartupDisabled 支持保留路径的禁用登记，诊断不再建议开启自启。PS7/PS5.1 定向各 14 条 PASS，实装两配套脚本同步、102 项文件保护一致；播放器与自启动状态未变。复现日志截断写长后的半行串接风险，列为下一独立修复项，详见 REVIEW 第九节。

- 2026-10-21：将已验证 A1–A3 及路径隔离补修实际部署，实装/缓存 EXE 与冻结源构建一致（9C2D7C47…ED7FAA34），共 61 个文件核验；实装默认 7/7、实装媒体 3/3 与实际 readiness/IPC 通过，67 项个人数据/插件定义/启动项保持。增加本地构建来源回执、完整旧文件备份与回退脚本；更新器 adopt 改为 10 秒有界等待。仅重启动画助手，Codex 自启仍关闭；不是整个架构整改完成或正式版本发布。

- 2026-10-21：补 A1 测试路径隔离，C# 与安装测试共享绝对路径/真实根父子关系/重解析点校验；要求测试助手入口绑定隔离根。首轮测试发现 WMI 子进程未继承环境，修测试夹具显式传根后，两套 PowerShell 默认各 64 条 PASS。真实状态/实装 EXE 快照一致；未部署、未作真实画面验收。既有多轮未提交改动保留，本轮增量和回退旧副本已保存；详见 EXECUTION-2026-10-20-A1-A3 最后节。

- 2026-10-08：回退后用户实际确认默认画面/声音/跳过正常，仍有触发迟到；
  本次源记录到派发4.905秒、派发到起播16.945ms。新增已知当前Codex日志尾部
  后台补读（250ms阈值、最多16文件、单工作门、不积累任务），保留文件通知/
  5秒目录及完成核对/原点击与冷启动抑制；不增加UIA或改播放器。新增分割行/
  无通知/有界文件/停止/慢核对隔离回归及固定读取元数据诊断，两套PowerShell
  各41组通过，更新器最终41组后只更新助手；用户原条件实际确认触发及时、
  画面/声音/跳过正常。正式tail补读源记录时间差248ms、读取8ms，随后约52ms
  起播，只派发一次。延迟单项本机验收归档，不宣布长期黑屏根治。备份/回执
  见VIDEO-FREEZE最后一节。

- 2026-10-08：对新聊天黑屏实装单窗口软件渲染与受限状态诊断候选，两套
  PowerShell及更新器各41组通过；用户真实反馈动画出现但卡顿、像慢放，验收
  失败。保存候选与诊断后20:19:26仅重启助手完整回退；源码/测试和三份EXE
  恢复原基线F4E463F5...829E028A，已通过生气修补保留。267保护文件/12个Codex
  进程/Run及标记保持，readiness/IPC通过；没有提交候选功能或宣布黑屏已修复。
  四次35个周期样本遮罩退出/视频可见、UI间隔78～94ms，但没有实际帧率或
  CPU/合成开销证据。详见docs/VIDEO-FREEZE-2026-10-08.md最后一节。

- 2026-10-08：口语生气识别候选补定向脏话变体、无标点/混合抱怨与台词排除，
  只修改PromptReaction和识别回归，派发/监听/播放器/媒体/声音保持。
  旧规则在新增22正例/25反例上19漏判/1误触；候选279识别样例、12分类及
  两套PowerShell完整各40组通过；既有更新器再次40组通过，成对备份同步实装/缓存，
  仅重启助手，保护文件/设置/Codex身份保持。用户已确认真实发送的画面声音/跳过通过，
  追加口语测试也确认通过，单项本机验收归档；不再次部署/重启。
  12:58普通消息已实际match=yes并自动打开生气窗口，有音轨且现场skip-button关闭；
  13:03追加消息也match=yes、有音轨且正常ended；实际验收依据为用户反馈，
  不以媒体时钟推进代替验收。
  新聊天声音继续/画面冻结未修复，不把此识别补丁写成卡帧修复。
  详见 docs/ANGER-MATCH-2026-10-08.md。

- 2026-10-06：修复云端测试时限与首帧可选契约：WPF 解码/像素恢复断言保留，
  测试内部 12→45 秒、外层 15→60 秒；无首帧提示 WARN，缺视频/越界路径仍失败。
  同步仓库测试 EXE，防止打包恢复旧测试时限；临时软件渲染实验已清理。
  Actions 37436342012 的编译、两套各 40 组、打包再验证及测试产物上传通过。
  云端 FFmpeg 扩展明确 SKIP，本机两套各 40 组及 FFmpeg 扩展通过。
  生产播放源码、实际安装、Codex、配置、视频与原公开 Alpha.5 ZIP/标签不变。
  详见 docs/CI-MEDIA-PROBE-2026-10-06.md；不冒称另一台电脑验收完成。

- 2026-10-06：完成 GitHub 上传与 Windows Alpha.5 预发行。main 完整158文件快照
  逐项哈希一致，保留远程历史；冻结153文件源码树另建发布标签镜像，不推送未审查本地历史。
  原60,039,511字节ZIP及两份附件的服务器SHA256/大小均与本地一致，三附件公开发布。
  官方CLI登录使用Windows凭据库；不读取浏览器凭据、不改系统代理或实际插件。
  首次云端Build通过、解码探针失败，明确公开未通过项；不改代码、关闭测试或重打包。

- 2026-10-06：继续诊断上传失败并读取远程完整树：126文件，缺32、不同6、远程独有0。
  官方网页附件通道仍失败；Git元数据/API可读不等于大文件传输通过，浅clone仍超时。
  已安装gh未登录，等待其具体账户授权；不读取浏览器凭据、不修改插件、不发布空Release。

- 2026-10-06：用户确认AI图标和插件目录全部现有视频公开授权，补齐CC BY4声明，
  源码清单包含15个场景视频路径（同片在两场景保留）；本地提交b4a628f。
  已通过网页提交源码、脚本、文档、图标及部分插件/场景文件。随后文件附件存储
  请求失败，hooks批次及独立JSON重试无回执，Release的114字节校验文件也失败。
  完整上传/Release未完成；不把准备队列算提交，不改播放器、不重打Alpha.5 ZIP。

- 2026-10-06：按用户授权用内置浏览器创建GitHub公开仓库，实际提交14个根目录
  文件（cd0967b快照），README/MIT已显示。其他目录和Alpha.5 Release尚未上传。
  图标原授权仅本机使用，含图标的素材/EXE/ZIP等待明确公开授权；不假称完整发布。
  记录网页提交不保留本地历史、子目录不能扁平上传；没有改运行代码、重打包或重启。

- 2026-10-06：补充中文 Alpha.5 发布文案，突出安装者必须自行审核本插件的三项钩子，
  把来源核对、已启用/已信任区别和 check.cmd 成功标准写明。补齐没有 CLI 命令、
  没有审核入口或找不全三项时的停止反馈，以及审核后的退出操作。
  仅文档；不安装、替用户信任、更新运行代码、重打 ZIP 或上传。

- 2026-10-06：将 START-HERE 改为简短中文逐步安装指南，补全 ZIP 解压、安装
  成功/停止提示、三个钩子的来源审核、无审核提示时的 CLI 备用入口及诊断分流。
  明确桌面聊天框不能代替 CLI /hooks、普通安装不需要诊断项目、首帧图可选。
  只修改文档；不改变代码、EXE、安装脚本、当前设置或信任，不重打已核查的 Alpha.5 ZIP。
  指南单独随发布说明提供，尚未取得新手/另一台电脑的完整照做验收。

## 0.7.0-alpha.5 — locally verified Windows Alpha (2026-10-06)

- Package the accepted completion-delivery baseline without rebuilding the
  executable or changing playback. Synchronize native/portable manifest versions
  and release guidance; retain earlier ZIPs. Extracted PS5.1/PS7 each pass all
  40 groups and installer preflight; isolated install/cache/media/three untrusted
  hooks pass. All protected live files, helper/Codex process identities and old
  artifacts are unchanged. ZIP is pinned to ead63ed with checksum and a separate
  verification report; no upload or cross-PC stability claim.

- Completion-delivery reliability patch: reserve anonymous requests
  separately, claim/consume only after durable playback-start or merge receipt;
  permit at most two retries in a two-second budget. Separate acknowledgement
  IPC merges simultaneous tasks without video overlap, preserves continuation
  identity across newer input and rejects stale installed executables. Explicit
  early Skip/Esc dismissal never retries into a new window. Existing
  candidate/claim formats and other scenes remain unchanged. Both PowerShell
  runtimes pass all 40 groups, with five extra isolated delivery repeats. Backed-up
  installed/cache deployment and helper-only restart passed; user accepted the
  notification and new-chat protection on 2026-10-06. A natural task completion
  reached accepted playback with audio and ended normally. No public package,
  second-PC guarantee or permanent exactly-once claim; see
  docs/COMPLETION-DELIVERY-2026-10-05.md.

- Freeze the previously tested reaction-flow and intro-recovery changes as
  separate source commits, then rebuild the package executable and run all 39
  groups under PowerShell 7 and Windows PowerShell 5.1. This is source/package
  archival only: no live deployment, connection/default-install change, or new
  visual acceptance. See docs/FREEZE-2026-10-05.md.

- 记录本机输入反馈连接规避验收：用户确认延迟可接受并批准保留，3个桌面
  新对话采用新提供方，配置及32保护文件复核通过。无播放器或默认安装变化；
  完整退出重开/跨电脑未验证，不宣称永久根治。个人配置/备份不提交。
  见 docs/CONNECTION-WORKAROUND-ACCEPTANCE-2026-10-05.md。

- Disable unnecessary paused-seek frame scrubbing for new-chat intro media,
  retaining startup and other scene policies, timing, media and sound. Extend
  isolated decoder checks to require changing non-black pixels through initial,
  recovered and healthy-cache reuse playback. Both PowerShell runtimes pass all
  39 groups; helper-only backed-up deployment and readiness checks pass. User
  accepted three real new-chat plays with sound, no picture freeze or stale-frame
  flash. Preserve 113 protected files, settings, startup/trust and Codex process
  identities. The intermittent rendering root cause and long-resident stability
  remain unproved; do not claim permanent resolution. This patch is not an anger
  hook delivery fix. See docs/NEW-CHAT-MEDIA-RECOVERY-2026-10-05.md.

- Recover failed resident intro media on the next trigger instead of retaining
  an unusable slot indefinitely. Retire the old identity before releasing it;
  preserve healthy preloads and bound retries to explicit requests. Hide the
  overlay before reset, handle media setup/start errors, and make timeouts
  recoverable. Add synthetic late-error and hidden real WPF recovery tests.
  Both PowerShell runtimes pass all 39 groups; backed-up helper-only update and
  protected-file checks pass. User accepted three real new-chat/skip cycles; the native
  HRESULT cause is not claimed fixed. See docs/NEW-CHAT-MEDIA-RECOVERY-2026-10-05.md.

- Dispatch matched anger reactions before task-state recording; preserve state
  in a synchronous finally block without changing storage, locks or classifiers.
  Both PowerShell runtimes pass all 37 groups, plus 10 isolated flow repeats.
  Backed-up deployment and one synthetic production playback pass; actual
  cold-start send acceptance is pending. Same-turn evidence shows upstream
  preparation before hook invocation, which this patch does not eliminate.
  See docs/ANGER-LATENCY-2026-10-04.md. Source archived separately as 447d472;
  the old upstream-delay limitation is not a claim of a new playback fix.

- Move watcher log/journal reconciliation off the click message loop into one
  non-backlogged background job; re-arm the click hook per new desktop identity.
  Preserve playback, discovery frequency, recognition, state ordering and media.
  Both PowerShell runtimes pass all 36 groups; backed-up local deployment and
  readiness/IPC pass. Real next-boot click acceptance remains pending. Startup
  network warnings and pre-hook reaction delay are not claimed fixed. See
  docs/COLD-START-REACTION-2026-10-04.md.

- Add anonymous stage timing for automatic anger reactions, retaining the pipe
  command and playback/filter decisions. Buffer hook writes until after dispatch
  and flush resident diagnostics asynchronously. Record fixed stages/results,
  bounded per-segment counts and process-origin correlation without prompt text,
  prompt hashes, session identifiers or media paths. Add isolated identity/privacy
  regressions; both PowerShell runtimes pass all 35 groups. User confirmed normal
  picture/audio for one actual send; its correlated stages end in normal closure.
  This is instrumentation, not a
  cold-start latency or missed-display fix. See docs/REACTION-TRACE-2026-10-04.md.

- Extend local anger recognition for directed ability/attitude criticism,
  repeated-failure complaints and explicit disappointment. Guard additional
  technical nouns and narrowly neutral fragments without suppressing hostile
  follow-up clauses; distinguish plain negation from rhetorical insults.
  Preserve the previous fixtures and add 98 recognition fixtures plus five
  task/reaction classification checks. Both PowerShell runtimes pass all 34
  groups; user accepted the first trigger, the second after isolated retest and
  the normal-request exclusion. Preserve the initial missed-display report;
  its cause is unresolved, not fixed by the successful retest. No playback,
  prompt dispatch, startup, hook trust or timing changes.
  See docs/ANGER-EXPANSION-2026-10-04.md.

- Expand local anger recognition for colloquial Chinese intensifiers/suffixes,
  direct belittling and short strong complaints; normalize compatibility forms
  such as full-width SB. Preserve translation/quotation/context exclusions and
  guard simple praise/negation, technical garbage nouns and USB references.
  Add 47 positive and 40 negative fixtures to the existing anger regression,
  retaining the original cases and anonymous case-index logging. Both PowerShell
  runtimes pass all 34 groups; user confirmed two actual message sends each
  played with picture and sound. This is not cold-start latency acceptance.
  This changes recognition only, not prompt dispatch, cold-start timing or
  playback. See docs/ANGER-DIAGNOSTICS-2026-10-03.md.

- Reserve a 48-DIP original-title-bar strip for owned anger, idle and foreground
  completion videos, instead of masking Codex's drag area. Keep fullscreen intros,
  independent completion notices, triggers, audio and preferences unchanged.
  User confirmed title-bar access and dragging but reported flashing during
  movement. Replace unconditional timer positioning with exact-owner,
  process/thread-scoped out-of-context location events, coalesced refreshes,
  unchanged-bound suppression and move-only positioning. Dispose subscriptions
  on close; keep changed-only timer fallback if registration fails. Add geometry,
  hidden synthetic-owner event/movement, filtering and cleanup regressions;
  both PowerShell runtimes pass all 34 groups. User confirmed smooth dragging,
  no flashing and working Skip in the actual anger preview. Do not extrapolate
  that acceptance to every scene or physical multi-monitor/DPI setup.
  Backed-up companion update does not restart Codex;
  see docs/DISPLAY-ACCEPTANCE-2026-10-03.md.

- Replace only the companion notification-area icon with the supplied artwork,
  preserving transparency in nine icon sizes. Retain the original square
  artwork; after the user found its full-body tray rendering unreadable, generate
  a head-only rabbit-ear/white-hair/red-eye design with the built-in image tool.
  User approved its design before integration, then preferred the original
  full-body artwork after trying it. Restore the original icon and converter
  default, retaining the compact design as an alternative; no subsequent C# changes.
  Embed the ICO as a managed resource so runtime never depends on Downloads or
  asset paths. Own/dispose the cloned icon and fall back to the system icon on
  resource errors. Keep executable/window/plugin-list icons, playback, settings,
  triggers and hook trust unchanged. Add resource and failure regressions; both
  PowerShell runtimes pass 32 groups. Companion and cached fallback EXE updated
  with backups; live resource loading, IPC, hook trust and untouched user data
  verified. The latest diagnostic could not read the compatible CLI/plugin state
  (UNKNOWN); do not claim every diagnostic passed. User accepted the restored
  full-body tray appearance; no separate manual menu-operation evidence was
  reported. See docs/TRAY-ICON-2026-10-03.md. Artwork redistribution
  licensing remains a separate pre-publication decision.

- Add independently persisted background completion-notice size/corner presets
  and a Chinese tray editor with unsaved-selection previews. Preserve the
  validated Large / BottomRight default, immediate automatic delivery, Codex
  foreground presentation, audio settings and non-activating/skippable windows.
  Reject conflicting/invalid saves and keep previous-version backups without
  migrating scene preferences. Both PowerShell runtimes pass 31 regression groups;
  live companion/cache updated with backups. User confirmed preview placement
  and sizes, and save/reopen persistence. An explicit one-shot task-player test
  loaded the shared saved layout and played to end. User confirmed correct layout,
  picture/audio and no switch back to Codex; retain the foreground-HWND-change
  log without claiming its cause was established. This is not a real model-complete
  event or proof of multi-monitor or second-PC behavior.

- Add Chinese per-scene automatic enablement, mute and 0–100% volume settings
  with immutable playback snapshots, a shared bounded/versioned JSON store,
  conflict rejection and atomic previous-version backups. Preserve startup 85%
  / other scenes 65% defaults, legacy idle-off compatibility, global automatic
  pause, explicit previews, existing rates, routing and window placement.
  Changes apply to next playback, without file reads on the click path. Add
  storage/editor/player-parameter regressions; both PowerShell runtimes pass
  30 groups. Update only the companion and cached fallback EXE with backups;
  Codex, media, hook definitions/trust and startup remain unchanged. Actual
  user acceptance confirms immediate muted New chat playback, disabled automatic
  New chat playback, working explicit previews while disabled, and restored
  automatic enablement. Remaining scenes, restart persistence and multi-DPI/PC
  cases remain separate; do not treat fixtures as all-trigger validation.

- Add an on-demand Chinese, read-only MP4/stream/audio/decode/first-frame checker and CLI/JSON entry point. Keep missing tools/timeouts explicitly unknown. Validate imports before copying, refuse existing video/cover names, and bound no-overwrite cover extraction. Prefer a shared UserProfile tools directory over redirected AppData after the user's Explorer-launched check exposed missing-tool results. Add real FFmpeg fixtures and dual-PowerShell regressions; keep optional GPL tools separate from the repository. Do not change the player, trigger hooks, startup, trust, media or playback settings. Independent external-process checks and the user's Explorer-launched retry passed: 12 videos, zero failed/unknown items and seven missing-cover warnings, with no automatic cover changes. This is checker acceptance, not revalidation of every animation trigger.

- Add Chinese read-only diagnostic labels, status counts and actionable explanations to check.cmd output, preserving detection and the existing JSON fields/statuses. Keep unknown future values visible and distinguish pending manual acceptance from failure. Add a dual-PowerShell presentation/data-preservation regression; do not update the player, hooks, startup or media.

- Show qualified automatic completion events without waiting for Codex foreground: retain the foreground-owned view, and use an immediate bottom-right audible, skippable, non-activating notification when Codex is background/minimized. Reuse media cleanup and preserve detection, deduplication, pause, manual previews and other scenes. Add routing/single-attempt/layout-DPI/native-style/skip regressions. User saw the background notification but found it too small; enlarge only its layout to 50% working-area width (height capped at 55%), with a regression that rejects the previous small layout. User confirmed the enlarged view, picture/audio and no switch back to Codex in a synthetic Stop-chain test; a real model-task completion and on-screen Skip remain separate acceptance items.

- Separate manual idle previews from passive automatic idle playback: prepare Codex after the tray menu closes, request a single acknowledged resident preview, and report busy/missing/unavailable failures. Manual previews accept Skip/Esc and survive input; automatic idle thresholds, quiet-return policy, input dismissal, and arbitration stay unchanged. Add isolated reply and unshown-WPF/native-style/skip regressions; user confirmed real tray playback, survival through mouse activity and effective Skip. Logs separately confirm two normal picture/audio playthroughs, not an on-screen skip event.

- Separate manual completion tray previews from automatic task playback: prepare the verified Codex foreground after the menu closes, attempt once, and report rejection instead of leaving a 90-second deferred preview. Keep automatic completion waiting/detection and idle previews unchanged. Add single-attempt success/failure regressions; user confirmed prompt real tray playback with picture/audio and normal-end logs.

- Prepare the verified Codex foreground window only for the manual anger tray preview, after closing/unwinding the menu. Report activation failure rather than silently discarding the request. Automatic triggers and other preview entries remain unchanged in this step; add deferred-dispatch/failure-path regressions. User confirmed the real tray preview, with moving-frame/audio/normal-end logs.

- Stop resident-owned anger/idle windows before a valid startup or New chat intro takes over, releasing old media before showing the intro. Preserve anger when the intro is missing or already failed, and keep the existing trigger/speed/volume paths. Add a real unshown-WPF close/idempotence regression; two live IPC preview transitions were logged and the user confirmed no visual/audio overlap. This is not new-click/cold-start event or independent-PC acceptance.

- Keep the resident alive with both intro folders empty, without a visible/activated shell or null-media failure. Preserve independent idle/reaction delivery and the existing fullscreen state when an intro is later added. Add an isolated empty-library WPF/IPC regression without moving user videos.

- Retain incomplete navigation-log lines until a newline arrives, including split UTF-8/CRLF, so file-write notifications cannot discard new-chat/frontend-ready events. Keep cached-click, deduplication and startup UIA gates unchanged.
- Scope resident IPC to the current user/session with an explicit protected ACL, 1-second absolute read deadline and 256-byte cap. Add isolated stalled/slow/oversized-client recovery and normal-message regressions; no hook definition, trust, media or Codex host changes.

- Raise only cold-start and its tray preview from 65% to 85% player volume at the user's request. Keep every other scene at 65%, playback speed and trigger behavior unchanged; add scene-isolation regression assertions. No media re-encoding or Windows system volume change.
- Startup safety: replace eager repeated full-tree UIA scans with current-process navigation readiness, foreground gating, isolated bounded probes and failure circuit breakers; keep cached mouse-click playback. Observe both physical and packaged desktop log roots. Add readiness/timeout/moving-layout regressions and a startup-disabled safety-update mode. User reported current tests passed; logs confirmed three separate Codex cold starts, one intro each and subsequent frontend readiness. Windows reboot/independent-PC acceptance remains pending.
- Keep companion runtime state/logs in `%USERPROFILE%\.codex-boot-animation` so packaged hooks and the independent helper share the same files. Explicit whitelist migration preserves old logs/media, backs up overwritten state, rejects ambiguity and supports rollback. Shared-file probes and non-UI tests passed; subsequent cold-start failure remains a separate open incident.
- Fix companion lifetime after Codex exits: detach a host-bound supervisor using local WMI, verify session/job ownership, recover the registered supervisor from SessionStart, and reject attached readiness. Add a real kill-on-close host regression with a normal-child control. User confirmed full exit/reopen and New chat playback. See the dated incident record; shared AppData isolation was discovered separately and needs its own fix.
- Recognize short Chinese insults and family-directed profanity without a preceding addressee, along with stronger complaints about repeated bad work. Keep quoted examples and positive exclamations excluded; preserve task-completion candidacy when an angry message also requests a repair.

## 0.7.0-alpha.4 — 2026-10-01

- Keep the clean-install integration home in a short Windows temporary path. Extracted-package verification caught Windows PowerShell 5.1 failing to enumerate a cache nested below a deep Chinese/spaced extraction directory. The real package cache and hook-discovery checks now use a separate short home without changing the user's Codex configuration.
- Bound native CLI inspection to 15 seconds and registration/install commands to 60 seconds after a real diagnostic plugin-list query stalled. Stop only the owned command on timeout, report unavailable state and preserve the rest of the diagnostic checks. Add native quoting and timeout regression cases.
- Send hook-inspection RPC through an explicit UTF-8 writer on .NET Framework. Windows PowerShell 5.1's legacy stdin encoding caused the app-server query to exit for Chinese installation paths; the same clean installation passed after the encoding fix.
- Retain alpha.3 deployment fixes. The failed alpha.3 archive is retained locally; alpha.4 is the package intended for distribution after extracted-package acceptance.

## 0.7.0-alpha.3 — 2026-10-01

- Fix a clean-install compatibility failure on CLI 0.159.2: the root portable manifest installed/enabled successfully but yielded zero runtime hooks. Use the native compatibility manifest with an explicit hook path, retain portable reference metadata outside the plugin, and verify all three runtime hooks are discovered before startup registration.
- Add read-only active-installation diagnostics for dependencies, plugin enablement, actual hook trust, helper/IPC health, active/cache paths, binary mismatches and whitelisted trigger reasons. Keep independent-PC/reboot and physical playback checks pending rather than treating diagnostic success as full acceptance.
- Reconcile session journals across both local and UTC date directories so UTC midnight in western time zones cannot hide a newly completed turn.
- Prefer the desktop-bundled CLI and fall back past incompatible CLI candidates. Check required commands and marketplace conflicts before changing installation settings.
- Verify plugin installation/enabled state. Installation and companion updates wait up to 20 seconds for supervisor, watcher, resident player and IPC readiness; roll back new startup registration/helper processes on failure while retaining a preexisting entry.
- Add `check.cmd`, an incomplete-ZIP extraction hint and a short bilingual setup guide.
- Add installation regression groups for CLI fallback, slow/missing helper readiness and startup rollback. Verify a real plugin registration/install into an isolated empty Codex home, including cached version, EXE and all eleven videos.
- Preserve video presentation, speed, volume and hook command definitions. This version also includes the separately committed anger-clip rotation fix (`4dd6a14`). Independent second-PC and genuine Windows reboot acceptance remain pending.

## 0.7.0-alpha.2 — 2026-09-30

- Preserve task-kind state across automatic continuation turns, while allowing new user input to replace it. Recognize concise requests such as “解决吧”.
- Recover confirmed long-task completion from the journal even if no prompt-hook state exists. Keep short chats, reaction-only requests, failed replies and duplicate events excluded.
- Do not mistake a pending independent second-PC/reboot acceptance caveat for failure of an otherwise completed repair; retain actual failure checks.
- Correlate anonymous completion delivery and player logs. Add production-path continuation/concurrency regression tests and a real-journal playback verification script.
- On the current PC, replay a previously missed real 23-minute task-completion record through detection, delivery and playback; confirm moving picture, audio track, normal ending, and user-observed picture/sound. Independent second-PC/reboot acceptance remains pending.

## 0.7.0-alpha.1 — 2026-09-30

- Debounce natural return input instead of canceling idle playback on a second mouse/key event. Preserve the opportunity for up to one minute while background/busy; consume only acknowledged resident acceptance, with idempotent retries.
- Persist desktop process-instance claims to handle Windows sign-in startup races and suppress replay after watcher recovery. Explicit updates adopt already-running instances.
- Bundle eleven explicitly CC BY 4.0 licensed clips and covers, covering all five scenes. Remove the old New chat duplicate from the active startup folder.
- Add portable plugin metadata, safe companion binary updates with rollback, immutable Git release ZIPs/checksums, and a Windows CI package artifact.
- Verify Windows PowerShell 5.1 script encoding and a clean package extracted to a Unicode/space-containing path. Mark independent second-PC validation as pending, not verified.

- Use the task-completion scene's 65% player volume for every scene, including cold start and idle. Add a non-destructive FFmpeg staging script to match the encoded loudness of future videos to a task-completion reference.
- Add a separately randomized `闲置互动` scene. After 15 minutes of Windows-session inactivity, a quiet return to foreground Codex can show one click-through video covering only the Codex window; input and competing animations dismiss it. The tray can preview or disable the scene without recording input contents.
- Play all anger-reaction clips at their original 1.0× speed; cold-start and New chat intros remain at 1.2×.
- Remove the post-playback anger cooldown; suppress only while an intro/reaction is actively visible or automatic reactions are paused.
- Keep an owner-bound anger clip alive through transient focus changes instead of closing it immediately after a prompt; ignore late media events once its window has closed. Task-completion clips retain their focus-loss behavior.
- Ignore structured questionnaire replies when matching anger prompts, so an assistant-authored test question containing an insult cannot start a clip before the user's real message.
- Keep every preloaded resident video hidden until selected, including after a video-library reload. Switching scenes now hides and mutes all other media layers, preventing a stale startup picture over an anger clip's audio.
- Close a queued full-screen playback if its MP4 fails to open or loading times out; do not show a known-failed clip until the library is reloaded.
- Exclude insult-only test messages from task-completion candidates, while preserving explicit repair requests that also contain an insult.
- Add a resident scene-switch regression test covering reload and subsequent selection.
- Play anger reactions in a skippable window owned by Codex instead of covering the whole monitor. Reuse the task-completion window's aspect-ratio-safe, blurred-backdrop presentation; keep cold-start and New chat full-screen.

## 0.6.4 — 2026-09-30

- Treat a turn lasting at least two minutes with a confirmed completion reply
  as a task even when its initial prompt looked like a question.
- Keep short question-and-answer exchanges excluded.

## 0.6.3 — 2026-09-30

- Hold a confirmed completion for up to 90 seconds if another app is in front,
  then play it when the Codex window regains focus instead of dropping it.
- Keep the completion overlay owned by Codex and avoid covering other apps.

## 0.6.2 — 2026-09-30

- Recover task-completion playback from Codex's local `task_complete` session
  record when the desktop does not invoke the `Stop` hook.
- Deduplicate journal and hook delivery with an atomic per-turn claim.
- Keep the journal watcher in the existing tray helper, without changing the
  startup or New chat playback paths.

## 0.6.1 — 2026-09-30

- Keep questions about completed tasks from being mistaken for new task requests.
- Recognize natural completion summaries such as “已经按规则改好” while
  excluding future plans and explicit failure language.
- Log anonymous entry and suppression reasons for the `Stop` hook, so a
  missing hook run can be distinguished from a rejected completion.

## 0.6.0 — 2026-09-30

- Give startup, New chat, anger reaction and task completion their own MP4
  folders. One clip plays consistently; multiple clips are random within the
  same scene, with no cross-scene fallback.
- Rescan scene folders after changes and release old media objects so replacing
  clips does not accumulate stale players.
- Move video routing to a dedicated source file and retire the old
  filename-based selection state.
- Route task-completion hooks through the installed resident before launching
  the proven one-shot window, so plugin caches do not normally freeze media
  choices.
- Fill task-completion letterbox space with a dimmed, blurred first-frame
  backdrop while keeping the foreground video uncropped.

## 0.5.0 — 2026-09-30

- Add a separate task-completion clip from a local MP4 library, with a tray
  preview and optional video selection.
- Use per-turn prompt/Stop hooks to suppress greetings, short chats and
  unsuccessful replies. Only a one-bit task flag is stored temporarily.
- Limit completion playback to the foreground Codex window instead of the
  whole display; close it when Codex loses focus.

## 0.4.1 — 2026-09-30

- Recognize directed colloquial profanity and frustrated complaints such as
  “你特么”, “你到底行不行”, and “越改越烂”.
- Keep standalone exclamations and quoted/example text from unexpectedly
  opening the full-screen reaction; add positive and negative regression cases.

## 0.4.0 — 2026-09-30

- Add an optional local emotion-response video, triggered by clear insults or
  strong disparagement directed at Codex when a message is submitted.
- Keep submitted prompt text out of logs. Ignore meta-discussion examples,
  avoid interrupting an active intro, and apply a 30-second reaction cooldown.
- Add a tray preview and video-role choice for the reaction, with descriptive
  names for the personal clips in the local video library.

## 0.3.0 — 2026-09-30

- Assign a separate fixed video to Codex cold starts and up to two videos to
  New chat. New chat chooses randomly between its two configured candidates.
- Preload all configured videos in the resident player so choosing a different
  clip does not add video-loading delay to the click path.
- Keep manual tray configuration, first-frame covers, sound, Skip/Escape and
  restart-persistent filename-only choices.

## 0.2.0 — 2026-09-29

- Add a dedicated MP4 library and tray-based manual video selection. The
  selection survives restarts, and the resident reloads the chosen video.
- Add a repository-scoped Codex marketplace, a prebuilt companion, and a
  preflighted installation script for another Windows computer.
- Keep newly added personal videos out of Git by default until their
  redistribution rights are reviewed.

## 0.1.0 — 2026-09-29

- Detect Codex desktop cold starts and new-chat mouse clicks with a persistent
  Windows companion; retain log-based navigation detection as a fallback.
- Preload the player to reduce click-to-video latency, play with sound at 1.2×,
  and provide Skip/Escape controls.
- Suppress duplicate startup/navigation triggers and avoid false playback when
  opening the Codex tray menu.
- Reset playback to the start and cover stale decoded frames with the exact
  first video frame before each replay.
- Keep the companion across Codex exits and Windows logins through a user-level
  sign-in entry. The SessionStart hook is recovery-only.

This changelog records product changes, not personal runtime logs.
