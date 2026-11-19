const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const file = path.join(__dirname, '../src/catalog.cjs');
const validate = fs.existsSync(file) ? require(file).validate : () => [];
const catalog = JSON.parse(fs.readFileSync(path.join(__dirname, '../data/catalog.json'), 'utf8'));
const copy = () => structuredClone(catalog);

test('校验器拒绝重复型号，防止一个型号出现两套查询结论', () => {
  const c = copy(); c.products.push(c.products[0]);
  assert.ok(validate(c).some(e => e.includes('重复型号')));
});
test('校验器拒绝不存在的参数来源和配套目标', () => {
  const c = copy(); c.products[0].source_ids = ['不存在']; c.relations[0].item = 'NO-SUCH-MODEL';
  const errors = validate(c).join('\n');
  assert.match(errors, /未知来源/); assert.match(errors, /未知配套型号/);
});
test('校验器拒绝无证据关系与不安全链接', () => {
  const c = copy(); c.relations[0].source_ids = []; c.sources[0].url = 'javascript:alert(1)';
  const errors = validate(c).join('\n');
  assert.match(errors, /缺少来源/); assert.match(errors, /官方 HTTPS/);
});
test('校验器拒绝无效查阅日期和缺少基本参数的产品', () => {
  const c = copy(); c.products[0].checked_on = '2026-02-31'; c.products[0].specs = {};
  const errors = validate(c).join('\n');
  assert.match(errors, /日期/); assert.match(errors, /规格/);
});
test('校验器拒绝资料差异中悬空的型号和证据', () => {
  const c = copy(); c.issues[0].product_ids = ['未知型号']; c.issues[0].observations[0].source_id = '未知';
  const errors = validate(c).join('\n');
  assert.match(errors, /未知记录型号/); assert.match(errors, /未知观察来源/);
});
test('实际目录的来源、型号和配套关系完整', () => {
  assert.deepEqual(validate(catalog), []);
});
test('订货配置不能引用未收录的基础型号', () => {
  const c = copy(); c.products.find(p => p.id === 'CX5130-0185').specs['基础型号'] = 'CX513O';
  assert.match(validate(c).join('\n'), /未知基础型号/);
});
test('证据必须记录来源支持的具体参数和值', () => {
  const c = copy(); c.products[0].evidence[0].claims = {};
  assert.match(validate(c).join('\n'), /缺少参数观察/);
});
