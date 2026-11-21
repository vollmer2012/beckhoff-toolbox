namespace BootScanner.Core;

/// <summary>Shared public-facing notice; it does not replace the applicable license terms.</summary>
public static class LegalNotice
{
    public const string Footer = "独立第三方工具，非 Beckhoff 官方产品；结果仅供离线参考，生产操作前请用官方工具核实。";
    public const string LinkText = "查看免责声明";
    public const string FullText = """
        Boot Scanner（Boot 查看器）使用说明与免责声明

        1. 身份与商标
        本软件是独立第三方工具，不是 Beckhoff 官方产品，未声明获得 Beckhoff 的认证、授权或背书。Beckhoff、TwinCAT、EtherCAT 等名称及商标归各自权利人所有，仅用于描述兼容对象。

        2. 用途与授权
        仅用于对您有权访问和分析的配置文件进行本地、离线、只读查看。请遵守适用法律、合同与保密义务。本软件不向您授予第三方软件、客户工程或商标的权利。

        3. 能力限制与现场安全
        文件格式支持不完整，结果可能缺失、失准或存在未验证关联；历史路径不证明文件当前存在。完整 WSM 工程与映射尚未解码，不能恢复 PLC 源码。显示结果仅供参考，不替代官方工程工具、现场核验或安全评估。请勿仅凭本工具结果实施生产变更、接线、带电操作或安全功能判断。

        4. 数据与隐私
        本软件处理本地选定的文件，不主动上传工程数据。文件名、历史路径、变量与导出内容可能含客户或个人信息；分享截图、日志及 CSV 前请检查并脱敏。公开仓库不提供 Boot 项目示例。

        5. 许可与责任边界
        本项目代码按 MIT 协议提供，运行组件的许可见 LICENSE 和 THIRD-PARTY-NOTICES.txt。TSM 容器读取使用 Windows 系统接口，不随软件分发 OpenMcdf。在适用法律允许的范围内，软件按现状提供，不作完整性、准确性或特定用途适用性的保证。依法不得排除或限制的责任不受本说明影响。本说明不构成对不存在知识产权争议或其他法律风险的保证。
        """;
}
