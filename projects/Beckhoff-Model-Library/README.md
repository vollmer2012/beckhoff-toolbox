# 倍福型号资料库

**查阅预览版，非倍福官方网站。** 资料持续补充与修订，可能存在遗漏或错误；采购、替换及现场使用前，请核对完整订货号、适用版本和官方文件，必要时向倍福确认。官网核对不等于实机验证，未收录不表示不存在或不兼容。

[提交资料纠错](https://github.com/vollmer2012/beckhoff-toolbox/issues/new?title=型号资料纠错)时，请附型号、错误字段、官方来源及版本或截图。修订可在 GitHub 文件历史中追溯。

可维护的官方选型资料索引：型号与中文关键词搜索、分类/主机筛选、规格简介、配套条件、官方来源及资料差异。

**在线查询：[倍福型号资料库](https://vollmer2012.github.io/beckhoff-toolbox/)**。

本批更新时间：2026-11-19 19:46:18（北京时间，Asia/Shanghai）；[更新记录](docs/更新记录.md) · [EL1xxx 清单](docs/EL1xxx收录清单.md)。

**打开查询网页：[dist/index.html](dist/index.html)**。本地浏览器可直接打开该文件；GitHub 页面中点击 HTML 文件只会显示源码，独立网页需要托管。

- [Markdown 型号快查](docs/型号快查.md)
- [控制器收录清单](docs/控制器收录清单.md)
- [维护指南](docs/维护指南.md)
- [唯一维护数据](data/catalog.json)
- [实施方案](docs/实施方案.md)

当前：44 个 CX 控制器基础型号；EL1xxx 专题 62 条（含 1 项配置模式）；总计 268 条型号或配置、124 个官方来源、114 条有来源的配套关系、3 条差异/冲突记录。核查日期 2026-11-19。包含型号模式，非完整产品目录；来源核查不等于实机验证或实时库存。

```powershell
npm test
npm run build
npm start
```

无需安装依赖。Node.js 20+；直接打开已生成的网页则不需要 Node。`npm start` 只启动本机回环预览。

公开仓库目录：`vollmer2012/beckhoff-toolbox/projects/Beckhoff-Model-Library/`。网页发布配置与启用步骤见 [网页发布说明](docs/网页发布说明.md)。仓库同步与网页部署分别验收；仅上传文件不代表网站已上线。
