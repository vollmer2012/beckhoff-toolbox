const {test}=require('node:test');
const assert=require('node:assert/strict');
const c=require('../data/catalog.json');
const harvested=require('../research/el1xxx-verified-fields-2026-11-19.json');
const {search}=require('../src/search.js');

test('EL1xxx 官网总览每个明确型号均有对应具体页面的参数证据',()=>{
  assert.equal(harvested.links.length,60);
  for(const link of harvested.links){
    const p=c.products.find(p=>p.id===link.id);
    assert.ok(p,link.id);
    assert.equal(p.family,'EL1xxx');
    assert.ok(p.evidence.some(e=>c.sources.find(s=>s.id===e.source_id)?.url===link.url));
    assert.equal(harvested.records.find(r=>r.id===link.id).technical[0][1],link.id);
  }
});
test('EL1xxx 不混淆电压、混合通道、计数频率与安全变体',()=>{
  const get=id=>c.products.find(p=>p.id===id);
  assert.equal(get('EL1859').specs['通道配置'],'8 inputs + 8 outputs');
  assert.equal(get('EL1859').specs['输出通道数'],'8');
  assert.equal(get('EL1252-0050').specs['额定电压'],'5 V DC');
  assert.equal(get('EL1502').specs['计数频率'],'max. 100 kHz');
  assert.equal(get('EL1512').specs['计数频率'],'max. 1 kHz');
  assert.equal(get('EL1918-2200').specs['集成 TwinSAFE Logic'],'不带（官网明确区分）');
  assert.equal(get('EL1918-2200').lifecycle,'单行供货状态未核实');
  assert.equal(get('EL1202-0100').kind,'配置模式');
});
test('按 EL1xxx、完整后缀及计数规格查询可找到本批资料',()=>{
  assert.equal(search(c,{q:'EL1xxx'}).length,62);
  assert.equal(search(c,{q:'EL1252-0050'})[0].id,'EL1252-0050');
  assert.ok(search(c,{q:'EL1xxx 100 kHz'}).some(p=>p.id==='EL1502'));
});
test('NPN 检索覆盖低电平候选与双极性端子，但不混入普通高电平输入',()=>{
  const ids=search(c,{q:'npn'}).map(p=>p.id);
  for(const id of ['EL1084','EL1088','EL1094','EL1098','EL1429'])assert.ok(ids.includes(id),id);
  assert.ok(!ids.includes('EL1008'));
  assert.equal(c.products.find(p=>p.id==='EL1429').specs['开关类型'],'PNP / NPN（每通道支持）');
});
