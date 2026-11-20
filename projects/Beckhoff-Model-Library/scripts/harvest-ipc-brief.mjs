// 从倍福官网 sitemap 收录 C / CX / CP 产品页的简录。只写入页面标题和读到的产品状态，不补写未出现的规格。
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as sleep } from 'node:timers/promises';

const root = fileURLToPath(new URL('../', import.meta.url));
const catalogPath = path.join(root, 'data/catalog.json');
const sitemapPath = process.env.SITEMAP || '/tmp/beckhoff/en-en-sitemap.xml';
const checkedOn = '2026-11-20';
const ua = 'Mozilla/5.0 (compatible; beckhoff-toolbox-catalog/0.1; +https://github.com/vollmer2012/beckhoff-toolbox)';

function canonPart(part) {
  const upper = part.toUpperCase();
  const matched = upper.match(/^([A-Z]+)(.*)$/);
  if (!matched) return upper.replace(/X/g, 'x');
  return matched[1] + matched[2].replace(/X/g, 'x');
}
function canonId(stem) {
  return stem.split('-').map(canonPart).join('-');
}
function familyOf(id) {
  const head = id.split('-')[0];
  if (head.includes('x')) return head;
  const matched = head.match(/^(CPX|CX|CP|C)(\d{2})/i);
  if (!matched) return head;
  const prefix = matched[1].toUpperCase();
  if (prefix === 'CX') return prefix + matched[2] + '00';
  return prefix + matched[2] + 'xx';
}
function linesOf(html) {
  const clean = html.replace(/<script[\s\S]*?<\/script>/gi, ' ').replace(/<style[\s\S]*?<\/style>/gi, ' ');
  return clean.replace(/<[^>]+>/g, '\n').split('\n').map(line => line.replace(/&nbsp;/g, ' ').replace(/&\w+;/g, ' ').replace(/\s+/g, ' ').trim()).filter(Boolean);
}
function titleOf(html) {
  const matched = html.match(/<title>([\s\S]*?)<\/title>/i);
  return matched ? matched[1].replace(/\s+/g, ' ').trim() : '';
}
function nameFromTitle(title) {
  const parts = title.split('|').map(part => part.trim()).filter(Boolean);
  if (parts.length >= 2 && !/倍福|beckhoff/i.test(parts[1])) return parts[1];
  return parts[0] || '';
}
function rawStatus(lines) {
  const labels = ['产品状态:', '产品状态：', 'Product status:', 'Product status：'];
  for (let i = 0; i < lines.length; i++) {
    for (const label of labels) {
      if (lines[i] === label || lines[i].startsWith(label)) {
        const inline = lines[i].slice(label.length).trim();
        const candidate = inline || lines[i + 1] || '';
        if (!candidate || candidate.length > 80) return '';
        if (/^(cpx|cx|cp|c)\d/i.test(candidate)) return '';
        return candidate;
      }
    }
  }
  return '';
}
function lifecycleOf(status, title) {
  const blob = `${status} ${title}`.toLowerCase();
  if (/last time buy|最后订购/.test(blob)) return '最后订购阶段（last time buy，页面查阅时）';
  if (/not recommended|不推荐/.test(blob)) return '正常供货，不推荐新项目';
  if (/service phase|服务阶段/.test(blob)) return '服务阶段';
  if (/product announcement|产品公告|产品发布/.test(blob)) return '产品公告';
  if (/regular delivery|in series production|正常供货|正常供应|常规供货|常规交付/.test(blob)) return '正常供货（页面查阅时）';
  return '未核实供货状态';
}
function categoryOf(url, title) {
  const blob = `${url} ${title}`.toLowerCase();
  if (url.includes('/control-panels/') || /control panel|控制面板/.test(blob)) return '控制面板';
  if (url.includes('/panel-pcs/') || /panel pc|面板型 pc|面板式电脑/.test(blob)) return '面板式电脑';
  return '主机硬件';
}
function aliasesOf(id, category) {
  if (category === '控制面板') return ['控制面板', 'Control Panel'];
  if (category === '面板式电脑') return ['面板式电脑', '面板型 PC', 'Panel PC'];
  if (id.startsWith('CX')) return ['嵌入式控制器', '嵌入式 PC', 'Embedded PC'];
  if (/^C\d/.test(id)) return ['工控机', '工业 PC', 'IPC'];
  return [];
}
function kindOf(id) {
  if (id.split('-')[0].includes('x')) return '型号模式';
  if (id.includes('-')) return '完整订货号';
  return '基础型号';
}
function sourceIdFor(id) {
  return 'ipc-' + id.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}
