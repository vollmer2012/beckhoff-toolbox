(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.ModelSearch = api;
})(typeof globalThis === 'object' ? globalThis : this, function () {
  'use strict';
  const indices = new WeakMap();
  // 资料没有“停产”字段。工程师常用来指已记录的服务阶段和最后订购，不改写产品状态。
  const statusQueries = {
    '停产': ['服务阶段', '最后订购'],
    '已停产': ['服务阶段', '最后订购'],
    '停售': ['服务阶段', '最后订购']
  };
  // 只把查询词展开到页面里已经出现的写法，不给产品追加别名。
  const phraseQueries = {
    wince: ['windows ce', 'windows embedded ce']
  };
  function normalize(value) {
    return String(value ?? '').normalize('NFKC').toLowerCase()
      .replace(/[‐‑‒–—−]/g, '-')
      .replace(/(\d)\s+(gb|mb|tb|ghz|mhz|khz|ms|mm|a|v|w)\b/g, '$1$2')
      .replace(/\s+/g, ' ').trim();
  }
  function modelKey(value) { return normalize(value).replace(/[-\s]/g, ''); }
  function queryText(value) {
    return normalize(value)
      .replace(/(\d+(?:\.\d+)?)\s*([gmt])(?:b)?(?![a-z])/g, (_, number, unit) => number + unit + 'b')
      .replace(/\bwin[\s-]?ce\b/g, 'wince');
  }
  function boilerplateSpecKeys(products) {
    const counts = new Map();
    const detailed = products.filter(product => product.review_state !== '简录待补');
    const basis = detailed.length ? detailed : products;
    for (const product of basis) for (const key of Object.keys(product.specs || {})) counts.set(key, (counts.get(key) || 0) + 1);
    const min = basis.length * 0.45;
    return new Set([...counts.keys()].filter(key => counts.get(key) >= min));
  }
  function visibleNote(value) {
    return String(value ?? '')
      .replace(/不[^。；]*停产/g, '')
      .replace(/不能使用\s*windows\s*ce/gi, '')
      .replace(/不支持[^。；]*windows\s*ce/gi, '');
  }
  function modelWildcard(term) {
    const key = modelKey(term);
    const parts = key.match(/^([a-z]+)([0-9].*)$/);
    if (!parts || !parts[2].includes('x')) return null;
    return new RegExp('^' + parts[1] + parts[2].replace(/x/g, '\\d'));
  }
  // 资料里的 CP22xx 这类页面用 x 表示数字。具体订货号如 CP2215 应对上该页，而不是被当成另一套型号。
  function patternCovers(model, queryKey) {
    if (!model.includes('x') || !queryKey) return false;
    const parts = model.match(/^([a-z]+)(.*)$/);
    if (!parts || !parts[2].includes('x') || !/\d/.test(parts[2])) return false;
    return new RegExp('^' + parts[1] + parts[2].replace(/x/g, '\\d')).test(queryKey);
  }
  function hasToken(text, term) {
    const escaped = term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return new RegExp('(?:^|[^a-z0-9])' + escaped + '(?=$|[^a-z0-9])').test(text);
  }
  function index(catalog) {
    if (indices.has(catalog)) return indices.get(catalog);
    const boilerplate = boilerplateSpecKeys(catalog.products);
    const records = catalog.products.map(product => {
      const specParts = [];
      for (const [key, value] of Object.entries(product.specs || {})) {
        if (!boilerplate.has(key)) specParts.push(key);
        specParts.push(value);
      }
      const notes = (product.notes || []).map(visibleNote);
      const capacityText = normalize([product.id, product.name, product.category, product.subcategory, product.family, product.summary,
        product.lifecycle, product.review_state, ...(product.aliases || []), ...specParts].join(' '));
      const text = normalize([capacityText, ...notes].join(' '));
      return { product, model: modelKey(product.id), text, capacityText };
    });
    indices.set(catalog, records);
    return records;
  }
  function termMatches(term, row) {
    const { product, model, text } = row;
    const wild = modelWildcard(term);
    if (wild && wild.test(model)) return true;
    if (patternCovers(model, modelKey(term))) return true;
    const statuses = statusQueries[term];
    if (statuses && statuses.some(status => normalize(product.lifecycle).includes(status))) return true;
    const phrases = phraseQueries[term];
    if (phrases && phrases.some(phrase => text.includes(phrase))) return true;
    if (/^[a-z]{1,2}$/.test(term)) return hasToken(text, term) || model.includes(modelKey(term));
    return text.includes(term) || model.includes(modelKey(term));
  }
  function search(catalog, { q = '', category = '', host = '', review = '', lifecycle = '', family = '', medium = '' } = {}) {
    const query = queryText(q);
    const terms = query.split(' ').filter(Boolean);
    const capacities = terms.filter(term => /^\d+(?:\.\d+)?[gmt]b$/.test(term));
    const queryKey = modelKey(query);
    const queryPattern = modelWildcard(query);
    const related = new Set(catalog.relations.filter(r => r.host === host).map(r => r.item));
    return index(catalog).filter(row => {
      const p = row.product;
      if (category && p.category !== category) return false;
      if (family && p.family !== family) return false;
      if (medium && p.subcategory !== medium) return false;
      if (review && p.review_state !== review) return false;
      if (lifecycle && p.lifecycle !== lifecycle) return false;
      if (host && !(p.id === host || p.specs['基础型号'] === host || related.has(p.id))) return false;
      return terms.every(term => {
        if (capacities.includes(term)) {
          if (p.specs['容量']) return queryText(p.specs['容量']) === term;
          const escaped = term.replace('.', '\\.');
          return new RegExp('(?:^|[^\\d.])' + escaped + '(?=$|[^a-z\\d])').test(row.capacityText);
        }
        return termMatches(term, row);
      });
    }).map(row => ({ ...row, rank: query && row.model === queryKey ? 0 : query && (row.model.startsWith(queryKey) || (queryPattern && queryPattern.test(row.model)) || patternCovers(row.model, queryKey)) ? 1 : capacities.length && !row.product.specs['容量'] ? 3 : 2 }))
      .sort((a, b) => a.rank - b.rank || a.product.id.localeCompare(b.product.id, 'en', { numeric: true }))
      .map(row => row.product);
  }
  function pageForSelection(results, id, pageSize) {
    const position = results.findIndex(p => p.id === id);
    return position < 0 ? 1 : Math.floor(position / pageSize) + 1;
  }
  function storageMedia(catalog) {
    return [...new Set(catalog.products.filter(p => p.category === '存储介质').map(p => p.subcategory).filter(Boolean))];
  }
  function hostScopes(catalog) {
    return [...new Set([
      ...catalog.relations.map(r => r.host),
      ...catalog.products.filter(p => p.category === '主机硬件' && p.kind !== '订货配置' && !p.specs['基础型号']).map(p => p.id)
    ])];
  }
  return { normalize, search, pageForSelection, storageMedia, hostScopes };
});
