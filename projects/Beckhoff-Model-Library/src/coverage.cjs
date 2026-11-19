'use strict';

function coverageBlock(data) {
  const products = data.products;
  const cx = products.filter(p => p.id.startsWith('CX') && p.kind === '基础型号').length;
  const storage = products.filter(p => p.category === '存储介质').length;
  const os = products.filter(p => p.category === '操作系统').length;
  const el1 = products.filter(p => p.family === 'EL1xxx').length;
  const el2 = products.filter(p => p.family === 'EL2xxx').length;
  return [
    `已收录 ${products.length} 条记录、${data.sources.length} 个官方来源：部分 CX 控制器 ${cx} 个基础型号，存储介质 ${storage} 条，操作系统 ${os} 条，EL1xxx ${el1} 条，EL2xxx ${el2} 条。`,
    '尚未覆盖运动控制、伺服、电机、线缆和端子全系列，也不包含 EL3xxx、ED、ES、ELM、EM、ELX、BC/BX，以及未逐项核对的其余控制器和历史型号。这不是完整产品目录。型号规律不能用来判断附件是否兼容。官网核对不等于实机验证。'
  ].join('\n');
}

module.exports = { coverageBlock };
