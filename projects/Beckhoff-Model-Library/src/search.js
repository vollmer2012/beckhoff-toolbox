(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.ModelSearch = api;
})(typeof globalThis === 'object' ? globalThis : this, function () {
  'use strict';
  const indices = new WeakMap();
  function normalize(value) {
    return String(value ?? '').normalize('NFKC').toLowerCase()
      .replace(/[‐‑‒–—−]/g, '-')
      .replace(/(\d)\s+(gb|mb|tb|ghz|mhz|khz|ms|mm|a|v|w)\b/g, '$1$2')
      .replace(/\s+/g, ' ').trim();
  }
  function modelKey(value) { return normalize(value).replace(/[-\s]/g, ''); }
  function queryText(value) {
    return normalize(value).replace(/(\d+(?:\.\d+)?)\s*([gmt])(?:b)?(?![a-z])/g, (_, number, unit) => number + unit + 'b');
  }
  function index(catalog) {
    if (indices.has(catalog)) return indices.get(catalog);
    const records = catalog.products.map(p => ({
      product: p,
      model: modelKey(p.id),
      text: normalize([p.id, p.name, p.category, p.subcategory, p.family, p.summary,
        ...(p.aliases || []), ...Object.entries(p.specs).flat()].join(' '))
    }));
    indices.set(catalog, records);
    return records;
  }
  function search(catalog, { q = '', category = '', host = '', review = '', lifecycle = '', family = '', medium = '' } = {}) {
    const query = queryText(q);
    const terms = query.split(' ').filter(Boolean);
    const capacities = terms.filter(term => /^\d+(?:\.\d+)?[gmt]b$/.test(term));
    const queryKey = modelKey(query);
    const related = new Set(catalog.relations.filter(r => r.host === host).map(r => r.item));
    return index(catalog).filter(({ product: p, model, text }) => {
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
          return new RegExp('(?:^|[^\\d.])' + escaped + '(?=$|[^a-z\\d])').test(text);
        }
        return text.includes(term) || model.includes(modelKey(term));
      });
    }).map(row => ({ ...row, rank: query && row.model === queryKey ? 0 : query && row.model.startsWith(queryKey) ? 1 : capacities.length && !row.product.specs['容量'] ? 3 : 2 }))
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
