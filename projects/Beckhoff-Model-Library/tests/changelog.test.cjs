const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const catalog = require('../data/catalog.json');
const { catalogHash, loadEntries, validateEntries, publicMarkdown, maintainerMarkdown } = require('../src/changelog.cjs');

const root = path.join(__dirname, '..');
const entries = loadEntries(path.join(root, 'data/changelog.json'));
const hash = catalogHash(path.join(root, 'data/catalog.json'));

test('更新记录按北京时间从新到旧，且最新一条对应当前目录', () => {
  assert.deepEqual(validateEntries(entries, catalog, hash), []);
  assert.equal(entries[0].records, catalog.products.length);
  assert.equal(entries[0].catalog_sha256, hash);
  assert.ok(entries.some(entry => entry.links.some(link => link.url.endsWith('/pull/1'))));
  assert.ok(entries.some(entry => entry.links.some(link => link.url.endsWith('/pull/2'))));
});

test('网页和维护记录由同一份更新数据生成', () => {
  const visitor = fs.readFileSync(path.join(root, 'docs/更新记录.md'), 'utf8');
  const maintainer = fs.readFileSync(path.join(root, 'docs/维护记录.md'), 'utf8');
  assert.equal(visitor, publicMarkdown(entries));
  assert.equal(maintainer, maintainerMarkdown(entries));
  for (const entry of entries) {
    assert.match(visitor, new RegExp(entry.title));
    assert.match(maintainer, new RegExp(entry.title));
  }
});
