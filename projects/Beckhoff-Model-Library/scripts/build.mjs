import { readFile, writeFile, mkdir, copyFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { validate } = require('../src/catalog.cjs');
const root = fileURLToPath(new URL('../', import.meta.url));
const data = JSON.parse(await readFile(path.join(root, 'data/catalog.json'), 'utf8'));
const errors = validate(data);
if (errors.length) { console.error(errors.join('\n')); process.exit(1); }
await mkdir(path.join(root, 'dist'), { recursive: true });
for (const file of ['index.html', 'styles.css', 'app.js']) await copyFile(path.join(root, 'site', file), path.join(root, 'dist', file));
await copyFile(path.join(root, 'src/search.js'), path.join(root, 'dist/search.js'));
await writeFile(path.join(root, 'dist/catalog.js'), 'window.MODEL_CATALOG = ' + JSON.stringify(data).replace(/</g, '\\u003c') + ';\n');
const esc = v => String(v).replaceAll('|', '\\|').replaceAll('\n', ' ');
const sourceMap = new Map(data.sources.map(s => [s.id, s]));
const links = refs => refs.map(id => `[${esc(sourceMap.get(id).title)}](${sourceMap.get(id).url})`).join('、');
const lines = ['# 倍福型号快查', '', `更新：${data.updated_on}。${data.scope}。共 ${data.products.length} 条记录，${data.sources.length} 个官方来源。`, '',
  '此文件由 `data/catalog.json` 自动生成，请勿直接维护。网页版见 `dist/index.html`。', '',
  '官网核对不等于实机兼容测试；供货状态是查阅时的页面记载，不能代替实时询价。型号模式不能直接下单。', ''];
for (const cat of [...new Set(data.products.map(p => p.category))]) {
  lines.push('## ' + cat, '', '| 型号 | 名称与简介 | 关键规格 | 产品状态 / 资料状态 | 来源 |', '| --- | --- | --- | --- | --- |');
  for (const p of data.products.filter(p => p.category === cat)) {
    lines.push(`| ${esc(p.id)} | ${esc(p.name)}；${esc(p.summary)} | ${esc(Object.entries(p.specs).map(([k,v]) => `${k}：${v}`).join('；'))} | ${esc(p.lifecycle)} / ${esc(p.review_state)} | ${links(p.source_ids)} |`);
    if (p.notes.length) lines.push(`| ${esc(p.id)} 注意 | ${esc(p.notes.join('；'))} | | | |`);
  }
  lines.push('');
}
lines.push('## 来源对应的参数观察', '', '以下按型号和来源分别列出支持的参数和值，不把多份来源混成一条证据。编辑归类及未知值在观察文字中标明。', '');
for (const p of data.products) {
  lines.push('### ' + p.id, '');
  for (const ev of p.evidence) {
    lines.push(links([ev.source_id]), '', '定位：' + ev.section, '', ev.observation, '', '| 参数 | 该来源中的值 / 整理说明 |', '| --- | --- |');
    for (const [key,value] of Object.entries(ev.claims)) lines.push(`| ${esc(key)} | ${esc(value)} |`);
    lines.push('');
  }
}
lines.push('## 配套关系', '', '关系来自指定主机页或系列手册；系列级范围不自动扩展为所有具体机型。', '', '| 主机 / 系列 | 型号 | 关系 | 条件 | 来源 |', '| --- | --- | --- | --- | --- |');
for (const r of data.relations) lines.push(`| ${esc(r.host)} | ${esc(r.item)} | ${esc(r.role)} | ${esc(r.conditions)} | ${links(r.source_ids)} |`);
lines.push('', '## 资料差异与待核实冲突', '');
for (const i of data.issues) {
  lines.push(`### ${i.id} · ${i.type} · ${i.title}`, '', i.description, '');
  for (const o of i.observations) lines.push(`- ${o.statement} ${links([o.source_id])}`);
  lines.push('', '处理：' + i.resolution, '');
}
lines.push('## 待补充范围', '');
for (const b of data.backlog) lines.push(`- **${b.topic}**：${b.reason}`);
lines.push('', '## 来源登记', '', '| 来源 | 地区 / 正文语言 | 查阅日期 | 版本 | 页面区段 |', '| --- | --- | --- | --- | --- |');
for (const s of data.sources) lines.push(`| ${links([s.id])} | ${esc(s.locale)} | ${s.checked_on} | ${esc(s.revision || '未核实')} | ${esc(s.section)} |`);
await writeFile(path.join(root, 'docs/型号快查.md'), lines.join('\n') + '\n');
await copyFile(path.join(root, 'docs/型号快查.md'), path.join(root, 'dist/型号快查.md'));
await copyFile(path.join(root, 'docs/维护指南.md'), path.join(root, 'dist/维护指南.md'));
const controllers = data.products.filter(p => p.id.startsWith('CX') && p.kind === '基础型号').sort((a,b) => a.id.localeCompare(b.id,'en',{numeric:true}));
const coverage = ['# 控制器收录清单', '', `核查日期：${data.updated_on}。当前 ${controllers.length} 个 CX 控制器基础型号；订货配置和选件不计入此数。`, '', '由 data/catalog.json 自动生成。已收录不等于全部系统与附件组合均已整理。', '', '| 基础型号 | 系列 | 处理器 | 产品状态 | 已收录订货配置数 | 官方来源 |', '| --- | --- | --- | --- | --- | --- |'];
for (const p of controllers) {
  const variants = data.products.filter(v => v.specs['基础型号'] === p.id).length;
  coverage.push(`| ${esc(p.id)} | ${esc(p.family)} | ${esc(p.specs['处理器'] || '见详情')} | ${esc(p.lifecycle)} | ${variants} | ${links(p.source_ids)} |`);
}
coverage.push('', '## 待补齐', '', ...data.backlog.filter(b => b.topic.includes('控制器')).map(b => b.reason), '', '配置数为 0 表示尚未录入，不表示厂商没有配置。', '');
await writeFile(path.join(root, 'docs/控制器收录清单.md'), coverage.join('\n'));
await copyFile(path.join(root, 'docs/控制器收录清单.md'), path.join(root, 'dist/控制器收录清单.md'));
console.log(`Validated and built: ${data.products.length} records / ${data.sources.length} sources / ${data.relations.length} relations / ${data.issues.length} issue records.`);
