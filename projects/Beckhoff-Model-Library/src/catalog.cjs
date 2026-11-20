'use strict';

function validDate(value) {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const date = new Date(value + 'T00:00:00Z');
  return !Number.isNaN(+date) && date.toISOString().slice(0, 10) === value;
}
function validate(c) {
  const errors = [];
  if (c.schema_version !== 1) errors.push('不支持的数据版本');
  for (const key of ['products', 'sources', 'relations', 'issues', 'backlog']) {
    if (!Array.isArray(c[key])) errors.push('缺少数组：' + key);
  }
  if (errors.length) return errors;
  const ids = new Set(), sources = new Set();
  const required = (obj, fields, label) => fields.forEach(f => {
    if (typeof obj[f] !== 'string' || !obj[f].trim()) errors.push(label + ' 缺少字段 ' + f);
  });
  for (const s of c.sources) {
    required(s, ['id', 'title', 'url', 'section', 'locale', 'kind', 'checked_on'], '来源');
    if (sources.has(s.id)) errors.push('重复来源：' + s.id);
    sources.add(s.id);
    try {
      const url = new URL(s.url);
      const host = url.hostname.toLowerCase();
      const official = host === 'beckhoff.com' || host.endsWith('.beckhoff.com') || host === 'beckhoff.com.cn' || host.endsWith('.beckhoff.com.cn');
      if (url.protocol !== 'https:' || !official) throw new Error();
    } catch { errors.push('来源必须是倍福官方 HTTPS 链接：' + s.id); }
    if (!validDate(s.checked_on)) errors.push('来源日期无效：' + s.id);
    for (const f of ['published_on']) if (s[f] != null && !validDate(s[f])) errors.push('来源日期无效：' + s.id + '/' + f);
  }
  const refs = (value, label) => {
    if (!Array.isArray(value) || !value.length) errors.push(label + ' 缺少来源');
    else value.forEach(s => { if (!sources.has(s)) errors.push(label + ' 未知来源：' + s); });
  };
  for (const p of c.products) {
    required(p, ['id', 'name', 'category', 'family', 'kind', 'summary', 'lifecycle', 'review_state', 'checked_on'], '型号');
    if (ids.has(p.id)) errors.push('重复型号：' + p.id);
    ids.add(p.id);
    refs(p.source_ids, p.id);
    if (!validDate(p.checked_on)) errors.push('型号日期无效：' + p.id);
    if (!p.specs || typeof p.specs !== 'object' || Array.isArray(p.specs) || !Object.keys(p.specs).length || Object.values(p.specs).some(v => typeof v !== 'string' || !v.trim())) errors.push('型号规格无效：' + p.id);
    if (!Array.isArray(p.notes) || !Array.isArray(p.aliases)) errors.push('型号备注或别名无效：' + p.id);
    if (!Array.isArray(p.evidence) || !p.evidence.length) errors.push('缺少参数依据：' + p.id);
    else for (const e of p.evidence) {
      if (!sources.has(e.source_id) || !p.source_ids.includes(e.source_id)) errors.push('未知参数依据：' + p.id);
      required(e, ['section', 'observation'], p.id + '依据');
      if (!e.claims || typeof e.claims !== 'object' || Array.isArray(e.claims) || !Object.keys(e.claims).length || Object.values(e.claims).some(v => typeof v !== 'string' || !v.trim())) errors.push('缺少参数观察：' + p.id);
    }
  }
  for (const p of c.products) {
    const base = p.specs?.['基础型号'];
    if (base && (!ids.has(base) || base === p.id)) errors.push('未知基础型号：' + p.id + '/' + base);
  }
  const relationKeys = new Set();
  for (const r of c.relations) {
    required(r, ['host', 'item', 'role', 'conditions'], '配套关系');
    if (!ids.has(r.item)) errors.push('未知配套型号：' + r.item);
    refs(r.source_ids, r.host + '/' + r.item);
    const key = r.host + '|' + r.item + '|' + r.role;
    if (relationKeys.has(key)) errors.push('重复配套关系：' + key);
    relationKeys.add(key);
  }
  const issueIds = new Set();
  for (const issue of c.issues) {
    required(issue, ['id', 'type', 'title', 'description', 'resolution'], '资料记录');
    if (issueIds.has(issue.id)) errors.push('重复资料记录：' + issue.id);
    issueIds.add(issue.id);
    if (!Array.isArray(issue.product_ids) || !issue.product_ids.length) errors.push('资料记录缺少型号');
    else issue.product_ids.forEach(id => { if (!ids.has(id)) errors.push('未知记录型号：' + id); });
    refs(issue.source_ids, issue.id);
    if (!Array.isArray(issue.observations) || !issue.observations.length) errors.push('资料记录缺少观察');
    else for (const o of issue.observations) {
      if (!sources.has(o.source_id) || !issue.source_ids.includes(o.source_id)) errors.push('未知观察来源：' + o.source_id);
      required(o, ['statement'], issue.id);
    }
  }
  if (!validDate(c.updated_on)) errors.push('目录日期无效');
  return errors;
}
module.exports = { validate };
