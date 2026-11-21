# Boot Scanner · TwinCAT Boot 查看器

独立第三方 Windows 工具，用于本地、离线、只读查看 TwinCAT Boot 配置中的可读信息。

**当前版本：1.1.2 · 公开预览。** 本项目不是 Beckhoff 官方产品，不声明官方认证、授权或背书。解析支持不完整，结果仅供参考，生产操作前请用官方工具核实。详见 [免责声明](docs/DISCLAIMER.md)。

Independent third-party Windows viewer for locally available TwinCAT Boot metadata. Offline and read-only. Partial format support; not a complete engineering-project recovery tool or an official Beckhoff product.

## 下载与运行

**[下载单 EXE](https://github.com/vollmer2012/beckhoff-toolbox/releases/download/twincat-boot-scanner-v1.1.2/BootScanner.exe)** · [下载完整 ZIP](https://github.com/vollmer2012/beckhoff-toolbox/releases/download/twincat-boot-scanner-v1.1.2/BootScanner-1.1.2-win-x64.zip) · [版本页](https://github.com/vollmer2012/beckhoff-toolbox/releases/tag/twincat-boot-scanner-v1.1.2)

MIT 许可只适用于本项目目录中有权按该协议授权的代码，不改变 toolbox 其他项目的授权。

在 [Releases](https://github.com/vollmer2012/beckhoff-toolbox/releases/tag/twincat-boot-scanner-v1.1.2) 下载 Windows x64 发布包，解压后运行 `BootScanner.exe`。请保留随包的许可证和说明文件。

- Windows 10/11 x64；自包含 EXE，无需另装 .NET。
- 不需要 TwinCAT、ADS DLL 或 Visual Studio。
- 打开或拖入有权访问的 Boot 目录；支持路径框输入后回车。
- 硬件/系统树、符号表和逐文件状态；可搜索、取消扫描、导出 CSV。
- 界面底部固定显示风险提示，可打开完整免责声明。

本项目目录与发布包不包含 Boot 项目例子、工程夹具文件、客户数据或现场截图。

## 技术栈

| 层次 | 技术 | 用途 |
| --- | --- | --- |
| 语言与运行时 | C# / .NET 8 | 核心逻辑、模型与桌面应用 |
| UI | Windows Forms | 拓扑树、符号表、搜索、文件诊断、免责声明 |
| TC2 容器 | Windows Structured Storage API / COM / P/Invoke | 只读打开 OLE 复合文档及已知配置流 |
| XML | System.Xml.Linq / XmlReader | 配置、TMC/TMI、TPY 的明确字段 |
| 编码 | System.Text.Encoding.CodePages | 历史 XML 编码兼容 |
| 导出 | UTF-8 BOM CSV | 变量/过程数据与带来源的拓扑表 |
| 测试 | xUnit / Microsoft.NET.Test.Sdk | 最小内存输入与临时生成数据的单元测试 |
| 发布 | dotnet publish / win-x64 / self-contained / single-file | 无需预装运行时的 EXE |

公开版不依赖或分发 OpenMcdf，复合文档读取使用 Windows 系统接口。系统接口只解决“打开容器”，不代表完整解码其中的 WSM 工程对象。

## 支持范围

| 文件/结构 | 支持 | 限制 |
| --- | --- | --- |
| TcSmProject / Io / System | 明确设备、系统、任务、实例字段及显式 Link | 无明确链接不推断映射 |
| TcBootProject / ProjectInfo | 文件中实际列出的 Devices、Boxes、地址和工程引用 | 不推断物理布线，不将数字类型代码冒充订货号 |
| TSM/WSM OLE 的 SlaveDeviceInfo | 已知长度前缀 UTF-16 XML、独立从站描述/过程数据 | 部分解析，不是完整 PLC 符号表 |
| TSM 的 WSM 主流 | 有限的长度匹配 ASCII 文件路径字符串候选 | 工程对象与完整映射未解码；候选不证明关联 |
| TMC / TMI / TPY | 明确暴露的符号信息 | 不反编译程序体、不恢复 ST 源码 |
| 启动/保持二进制及未知类型 | 识别/跳过/未处理说明 | 不猜测内容 |

PLC 工程名只在 XML 明确记录时显示；历史 TPY 文件名不当作确认的工程名。路径仅展示，不访问目标，UNC、Unicode 或未知 WSM 长字符串形式不会猜测提取。

缺少可选 TPY 显示“暂不可用”，不等于工程损坏。Context 周期保留原始值，并标记换算单位待核。

## 只读与数据保护

- 不连接控制器、不登录、不下载、不写回 Boot、不修改启动配置。
- 跳过文件/目录 reparse points；导出禁止写入扫描的 Boot 内。
- CSV 采用替换目录项写入，避免截断硬链接目标，并对公式式文本添加保护。
- XML 最大64MiB、嵌套256层；容器最大256MiB，受支持 XML 流8MiB、WSM候选读取16MiB。
- 本地处理，不主动上传工程。历史路径、变量名、CSV、日志和截图仍可能含客户信息，分享前必须自行检查脱敏。
- 一次有界文件解析内部不能立即中断，取消在阶段与符号文件之间生效。

## 构建与测试

开发需要 Windows x64 与 .NET 8 SDK。

```powershell
dotnet restore BootScanner.sln
dotnet test tests/BootScanner.Tests/BootScanner.Tests.csproj -c Release
dotnet build BootScanner.sln -c Release
.\build-windows.ps1 -NoReadyToRun
```

输出：`publish/win-x64/BootScanner.exe`。默认不裁剪 WinForms 运行时。公开测试不依赖随仓库分发的 Boot 工程资产。

## 文档

- [使用指南](docs/USAGE.md)
- [架构、技术栈与数据流](docs/ARCHITECTURE.md)
- [支持矩阵与已知限制](docs/SUPPORT.md)
- [免责声明、商标与法律边界](docs/DISCLAIMER.md)
- [版本记录](CHANGELOG.md)
- [贡献规范](CONTRIBUTING.md) / [安全与隐私报告](SECURITY.md)

## 许可与商标

本项目代码采用 [MIT License](LICENSE)，.NET 运行组件保留其各自声明，见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) 与 `licenses/`。

Beckhoff、TwinCAT、EtherCAT 等名称与商标归各自权利人所有，仅用于说明兼容对象。项目名称和 MIT 许可不表示获得厂商商标使用权、官方背书或客户工程授权。免责说明不能保证不存在法律纠纷，也不排除适用法律不得排除的责任。
