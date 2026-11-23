const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const enginePath = path.join(__dirname, '../src/search.js');
// 初次运行尚未实现时，以空结果暴露缺失行为；实现后直接调用实际函数。
const engine = fs.existsSync(enginePath) ? require(enginePath) : { search: () => [] };
const catalog = JSON.parse(fs.readFileSync(path.join(__dirname, '../data/catalog.json'), 'utf8'));
const find = (q, filters = {}) => engine.search(catalog, { q, ...filters }).map(p => p.id);

test('完整型号排在首位，大小写、全角与长横线不影响匹配', () => {
  assert.equal(find(' ｃｘ２９００－００２６ ')[0], 'CX2900-0026');
  assert.equal(find('cx2900–0026')[0], 'CX2900-0026');
});
test('省略型号横线仍能找到型号，且不把相邻数字拼成另一型号', () => {
  assert.equal(find('cx19000124')[0], 'CX1900-0124');
  assert.deepEqual(find('CX1900-9999'), []);
});
test('多词按 AND 匹配规格，容量单位的空格不影响结果', () => {
  assert.deepEqual(find('CFast 40GB',{category:'存储介质'}), ['CX2900-0038']);
  assert.deepEqual(find('CFast 40 GB',{category:'存储介质'}), ['CX2900-0038']);
  assert.deepEqual(find('CFast 999GB'), []);
});
test('中文别名检索到存储卡与电池', () => {
  assert.ok(find('存储卡').includes('CX1900-0124'));
  assert.deepEqual(find('备用电池'), ['CX1900-0102']);
});
test('分类与主机筛选取交集，只依据显式配套关系', () => {
  assert.deepEqual(find('40GB', { category: '存储介质', host: 'CX5130' }), ['CX2900-0038']);
  assert.deepEqual(find('CX2900-0503', { host: 'CX7000' }), []);
});
test('完整主机可查自身与配置，同时保留模式级关系的独立范围', () => {
  assert.ok(find('', { host: 'CX5130' }).includes('CX5130-0185'));
  assert.ok(find('', { host: 'CX80xx' }).includes('CX1900-0124'));
  assert.ok(!find('', { host: 'CX7000' }).includes('CX1900-0124'));
});
test('资料状态筛选返回真正的冲突项，产品公告与资料状态独立', () => {
  assert.deepEqual(find('', { review: '待核实冲突' }), ['EK1100']);
  assert.ok(find('', { lifecycle: '产品公告' }).includes('CX1900-0134'));
});
test('空条件返回全部且无重复，空白查询不产生空结果', () => {
  assert.equal(find('   ').length, catalog.products.length);
  assert.equal(new Set(find('')).size, catalog.products.length);
});
test('打开分页之外的型号链接时，定位到该型号所在页而不是改成首条', () => {
  const results = [{id:'A'},{id:'B'},{id:'C'},{id:'D'}];
  const locate = engine.pageForSelection || (() => 1);
  assert.equal(locate(results,'C',2),2);
  assert.equal(locate(results,'B',2),1);
  assert.equal(locate(results,'未知',2),1);
});
test('新增的存储介质分类可以从数据生成筛选项，不混入附件子类', () => {
  const list = engine.storageMedia || (() => []);
  assert.deepEqual(list({products:[{category:'存储介质',subcategory:'eMMC'},{category:'存储介质',subcategory:'eMMC'},{category:'附件',subcategory:'电池'}]}),['eMMC']);
});
test('没有配套关系的新控制器也能出现在主机筛选，不把订货配置当作基础主机',()=>{
  const scopes=engine.hostScopes || (()=>[]);
  assert.deepEqual(scopes({products:[{id:'CX5020',category:'主机硬件',kind:'基础型号',specs:{}},{id:'CX5020-0111',category:'主机硬件',kind:'订货配置',specs:{'基础型号':'CX5020'}}],relations:[{host:'CX50x0',item:'CARD'}]}).sort(),['CX5020','CX50x0'].sort());
});
test('40g、40 G 和 40GB 等价，实际容量 40GB 的存储件优先于主机说明',()=>{
  const results=find('40g');
  assert.deepEqual(results.slice(0,2),['C9900-H868','CX2900-0038']);
  assert.deepEqual(find('40 G'),results);
  assert.deepEqual(find('40GB'),results);
  for(const id of ['C9900-H597','C9900-H598','CX2900-0503']) assert.ok(!results.includes(id),id+' 不应作为 40GB 存储件返回');
});
test('容量精确匹配，240GB、640GB 和替代 40GB 的 80GB 盘不混入',()=>{
  const sample={products:[
    {id:'A',name:'40 GB 卡',specs:{'容量':'40 GB'},category:'存储介质',summary:''},
    {id:'B',name:'240 GB 卡',specs:{'容量':'240 GB'},category:'存储介质',summary:''},
    {id:'C',name:'640 GB 卡',specs:{'容量':'640 GB'},category:'存储介质',summary:''},
    {id:'D',name:'80 GB 升级盘',specs:{'容量':'80 GB'},category:'存储介质',summary:'instead of 40 GB'}
  ],relations:[]};
  assert.deepEqual(engine.search(sample,{q:'40g'}).map(p=>p.id),['A']);
});
test('容量缩写兼容多词筛选，GHz 处理器频率保持原意',()=>{
  assert.deepEqual(find('CFast 40g',{category:'存储介质'}),['CX2900-0038']);
  assert.ok(find('1.4GHz').includes('CX2020'));
});
test('产品状态和资料状态可以直接搜索，否定句里的停产不算命中', () => {
  assert.deepEqual(new Set(find('服务阶段')), new Set(find('', { lifecycle: '服务阶段' })));
  const stopped = find('停产');
  assert.ok(stopped.includes('CX2020'));
  assert.ok(stopped.includes('EL2022'));
  assert.ok(!stopped.includes('CX1900-0124'));
  for (const id of stopped) {
    const life = catalog.products.find(p => p.id === id).lifecycle;
    assert.ok(life.includes('服务阶段') || life.includes('最后订购'), id);
  }
  assert.ok(find('待核实冲突').includes('EK1100'));
});
test('温度不因每条记录的工作温度标签铺开，扩展温度仍然能查到', () => {
  const results = find('温度');
  assert.ok(results.includes('CX2900-0038'));
  assert.ok(results.includes('CX1900-0022'));
  assert.ok(results.includes('CX1900-0124'));
  assert.ok(results.includes('EL3202'));
  assert.ok(results.includes('EL3314'));
  assert.ok(!results.includes('EL1008'));
  assert.ok(!results.includes('CX5130'));
  assert.ok(results.length < catalog.products.length / 4);
});
test('短英文词按整词匹配，不把 IO 配到 IoT，也不把 CE 配到无关英文', () => {
  const io = find('IO');
  assert.ok(io.includes('EL1008'));
  assert.ok(io.includes('EL3202'));
  assert.ok(!io.includes('C9900-S537'));
  assert.ok(!io.includes('CX2042'));
  const ce = find('CE');
  assert.ok(ce.includes('CX1020'));
  assert.ok(ce.includes('CX5010-0110'));
  assert.ok(!ce.includes('C6030-0080'));
  assert.ok(!ce.includes('CX2020'));
  assert.ok(!ce.includes('CX2042'));
  assert.ok(!ce.includes('CX9020-0100'));
  assert.ok(ce.length < 40);
});
test('型号里的 x 表示一位数字，完整型号仍排在前面', () => {
  assert.deepEqual(find('CX20x0').slice(0, 3), ['CX2020', 'CX2030', 'CX2040']);
  assert.deepEqual(find('CX20X0'), find('CX20x0'));
  assert.ok(!find('CX20x0').includes('CX2033'));
  assert.ok(!find('CX20x0').includes('CX2042'));
  assert.equal(find('CX2020')[0], 'CX2020');
  assert.ok(find('EL1xxx').includes('EL1008'));
});
test('WinCE 对应资料中的 Windows CE，不把不能使用 CE 或 WEC7 算进去', () => {
  for (const query of ['WinCE', 'win-ce', 'win ce']) {
    const results = find(query);
    assert.ok(results.includes('CX2900-0026'), query);
    assert.ok(results.includes('CX5010-0110'), query);
    assert.ok(results.includes('CX1020'), query);
    assert.ok(!results.includes('CX9020-0100'), query);
    assert.ok(!results.includes('CX9020-0115'), query);
  }
  const wec = find('WEC7');
  assert.ok(wec.includes('CX9020-0115'));
  assert.ok(wec.includes('CX5130'));
  assert.ok(!find('40GB').includes('CX5130'));
});
test('空结果说明型号可能尚未收录，并保留提交纠错入口', () => {
  const html = fs.readFileSync(path.join(__dirname, '../site/index.html'), 'utf8');
  const app = fs.readFileSync(path.join(__dirname, '../site/app.js'), 'utf8');
  assert.match(html, /id="empty-text"/);
  assert.match(html, /提交纠错/);
  assert.match(html, /issues\/new\?title=型号资料纠错/);
  assert.match(app, /可能尚未收录/);
  assert.match(app, /不表示该产品不存在/);
});
test('说明页的收录数字与目录一致，并给出可打开的示例查询', () => {
  const { coverageBlock } = require('../src/coverage.cjs');
  const block = coverageBlock(catalog);
  for (const file of [path.join(__dirname, '../README.md'), path.join(__dirname, '../../../README.md')]) {
    assert.ok(fs.readFileSync(file, 'utf8').includes(block), file);
  }
  const root = fs.readFileSync(path.join(__dirname, '../../../README.md'), 'utf8');
  assert.match(root, /https:\/\/vollmer2012\.github\.io\/beckhoff-toolbox\//);
  assert.match(root, /q=CX5130/);
  assert.match(root, /CFast%2040GB/);
  assert.match(root, /%E5%A4%87%E7%94%A8%E7%94%B5%E6%B1%A0/);
  assert.match(root, /非官方/);
  assert.match(root, /非完整目录/);
  assert.match(root, /官网核对不等于实机验证/);
});
