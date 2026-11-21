# 架构与技术栈

## 项目边界

`BootScanner.Core`（net8.0）负责安全发现、版本证据、配置读取、模型和CSV。
`BootScanner.App`（net8.0-windows / WinForms）负责本地交互、后台扫描、取消、搜索、展示与免责声明。
`BootScanner.Tests`（xUnit）使用最小内存输入和运行时临时生成数据，不附带Boot项目资产。

## 数据流

用户选择目录 → 只读文件发现 → 版本证据检测 → 各格式解析 → TopologyNode / SymbolEntry / ParseStatusEntry → 窗口与CSV。

每个恢复内容尽量保留相对来源文件、XML位置或流偏移；未验证路径候选与明确工程字段分别标识。只有显式Link才可构建PLC/I/O关联。

## 容器层

CompoundStorage使用Windows `StgOpenStorageEx`、`IStorage`、`IStream` 读取OLE复合文档。根存储以 `STGM_READ | STGM_SHARE_DENY_WRITE` 打开，不请求写权限。适配器释放COM对象并平衡本线程的COM初始化。非Windows环境不解析TSM容器。

容器层不会解释WSM工程语义。TsmProjectParser只支持已确认形态的SlaveDeviceInfo描述流；WsmFileReferences仅提取有长度证据的有限文件路径候选，且该可选流错误不遮蔽其它有效流。

## XML层

XmlReader禁止DTD及外部解析器；限制输入大小和树深度。CurrentConfigParser处理工程与运行时两种XML结构；TmcTmiParser和TpyParser读取明确符号信息。

WindowsOS COM接口解决容器读入问题，不保证任何私有配置结构都已被解码。未实现字段和未知格式须保留诊断，不猜数值、型号或映射。

## UI与并发

WinForms在后台Task中执行扫描，CancellationToken在阶段和每个符号文件间检查；请求编号防止旧结果覆盖新路径。关闭窗口时取消未完成任务。

免责声明固定显示在窗口底部；全文通过可滚动对话框阅读。它是风险提示，不是一个假设可以排除一切责任的点击协议。

## 依赖

运行依赖为Microsoft .NET 8及其编码组件；公开源码和运行包不含OpenMcdf、TwinCAT SDK、ADS DLL或厂商安装程序。构建与测试的NuGet组件按各自许可证使用。