function tooBroad(id) {
  const head = id.split('-')[0];
  const matched = head.match(/^([A-Z]+)(.*)$/);
  if (!matched || !matched[2].includes('x')) return false;
  return matched[2].replace(/x/g, '').replace(/\D/g, '').length < 2;
}

async function fetchText(url) {
  let lastError;
  for (let attempt = 1; attempt <= 4; attempt++) {
    try {
      const response = await fetch(url, { headers: { 'user-agent': ua, accept: 'text/html' }, redirect: 'follow' });
      if (response.status === 404) return null;
      if (response.status === 429 || response.status >= 500) throw new Error('HTTP ' + response.status);
      if (!response.ok) throw new Error('HTTP ' + response.status);
      return { finalUrl: response.url, html: await response.text() };
    } catch (error) {
      lastError = error;
      await sleep(400 * attempt);
    }
  }
  throw lastError;
}

const sitemap = await readFile(sitemapPath, 'utf8');
const locs = [...sitemap.matchAll(/<loc>(.*?)<\/loc>/g)].map(match => match[1]);
const candidates = [];
for (const url of locs) {
  if (!url.endsWith('.html')) continue;
  if (!url.includes('/products/ipc/') && !url.includes('/support/service-products/ipc-service-products/')) continue;
  const stem = decodeURIComponent(url.slice(url.lastIndexOf('/') + 1, -5));
  if (!/^(cpx|cx|cp|c)\d/i.test(stem)) continue;
  if (/^c9900/i.test(stem)) continue;
  const id = canonId(stem);
  if (tooBroad(id)) continue;
  candidates.push({ id, enUrl: url, service: url.includes('/service-products/') });
}
const byId = new Map();
for (const item of candidates) {
  const current = byId.get(item.id);
  if (!current || (current.service && !item.service)) byId.set(item.id, item);
}
const queue = [...byId.values()];
console.log(`Candidate pages: ${queue.length}`);

const catalog = JSON.parse(await readFile(catalogPath, 'utf8'));
const existingIds = new Set(catalog.products.map(product => product.id));
const existingSourceIds = new Set(catalog.sources.map(source => source.id));
const linkedFiles = new Map();
for (const product of catalog.products) {
  const files = new Set();
  for (const sourceId of product.source_ids || []) {
    const source = catalog.sources.find(item => item.id === sourceId);
    if (!source) continue;
    try { files.add(new URL(source.url).pathname.split('/').pop().toLowerCase()); } catch { /* 已有来源保持原样。 */ }
  }
  linkedFiles.set(product.id, files);
}

const concurrency = 6;
let cursor = 0;
const extracted = [];
const failures = [];
async function worker() {
  while (cursor < queue.length) {
    const item = queue[cursor++];
    const zhUrl = item.enUrl.replace('https://www.beckhoff.com/en-en/', 'https://www.beckhoff.com.cn/zh-cn/');
    try {
      const head = item.id.split('-')[0].toLowerCase().replace(/x/g, '');
      let page = null;
      let locale = 'zh-cn';
      let usedUrl = zhUrl;
      try { page = await fetchText(zhUrl); } catch { page = null; }
      if (!page || !titleOf(page.html).toLowerCase().includes(head)) {
        page = await fetchText(item.enUrl);
        locale = 'en-en';
        usedUrl = item.enUrl;
      }
      if (!page) {
        failures.push({ id: item.id, reason: '页面不存在' });
        continue;
      }
      const title = titleOf(page.html);
      const name = nameFromTitle(title);
      if (!name) {
        failures.push({ id: item.id, reason: '没有标题' });
        continue;
      }
      const status = rawStatus(linesOf(page.html));
      extracted.push({
        id: item.id,
        name,
        title,
        status,
        lifecycle: lifecycleOf(status, title),
        category: categoryOf(page.finalUrl || usedUrl, name),
        url: page.finalUrl || usedUrl,
        locale,
        service: item.service
      });
      if (extracted.length % 40 === 0) console.log(`fetched ${extracted.length}/${queue.length}`);
    } catch (error) {
      failures.push({ id: item.id, reason: String(error.message || error) });
    }
  }
}
await Promise.all(Array.from({ length: concurrency }, worker));
console.log(`Fetched ${extracted.length}, failed ${failures.length}`);

