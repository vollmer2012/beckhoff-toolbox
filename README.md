# beckhoff-toolbox

个人维护的 Beckhoff / TwinCAT 示例、实用工具与使用文档集合。非 Beckhoff 官方项目。

## 项目索引

| 项目 | 用途 | 状态 |
| --- | --- | --- |
| [TwinCAT2 CE CerDisp](projects/TwinCAT2-CE-CerDisp/README.md) | TwinCAT 2 程序导出：读取网卡信息并尝试启动 CE 端 CerDisp | 用户反馈已在 CX8090、CX5120-0111 上测试；软件版本及测试范围待补充 |
| [Beckhoff Finder V1.01](projects/Beckhoff-Finder/README.md) | 作者此前发布的 Windows 工具运行版，EXE 通过 Releases 分发 | 运行版发布准备中；具体功能与兼容环境待补充 |

各项目分别记录依赖、使用步骤、安全事项及兼容性。请先阅读对应项目 README；用户反馈的测试记录与维护者独立复测结果分别标明。

## 目录约定

```text
projects/
  TwinCAT2-CE-CerDisp/
    README.md
    original/
      README.md
      开启CEROMET查看IP.EXP
  Beckhoff-Finder/
    README.md
    SHA256SUMS.txt
CONTRIBUTING.md
.github/
  ISSUE_TEMPLATE/
  pull_request_template.md
```

后续公开项目放入 `projects/<项目名>/`，并在首页索引登记。原始资料放入项目的 `original/`，改进代码可放入 `src/`，额外文档可放入 `docs/`；按实际内容建立目录。

## 反馈与参与

- [提交问题、建议或兼容性记录](https://github.com/vollmer2012/beckhoff-toolbox/issues/new/choose)。
- [查看和提交 Pull Requests](https://github.com/vollmer2012/beckhoff-toolbox/pulls)。
- 修改流程：Fork → 在自己的分支修改 → 提交 PR → 维护者审核并决定是否合并。详见 [贡献指南](CONTRIBUTING.md)。

普通贡献者通过 PR 提出修改；合并由有相应权限的维护者执行。反馈请注明项目名称、代码版本、设备和软件环境，并去除凭据、客户资料与敏感网络信息。

## 使用与授权说明

本仓库公开展示资料并接受反馈与 PR。目前未添加统一开源许可证；公开访问和提交 PR 不自动授予额外的使用、修改或再分发许可。各文件仍受相应权利人的授权约束。Beckhoff 库文件不随仓库分发。

涉及 PLC 下载、远程访问或现场设备操作时，应先备份，并在获准的测试环境验证。具体限制和待验证项以各项目说明为准。
