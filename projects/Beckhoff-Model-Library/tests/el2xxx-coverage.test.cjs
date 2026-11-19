const { test } = require('node:test');
const assert = require('node:assert/strict');
const catalog = require('../data/catalog.json');
const harvested = require('../research/el2xxx-verified-fields-2026-11-19.json');
const { search } = require('../src/search.js');

const product = id => catalog.products.find(item => item.id === id);

test('EL2xxx 官网总览的 71 个具体型号逐一绑定对应产品页证据', () => {
  assert.equal(harvested.links.length, 71);
  assert.equal(new Set(harvested.links.map(link => link.id)).size, 71);
  for (const link of harvested.links) {
    const item = product(link.id);
    assert.ok(item, link.id);
    assert.equal(item.family, 'EL2xxx');
    assert.ok(item.evidence.some(ev => catalog.sources.find(source => source.id === ev.source_id)?.url === link.url), link.id);
    const technicalHeading = harvested.records.find(record => record.id === link.id)?.technical?.[0]?.[1] || '';
    assert.ok(technicalHeading.split(',').map(value => value.trim()).includes(link.id), `${link.id}: ${technicalHeading}`);
  }
});

test('EL2xxx 供货状态保留四个 last time buy 页面，不从系列统一继承', () => {
  const lastTimeBuy = harvested.records.filter(record => record.status === 'last time buy').map(record => record.id).sort();
  assert.deepEqual(lastTimeBuy, ['EL2022', 'EL2024', 'EL2032', 'EL2034']);
  for (const id of lastTimeBuy) assert.match(product(id).lifecycle, /最后订购|last time buy/i);
  assert.match(product('EL2008').lifecycle, /正常供货/);
});

test('EL2xxx 不混淆低侧输出、推挽、PWM、继电器及 TwinSAFE', () => {
  assert.equal(product('EL2088').specs['最大输出电流'], '0.5 A (short-circuit proof) per channel, ∑ 3 A');
  assert.ok(product('EL2088').aliases.includes('低侧输出'));
  assert.equal(product('EL2202').specs['开关时间'], 'typ. TON: < 1 µs, typ. TOFF: < 1 µs');
  assert.equal(product('EL2535').specs['PWM 载波频率'], '30 kHz');
  assert.equal(product('EL2602').specs['额定电压'], '230 V AC/30 V DC');
  assert.equal(product('EL2904').specs['安全协议'], 'TwinSAFE/Safety over EtherCAT');
  assert.equal(product('EL2911').specs['最大输出电流'], '10 A');
});

test('EL2xxx、完整后缀与输出类型可以组合查询，且不混入 ES/ED 型号', () => {
  assert.equal(search(catalog, { q: 'EL2xxx' }).length, 71);
  assert.equal(search(catalog, { q: 'EL2535-0103' })[0].id, 'EL2535-0103');
  assert.ok(search(catalog, { q: 'EL2xxx PWM 30 kHz' }).some(item => item.id === 'EL2535'));
  assert.ok(search(catalog, { q: 'EL2xxx 继电器 230 V AC' }).some(item => item.id === 'EL2602'));
  assert.ok(!catalog.products.some(item => /^ES2|^ED2/.test(item.id) && item.family === 'EL2xxx'));
});