const added = [];
const linked = [];
for (const item of extracted.sort((a, b) => a.id.localeCompare(b.id, 'en', { numeric: true }))) {
  if (existingIds.has(item.id)) {
    const file = new URL(item.url).pathname.split('/').pop().toLowerCase();
    if (!linkedFiles.get(item.id)?.has(file)) linked.push(item);
    continue;
  }
  const sourceId = sourceIdFor(item.id);
  if (existingSourceIds.has(sourceId)) {
    failures.push({ id: item.id, reason: '来源编号冲突 ' + sourceId });
    continue;
  }
  existingSourceIds.add(sourceId);
  existingIds.add(item.id);
  const specs = { 官网名称: item.name };
  if (item.status) specs['页面状态'] = item.status;
  const product = {
    id: item.id,
    name: item.name,
    category: item.category,
    subcategory: item.id.startsWith('CX') ? '嵌入式控制器' : item.category === '主机硬件' ? '工控机' : item.category,
    family: familyOf(item.id),
    kind: kindOf(item.id),
    summary: item.name,
    specs,
    lifecycle: item.lifecycle,
    review_state: '简录待补',
    notes: ['简录：只根据官网产品页建立检索条目，规格未逐项摘录。详情以官方页面为准。'],
    aliases: aliasesOf(item.id, item.category),
    source_ids: [sourceId],
    checked_on: checkedOn,
    evidence: [{
      source_id: sourceId,
      section: '页面标题' + (item.status ? '；产品状态' : ''),
      observation: `简录。官网页面标题为“${item.title}”。${item.status ? '页面状态原文：' + item.status + '。' : '标题区未读到单独的产品状态。'}规格未逐项摘录。`,
      claims: { ...specs }
    }]
  };
  catalog.sources.push({
    id: sourceId,
    title: item.id + ' 官方产品页',
    url: item.url,
    section: '页面标题' + (item.status ? '；产品状态' : ''),
    locale: item.locale === 'zh-cn' ? 'zh-cn（产品页）' : 'en-en（产品正文英文）',
    kind: '产品选型页',
    checked_on: checkedOn,
    revision: null,
    published_on: null,
    archived_file: null,
    sha256: null
  });
  catalog.products.push(product);
  added.push(product);
}

const present = new Set(catalog.products.map(product => product.id));
for (const product of added) {
  const base = product.id.match(/^(.*)-\d{4}$/);
  if (base && present.has(base[1]) && base[1] !== product.id) product.specs['基础型号'] = base[1];
  const evidence = product.evidence[0];
  evidence.claims = { ...product.specs };
}

for (const item of linked) {
  const product = catalog.products.find(entry => entry.id === item.id);
  const sourceId = sourceIdFor(item.id);
  if (!product || existingSourceIds.has(sourceId) || product.review_state === '简录待补') continue;
  existingSourceIds.add(sourceId);
  catalog.sources.push({
    id: sourceId,
    title: item.id + ' 官方产品页',
    url: item.url,
    section: '页面标题',
    locale: item.locale === 'zh-cn' ? 'zh-cn（产品页）' : 'en-en（产品正文英文）',
    kind: '产品选型页',
    checked_on: checkedOn,
    revision: null,
    published_on: null,
    archived_file: null,
    sha256: null
  });
  product.source_ids.push(sourceId);
  product.evidence.push({
    source_id: sourceId,
    section: '页面标题',
    observation: `补充官方产品页链接。页面标题为“${item.title}”。不改动已核对规格。`,
    claims: { 官网页面: item.name }
  });
}

catalog.updated_on = checkedOn;
catalog.updated_at = '2026-11-20T09:40:00+08:00';
if (!catalog.backlog.some(item => item.topic === 'C / CX / CP 简录')) {
  catalog.backlog.push({
    topic: 'C / CX / CP 简录',
    reason: '2026-11-20 从官网产品页补充工控机、嵌入式控制器、控制面板和面板式电脑的可检索条目。简录只保留型号、官网名称、系列和产品页链接；CPU、屏幕尺寸等规格未逐项摘录，采购前须打开官方页面核对。'
  });
}

await mkdir(path.join(root, 'research'), { recursive: true });
const summary = {
  checked_on: checkedOn,
  candidate_pages: queue.length,
  added: added.length,
  linked_existing: linked.filter(item => catalog.products.find(product => product.id === item.id)?.source_ids.includes(sourceIdFor(item.id))).length,
  failed: failures,
  by_group: {}
};
for (const product of added) {
  const group = product.id.startsWith('CPX') ? 'CPX' : product.id.startsWith('CP') ? 'CP' : product.id.startsWith('CX') ? 'CX' : 'C';
  summary.by_group[group] = (summary.by_group[group] || 0) + 1;
}
await writeFile(path.join(root, 'research/ipc-brief-2026-11-20.json'), JSON.stringify({ summary, records: added.map(product => ({ id: product.id, name: product.name, category: product.category, family: product.family, lifecycle: product.lifecycle, url: catalog.sources.find(source => source.id === product.source_ids[0]).url })) }, null, 2) + '\n');
await writeFile(catalogPath, JSON.stringify(catalog, null, 2) + '\n');
console.log(JSON.stringify(summary, null, 2));
