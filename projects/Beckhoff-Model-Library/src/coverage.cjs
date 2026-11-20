'use strict';

function coverageBlock(data) {
  const products = data.products;
  const cx = products.filter(p => p.id.startsWith('CX') && p.kind === '基础型号').length;
  const cIpc = products.filter(p => /^C\d/.test(p.id) && !p.id.startsWith('C9900')).length;
  const cp = products.filter(p => p.id.startsWith('CP')).length;
  const brief = products.filter(p => p.review_state === '简录待补').length;
  const storage = products.filter(p => p.category === '存储介质').length;
  const os = products.filter(p => p.category === '操作系统').length;
  const el1 = products.filter(p => p.family === 'EL1xxx').length;
  const el2 = products.filter(p => p.family === 'EL2xxx').length;
  return [
    `已收录 ${products.length} 条记录、${data.sources.length} 个官方来源：工控机 C 系列 ${cIpc} 条，CX 控制器基础型号 ${cx} 个，控制面板与面板式电脑 CP 系列 ${cp} 条，存储介质 ${storage} 条，操作系统 ${os} 条，EL1xxx ${el1} 条，EL2xxx ${el2} 条。其中 ${brief} 条是官网产品页简录，规格未逐项摘录。`,
    '尚未覆盖运动控制、伺服、电机、线缆和端子全系列，也不包含 EL3xxx、ED、ES、ELM、EM、ELX、BC/BX。简录只保证能按型号找到官方产品页，不是完整规格，也不等于实机验证。型号规律不能用来判断附件是否兼容。官网核对不等于实机验证。'
  ].join('\n');
}

module.exports = { coverageBlock };
