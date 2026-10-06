# Alpha.5 本地发布包核查 — 2026-10-06

## 当前状态

用户确认具体范围后批准一次执行。**本地发布包核查已通过，可作为Windows Alpha测试包；未公开上传，另一台电脑验收仍待后续。**
只升项目元数据/说明为0.7.0-alpha.5，使用已验收EXE，不重建或修改播放代码。
冻结标签freeze-2026-10-06-completion-accepted与既有实际安装保持。

## 范围、保护与回退

- 修改native/portable版本、README、RELEASE、INSTALLATION、CHANGELOG及必要交接/本记录。
- 新ZIP/校验码写dist；不覆盖Alpha.1–Alpha.4。使用干净Git提交的不可变归档。
- 解压至唯一中文/空格测试目录；完整回归和Install -CheckOnly。
- 真正插件注册/缓存只在临时独立Codex home；hooks/list使用该隔离环境的一次性
  app-server，结束关闭。不复制认证，不发送消息、不播放现场动画、不信任钩子。
- 不运行完整Install/Install-Startup，不改真实Codex/信任/自启/连接/媒体/音量/布局，
  不重启任何实际程序、不新增依赖、不上传。临时文件保留供复核，不递归删除。
- 失败停在此问题；必要时只撤销本次版本/说明改动，不回退稳定播放或个人环境。

私有备份及保护快照：用户数据目录backup/alpha5-check-20261006-612d811e；
仅版本文件副本、文件哈希及进程身份，不复制登录凭据。该目录保留protection-before.json
与result.json，不纳入公开归档。

## 核查入口及已知限制

包由既有Package-Release.ps1生成；脚本先跑全部回归，从Git HEAD归档，拒绝覆盖旧包。
解压检查使用Test.ps1、Install.ps1 -CheckOnly、Verify-CleanInstall.ps1。
后者验证隔离缓存版本、EXE、媒体及三钩子enabled/untrusted，不绕过信任。
两套PowerShell检查及隔离安装不是另一台电脑、登录重启或实际钩子信任手测的替代。

初始只读范围检查：152个Git跟踪文件；11个MP4与CC BY 4.0媒体目录逐项一致；
无日志、数据库、认证/用户配置/个人设置/备份/诊断目录被跟踪；常见密钥模式未检出。
这只是限定规则的扫描，不承诺绝对无秘密；历史技术记录仍含示例、本机路径和Git署名信息。
首轮枚举中文文件时Git默认引号使MP4列表未识别，检查报错即停；随后只对该命令
使用core.quotepath=false重新完整比对通过，没有更改Git全局配置或媒体。
一次文档补丁上下文未匹配，整批被拒绝、未落盘；重新核对精确上下文后再应用。

## 已完成检查

候选版本Windows PowerShell 5.1完整40组通过；源码目录Install -CheckOnly通过，
报告既有实际助手路径冲突并阻止完整安装，这是保护措施而不是安装成功。
源码目录有14个本地MP4，Git/授权目录只有11个；最终包须精确只含11个受审素材，
不能把源码目录的额外个人视频打入归档。当前核对环境desktop26.930.3930.0 / CLI0.160.0。
核查后的最终验收另写仓库本记录及dist旁置报告；不可变ZIP内是归档时的记录，
不要为补写事后报告覆盖该ZIP或让其校验码失效。

包来源提交：`ead63ed7623cf3ea8c7053042e599e15f26114c1`（仅9个发布元数据/文档文件）。
没有重建EXE，src/scripts/assets/hooks相对于此前验收冻结标签无差异。

包：`dist/codex-boot-animation-0.7.0-alpha.5-windows.zip`，60,039,511字节。
SHA256：`85d26f36aec2afe8dcf113f4e70b4c8f5deb086c70719a6bebd97bfc7ed7d8ca`。
附同名.sha256；只从干净Git提交归档。ZIP含153个文件，逐项匹配来源提交，
11个MP4精确匹配CC BY 4.0目录，11张封面由既有回归完整检查。代码/文档MIT。
解压后二进制逐项SHA256相等；文本只允许Git既有换行正规化，全部内容一致。
解压清单/常见凭据模式再检未发现禁止的私人文件或密钥格式，保留有限扫描的限制。

核查目录：`C:\Projects\cba-alpha5-8985d681\中文 空格`，保留供复核。
- 源码PS5.1完整40组、Package-Release运行PS7完整40组通过。
- 解压包PS5.1完整40组、PS7完整40组通过。
- 解压包两种PowerShell的Install -CheckOnly均通过；均报告已有实际安装冲突，
  明确阻止直接完整安装，不冒充已部署、不接管原助手。
- 解压包Verify-CleanInstall用PS5.1执行：临时独立Codex home注册/安装/启用、
  缓存版本alpha.5、EXE字节、11段视频检查通过。SessionStart/UserPromptSubmit/Stop
  均发现且enabled=true、trust=untrusted；未审核或绕过信任，没有真实消息或现场播放。
- 私有隔离home：`%LOCALAPPDATA%\Temp\cba-clean-5b21c4a46681437a960f20836bcbc0e3`，
  临时app-server结束，由既有脚本负责关闭，环境变量只在子检查进程中使用。
- 最终保护核对：36个保护文件（包含源码包/安装/缓存三EXE）、16个原进程身份
  （13个Codex、3个助手）、Run项和8个旧ZIP/校验码哈希全部不变，实际助手IPC/生命周期仍通过。

核查完成后只保存本报告、CHANGELOG/RELEASE/HANDOFF/NEXT_ACTION状态，
另附dist/codex-boot-animation-0.7.0-alpha.5-windows.verify.md；不重写已校验的ZIP。
发布标签`v0.7.0-alpha.5`指向包来源ead63ed，不指向事后报告提交。
dist报告与校验码是发布附件，不含机器路径、进程ID、配置或私人日志。

## 当前结论与后续取舍

本轮一次性核查完成；不再追加功能、重构或重复已验收动画。当前适合Windows社区Alpha，
不标稳定版/官方内嵌插件/所有电脑必能运行。下一阶段只需维护者决定是否公开上传及
准备面向用户的简明发布说明；远程仓库、上传或账号操作必须另行授权。
另一台电脑/独立账号、不同缩放/多屏、登录重启与真实信任/卸载，留稳定版前验证。
