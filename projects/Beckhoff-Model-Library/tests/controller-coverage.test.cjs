const {test}=require('node:test');
const assert=require('node:assert/strict');
const catalog=require('../data/catalog.json');
const {search}=require('../src/search.js');

test('CX5020 基础型号和完整订货号可查询，E-bus 与 K-bus 配置分开',()=>{
  assert.equal(search(catalog,{q:'cx5020'})[0]?.id,'CX5020');
  const e=search(catalog,{q:'CX5020-0111'})[0];
  const k=search(catalog,{q:'CX5020-1115'})[0];
  assert.equal(e?.specs['总线'],'E-bus');
  assert.equal(k?.specs['总线'],'K-bus');
  assert.equal(k?.specs['运行时'],'TwinCAT 3 XAR，不含许可证');
});
test('CX5020 主机筛选包含明确配套的 CF 卡和接口，不混入 CFast',()=>{
  const ids=search(catalog,{host:'CX5020'}).map(p=>p.id);
  assert.ok(ids.includes('CX1900-0024'));
  assert.ok(ids.includes('CX5020-N030'));
  assert.ok(!ids.includes('CX2900-0038'));
});
test('常用与服务阶段控制器本体有规格来源，不能以配置条数代替本体覆盖',()=>{
  for(const id of ['CX5010','CX5020','CX5120','CX5140','CX5230','CX5240','CX5330','CX5340','CX5620','CX5630','CX9240','CX2020','CX2030','CX2040','CX2033','CX2043','CX2042','CX2062','CX2072']){
    const p=catalog.products.find(p=>p.id===id);
    assert.ok(p,`${id} 未收录`);
    assert.equal(p.kind,'基础型号');
    assert.ok(p.specs['处理器']);
    assert.ok(p.evidence.some(e=>e.claims['处理器']===p.specs['处理器']));
  }
});
