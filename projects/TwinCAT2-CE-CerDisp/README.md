# TwinCAT2 CE CerDisp

项目状态：**原始 EXP 已归档；用户反馈已在 CX8090、CX5120-0111 上测试。归档维护者未执行编译或实机复测。**

本目录用于归档用户的 TwinCAT 2 / Windows CE / CerDisp 相关项目。原始文件为 [`开启CEROMET查看IP.EXP`](original/开启CEROMET查看IP.EXP)，按原始字节归档，保留文件名、编码和换行。文件包含 ST 程序 `MAIN_SysInfo`，调用 `FB_GetAdaptersInfo` 读取网卡信息，调用 `NT_StartProcess` 尝试启动 `\windows\CerDisp.exe`。这些描述来自源码阅读。用户反馈已在 CX8090、CX5120-0111 上测试；具体软件版本和逐项测试结果尚未提供。

## 文件结构

- `README.md`：依赖、导入流程、安全说明和验证状态。
- `original/README.md`：原始文件接收及保真要求。
- `original/开启CEROMET查看IP.EXP`：用户提供的原始导出文件，2072 字节。

## 范围与数据流

本次完成原件归档与文档更新。源码中的调用和数据流如下，调用周期由使用工程的任务配置决定：

- `btHMIGetNetInfo` → `fbGetAdaptersInfo(bExecute:=btHMIGetNetInfo)`；`TOF_Delay` 设置 `PT:=T#5S`，在其 `Q` 为 TRUE 时，将 `arrAdapters[0]` 和 `[1]` 的描述、IP、子网掩码复制到 `info_Ad_Nam1/2`、`info_Ad_Add1/2`、`info_Ad_Sub1/2`。
- `bEnableCerDisp` → `NT_StartProcess_Test` 的 `START`；程序路径为 `\windows\CerDisp.exe`，工作目录为 `\windows`。
- `SystemInfo.projectName` → `info_Pro_Name`，该赋值同样受 `TOF_Delay.Q` 条件控制。

原件没有在复制网卡信息前显式检查读取完成、错误状态或有效网卡数量；启动调用的 `NETID`、`COMNDLINE` 留空，`ERRID` 输出未接收。上述导出语法、目标选择及错误处理需在匹配环境中检查，不应视为已验证的完整应用。

## TcUtilities.lib 依赖

**需加载TcUtilities.lib库**。在 TwinCAT 2 PLC Control 的库管理器中添加匹配版本的 `TcUtilities.lib`。源码使用 `FB_GetAdaptersInfo`、`NT_StartProcess`，并声明 `GETCURTASKINDEX`；最低库版本及其他依赖以实际编译结果确认。

源码还引用 `SystemInfo.projectName`，但本 EXP 没有定义 `SystemInfo`。需在使用工程中确认其来源和类型；不要假定仅加载一个库即可编译整个导出文件。

从适配目标 TwinCAT 2 版本的正规 Beckhoff 安装介质或官方渠道取得库，在 TwinCAT PLC Control 的库管理器中添加引用，并记录实际库版本。不要将 TwinCAT 3 的 `Tc2_Utilities` 名称直接当作可替换依赖；本项目尚未验证跨版本迁移。

库文件不随本目录分发。缺少符号时先检查库版本、目标平台和间接依赖，不通过猜测接口修改原始导出文件。

