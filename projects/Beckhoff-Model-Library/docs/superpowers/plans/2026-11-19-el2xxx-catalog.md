# EL2xxx Catalog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将官网当前 EL2xxx 总览中的具体型号作为可追溯资料批次加入查询网页并发布。

**Architecture:** 以官网总览限定范围、具体产品页限定事实，写入现有 JSON 单一数据源；现有构建器继续生成静态网页和 Markdown。数据与搜索行为由 Node 测试保护。

**Tech Stack:** JSON、Node.js 20+、零依赖 HTML/CSS/JavaScript、GitHub Pages

**Spec:** `docs/superpowers/specs/2026-11-19-el2xxx-catalog-design.md`

## Global Constraints

- 仅收录总览明确列出的 EL2xxx 链接；不推断型号或后缀。
- 每条产品必须有对应具体页面来源、查阅日期和 evidence claims。
- 保留官网英文参数值和单位；中文只用于字段名、摘要与编辑说明。
- TwinSAFE 不构成安全设计结论；供货状态不从系列页继承。

## Review Focus

- 总览型号漏录或重复时覆盖测试失败。
- `last time buy` 被误标为正常供货时状态测试失败。
- 继电器、PWM/脉冲与普通晶体管输出混淆时关键规格测试失败。
- 同页 ES 型号或配置模式被误收为 EL 完整订货号时范围测试失败。
- EL2xxx 导入破坏既有 NPN、40G、完整型号搜索时全套测试失败。

---

### Task 1: EL2xxx 覆盖契约

**Files:** Create `tests/el2xxx-coverage.test.cjs`

- [ ] 写入总览 71 条、具体页面证据、状态差异和关键类别测试。
- [ ] 运行该测试并确认因数据尚未导入而失败。

### Task 2: 结构化导入

**Files:** Modify `data/catalog.json`; use `research/el2xxx-verified-fields-2026-11-19.json`

- [ ] 按具体型号列映射产品、来源、状态和 evidence。
- [ ] 运行 EL2xxx 测试并确认通过。

### Task 3: 清单与公开说明

**Files:** Modify `scripts/build.mjs`, `site/index.html`, `docs/更新记录.md`, `docs/工作进度.md`, `README.md`

- [ ] 生成 EL2xxx 收录清单并展示本批范围与北京时间。
- [ ] 运行全量测试和构建。

### Task 4: 发布与线上验收

**Files:** Regenerate `dist/`; synchronize public GitHub repository.

- [ ] 核对本地搜索、窄屏页面与清单链接。
- [ ] 非强制推送到 `main`，等待 GitHub Actions 成功。
- [ ] 在线核对总数、更新时间、典型型号与状态查询。

