const { test } = require('node:test');
const assert = require('node:assert/strict');
const catalog = require('../data/catalog.json');
const harvested = require('../research/el3xxx-verified-fields-2026-11-23.json');
const { search } = require('../src/search.js');

const product = id => catalog.products.find(item => item.id === id);

test('EL3xxx 官网总览的 117 个具体型号逐一绑定对应产品页证据', () => {
  assert.equal(harvested.links.length, 117);
  assert.equal(new Set(harvested.links.map(link => link.id)).size, 117);
  for (const link of harvested.links) {
    const item = product(link.id);
    assert.ok(item, link.id);
    assert.equal(item.family, 'EL3xxx');
    assert.ok(item.evidence.some(ev => catalog.sources.find(source => source.id === ev.source_id)?.url === link.url), link.id);
  }
});

test('EL3xxx 区分模拟信号、温度、测量桥、功率测量和产品公告', () => {
  assert.equal(product('EL3062').specs['分辨率'], '12 bit (16 bit presentation incl. sign)');
  assert.equal(product('EL3184').specs['协议'], 'HART (version 7)');
  assert.ok(product('EL3184').aliases.includes('HART'));
  assert.ok(product('EL3202').aliases.includes('RTD'));
  assert.ok(product('EL3314').aliases.includes('热电偶'));
  assert.ok(product('EL3356').aliases.includes('应变片'));
  assert.ok(product('EL3443').aliases.includes('功率测量'));
  assert.match(product('EL3475-0200').lifecycle, /产品公告/);
});

test('EL3xxx、完整后缀与中文测量类型可以组合查询', () => {
  assert.equal(search(catalog, { q: 'EL3xxx' }).length, 117);
  assert.equal(search(catalog, { q: 'EL3174-0032' })[0].id, 'EL3174-0032');
  assert.ok(search(catalog, { q: 'EL3xxx 热电偶 24 bit' }).some(item => item.id === 'EL3314-0010'));
  assert.ok(search(catalog, { q: 'EL3xxx 功率测量 480 V' }).some(item => item.id === 'EL3443'));
});