参考：[Beckhoff TwinCAT 2 TcUtilities 官方手册](https://download.beckhoff.com/download/document/automation/twincat2/TwinCAT_2_PLC_Lib_TcUtilities_EN.pdf)。手册介绍库接口，不代表本项目已在任何设备上通过验证。

## 导入与使用步骤

**需加载TcUtilities.lib库**。以下步骤依据原始 EXP 整理；用户反馈已在 CX8090、CX5120-0111 上测试。归档维护者未执行离线编译或实机复测。

1. 备份现有 PLC 工程、控制器配置及可恢复版本，在离线工程副本中操作。
2. 取得原始 EXP，核对文件大小与 SHA-256；保留原编码、换行和文件名，使用副本导入。
3. 使用与目标设备匹配的 TwinCAT 2 PLC Control，选择 **Project → Import**，选取 EXP，检查导入对象和同名冲突，不盲目覆盖现有对象。
4. **需加载TcUtilities.lib库**：在库管理器中添加匹配版本的 `TcUtilities.lib`；检查 `SystemInfo.projectName` 的外部定义和其他依赖，并确认 `MAIN_SysInfo` 在使用工程中的调用关系。
5. 核对目标设备是否存在 `\windows\CerDisp.exe`，确认 `NT_StartProcess_Test` 中留空参数在当前版本下的处理方式及预期目标；在工作副本中补充所需参数、完成/错误检查和有效网卡检查。保留原件。
6. 离线编译，记录完整错误和警告。编译通过仅说明该工程配置下可编译，不证明目标 CE 镜像或硬件兼容。
7. 在获准的隔离测试设备上，确认恢复方案及设备安全状态，再安排下载与运行。以源码确认的接口操作，不强制未知变量、不将未知调用直接加入生产循环。
8. 在获准的测试工程中，通过 `MAIN_SysInfo.btHMIGetNetInfo` 请求读取网卡信息，观察 `info_Ad_Add1/2`、`info_Ad_Sub1/2` 等变量；通过 `MAIN_SysInfo.bEnableCerDisp` 向启动功能块发出请求。触发时序、完成条件与复位方式需按匹配库接口验证。另行验证 PC 客户端与 CE 端组件匹配、连接和退出方式，测试后关闭临时远程访问。
9. 源码注释提到长期启用会占用 CPU、CE 断电重启后功能关闭；这是原作者说明，尚未在本项目验证。原代码没有明确的停止 CerDisp 调用，不应将重启当作已经验证的退出方案。

导入菜单依据：[Beckhoff — Importing a function block](https://infosys.beckhoff.com/content/1033/kl2751_kl2761/3472918155.html)。EXP 项目操作参考：[TwinCAT PLC Control — Managing Projects](https://infosys.beckhoff.com/content/1033/tcplccontrol/925286667.html)。

## 兼容性与验证记录

| 项目 | 当前状态 |
| --- | --- |
| 原始 EXP 内容及编码 | 已按原始字节归档；以 GB18030 解码可读，未转码 |
| TwinCAT 2 开发环境与 Runtime 版本 | 未提供，待验证 |
| Windows CE 版本、设备镜像 | 未提供，待验证 |
| CX8090 | 用户于 2026-10-23 反馈已测试；软件版本及具体测试范围待补充 |
| CX5120-0111 | 用户于 2026-10-23 反馈已测试；软件版本及具体测试范围待补充 |
| 其他控制器型号及 CPU 架构 | 未提供，待验证 |
| TcUtilities.lib 版本及间接依赖 | 需加载；具体版本及 SystemInfo 等依赖待编译确认 |
| CerDisp 组件、路径、参数及客户端 | 源码路径为 `\windows\CerDisp.exe`；设备组件和客户端未验证 |
| 离线导入、编译 | 归档维护者未执行；用户测试时使用的工程和编译环境待补充 |
| 启动、IP 获取、显示连接及异常处理 | 用户反馈上述两款设备已测试；逐项结果待补充。原件缺少显式完成/错误检查 |
| 重复触发、停止、重启后的行为 | 未验证 |
| TwinCAT 3、其他 Windows 版本 | 未验证，不作兼容承诺 |

验证时填写：日期、测试人、硬件型号、CPU、系统镜像、开发环境与 Runtime 版本、库版本、原文件校验值、步骤、预期/实际结果、日志、恢复结果。不得把计划或官方支持列表写成本项目实测结论。

## 安全注意事项

**设备安全：** PLC 下载、任务调整及远程操作可能影响现场设备。先做工程和配置备份，在隔离环境验证，生产操作按现场维护流程进行。远程显示或诊断工具不能替代安全联锁、急停和现场确认。

**网络安全：** 若源码或组件启用远程访问，应先核实认证、加密及访问控制能力；未验证前不得假定安全。限制在获准的维护网络，不直接暴露到互联网，不擅自放开防火墙或端口。敏感 IP、AMS Net ID、账户和客户信息按最小必要原则记录，凭据不入库。测试后关闭临时访问。

**原件保护：** 原始 EXP 保持逐字节不变。任何改动另存为工作副本并记录差异。不要把私有仓库状态理解为可以自由分发第三方库或客户资料的授权。

## 项目状态与下一步

2026-10-23：本项目已归档到公开仓库 `vollmer2012/beckhoff-toolbox`。

- 已归档用户提供的原始 `开启CEROMET查看IP.EXP` 并记录校验值，使用说明明确“需加载TcUtilities.lib库”。
- 已完成源码阅读，记录实际变量、路径、留空参数及外部符号依赖。
- 2026-10-23：用户反馈已在 CX8090、CX5120-0111 上测试；此记录依据用户反馈，归档维护者未独立复测。
- 待补充：每款设备的 TwinCAT 开发环境与 Runtime 版本、操作系统/镜像、TcUtilities.lib 版本、测试范围及结果；如需复测，在隔离环境执行。
- 当前为原件和文档归档，尚未创建带完整环境记录的验证版本或 Release。


## 反馈与参与修改

本项目属于公开仓库 `vollmer2012/beckhoff-toolbox`。

- [反馈问题、建议或补充兼容性记录](https://github.com/vollmer2012/beckhoff-toolbox/issues)。
- [提交和查看修改建议](https://github.com/vollmer2012/beckhoff-toolbox/pulls)。

欢迎 Fork 后修改并提交 Pull Request，由维护者审核并决定是否合并。报告问题时请注明设备型号、TwinCAT 与操作系统版本、TcUtilities.lib 版本、操作步骤、预期与实际结果，并移除凭据和客户敏感信息。详细约定见 [CONTRIBUTING.md](../../CONTRIBUTING.md)。

原始 EXP 保留为归档基准；如有改进，请另存到本项目的 `src/` 并说明修改内容和验证情况。仓库不附带 Beckhoff 库文件。本次发布准备未新增许可证授权；公开访问及提交 PR 不自动授予额外的使用或分发许可。

