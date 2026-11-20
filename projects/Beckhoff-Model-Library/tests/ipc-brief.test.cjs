const { test } = require('node:test');
const assert = require('node:assert/strict');
const catalog = require('../data/catalog.json');
const { search } = require('../src/search.js');
const find = (q, filters = {}) => search(catalog, { q, ...filters }).map(p => p.id);

test('工控机、嵌入式控制器、控制面板和面板式电脑能按型号或中文类型查到', () => {
  assert.equal(find('C6015')[0], 'C6015');
  assert.ok(find('C6015').includes('C6015-0010'));
  assert.ok(find('C60').includes('C6015'));
  assert.ok(find('C60').includes('C6030'));
  assert.ok(find('CP22').includes('CP22xx'));
  assert.deepEqual(find('CP2215'), ['CP22xx']);
  assert.deepEqual(find('CX52x0').slice(0, 2), ['CX5230', 'CX5240']);
  assert.equal(find('CX9020')[0], 'CX9020');
  assert.ok(find('工控机').includes('C6015'));
  assert.ok(find('嵌入式控制器').includes('CX9020'));
  assert.ok(find('嵌入式控制器').includes('CX8200'));
  assert.ok(!find('嵌入式控制器').includes('CX2100-0004'));
  assert.ok(find('控制面板').includes('CP29xx'));
  assert.ok(find('面板式电脑').includes('CP22xx'));
});

test('简录保留官网链接和待补状态，不改写已核对的控制器规格', () => {
  const brief = catalog.products.filter(p => p.review_state === '简录待补');
  assert.ok(brief.length >= 200);
  for (const product of brief) {
    assert.equal(product.review_state, '简录待补');
    assert.ok(product.name.trim());
    assert.equal(product.evidence.length, 1);
    const source = catalog.sources.find(item => item.id === product.source_ids[0]);
    assert.ok(source, product.id);
    assert.match(source.url, /^https:\/\/(www\.)?beckhoff\.com(\.cn)?\//);
    assert.ok(!product.specs['处理器'] || product.evidence[0].claims['处理器'], product.id);
  }
  const curated = catalog.products.find(p => p.id === 'CX5130');
  assert.equal(curated.review_state, '已核对官网');
  assert.match(curated.specs['处理器'], /E3827/);
  assert.equal(catalog.products.filter(p => p.id === 'C6015').length, 1);
});
