'use strict';
const fs = require('fs');
const crypto = require('crypto');

function catalogHash(filePath) {
  return crypto.createHash('sha256').update(fs.readFileSync(filePath)).digest('hex');
}
function loadEntries(filePath) {
  const data = JSON.parse(fs.readFileSync(filePath, 'utf8'));
  if (!data || !Array.isArray(data.entries)) throw new Error('data/changelog.json 缺少 entries');
  return data.entries;
}
function formatWhen(at) {
  return new Intl.DateTimeFormat('sv-SE', {
    timeZone: 'Asia/Shanghai',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23'
  }).format(new Date(at));
}
function validateEntries(entries, catalog, hash) {
  const errors = [];
  if (!entries.length) errors.push('更新记录为空');
  let previous = Infinity;
  entries.forEach((entry, index) => {
    const label = '第 ' + (index + 1) + ' 条';
    if (!entry.at || !entry.title || !entry.summary || typeof entry.records !== 'number') errors.push(label + ' 缺少时间、标题、说明或记录数');
    if (!Number.isFinite(Date.parse(entry.at)) || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\+08:00$/.test(entry.at)) errors.push(label + ' 的时间须是带 +08:00 的北京时间');
    const time = Date.parse(entry.at);
    if (time > previous) errors.push('更新记录须按时间从新到旧排列');
    previous = time;
    if (!Array.isArray(entry.links) || !entry.links.length || entry.links.some(link => !link.label || !/^https:\/\//.test(link.url || ''))) errors.push(label + ' 缺少可打开的链接');
  });
  const newest = entries[0];
  if (newest && newest.records !== catalog.products.length) errors.push('最新一条的 records 与当前目录条数不一致');
  if (newest && newest.sources != null && newest.sources !== catalog.sources.length) errors.push('最新一条的 sources 与当前来源数不一致');
  if (newest && newest.catalog_sha256 !== hash) errors.push('目录已变化。请在 data/changelog.json 最上方新增一条，并把 catalog_sha256 设为当前 data/catalog.json 的 SHA256。');
  return errors;
}
function linkLine(entry) {
  return entry.links.map(link => `[${link.label}](${link.url})`).join(' · ');
}
function publicMarkdown(entries) {
  const lines = ['# 更新记录', '', '时间均为北京时间（Asia/Shanghai）。每条对应一次已合并的修改，记录当时的目录条数。资料仍可能有遗漏；官网核对不等于实机验证。', ''];
  for (const entry of entries) {
    lines.push(`## ${formatWhen(entry.at)} — ${entry.title}`, '', entry.summary, '', `当时目录：${entry.records} 条${entry.sources != null ? '、' + entry.sources + ' 个来源' : ''}。`, '', linkLine(entry), '');
  }
  return lines.join('\n');
}
function maintainerMarkdown(entries) {
  const lines = [
    '# 维护记录',
    '',
    '给维护者。网页上的「更新记录」和本文件都由 `data/changelog.json` 生成，请勿直接改这两个 Markdown。',
    '',
    '## 以后每次改目录',
    '',
    '1. 修改 `data/catalog.json` 后，在 `data/changelog.json` 的 `entries` 最上方新增一条。',
    '2. `at` 用合并或提交当时的北京时间，格式 `YYYY-MM-DDTHH:mm:ss+08:00`。',
    '3. `title` 和 `summary` 用白话写清改了什么。`records` 填改完后的 `products.length`，`sources` 填来源数。',
    '4. `links` 至少放一个 Pull Request 或提交链接。',
    '5. `catalog_sha256` 填当前 `data/catalog.json` 文件的 SHA256。可在项目目录运行：',
    '',
    '```powershell',
    'node -e "const fs=require(\'fs\');const c=require(\'crypto\');console.log(c.createHash(\'sha256\').update(fs.readFileSync(\'data/catalog.json\')).digest(\'hex\'))"',
    '```',
    '',
    '6. 运行 `npm test` 和 `npm run build`。测试会在目录变化后、最新一条的哈希还对不上时失败。',
    '',
    '只改检索或页面、不动 `catalog.json` 时，不必新开一条。若仍想留下说明，可以新增一条，但 `records` 和 `catalog_sha256` 必须与当时的目录一致。',
    '',
    '以下与网页上的更新记录相同。',
    ''
  ];
  for (const entry of entries) {
    lines.push(`## ${formatWhen(entry.at)} — ${entry.title}`, '', entry.summary, '', `当时目录：${entry.records} 条${entry.sources != null ? '、' + entry.sources + ' 个来源' : ''}。`, '');
    if (entry.catalog_sha256) lines.push('目录 SHA256：`' + entry.catalog_sha256 + '`', '');
    lines.push(linkLine(entry), '');
  }
  return lines.join('\n');
}
module.exports = { catalogHash, loadEntries, formatWhen, validateEntries, publicMarkdown, maintainerMarkdown };
