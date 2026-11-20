(function () {
  'use strict';
  const $ = id => document.getElementById(id);
  const c = window.MODEL_CATALOG;
  if (!c || !window.ModelSearch) {
    $('results').textContent = '资料未能加载。请重新打开完整 dist 目录中的 index.html，或运行 npm run build 后重试。';
    return;
  }
  const sources = new Map(c.sources.map(s => [s.id, s]));
  const products = new Map(c.products.map(p => [p.id, p]));
  const pageSize = 25;
  const params = new URLSearchParams(location.search);
  const state = { q: params.get('q') || '', category: params.get('category') || '',
    host: params.get('host') || '', medium: params.get('medium') || '',
    review: params.get('review') || '', lifecycle: params.get('lifecycle') || '',
    selected: params.get('product') || '', tab: params.get('tab') || 'search', page: 1 };
  const e = text => String(text ?? '').replace(/[&<>"']/g, ch => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' })[ch]);
  const badge = value => `<span class="badge ${value === '待核实冲突' ? 'conflict' : value === '资料差异' ? 'difference' : value === '产品公告' ? 'announcement' : value === '简录待补' ? 'brief' : ''}">${e(value)}</span>`;
  const modelButton = id => `<button type="button" class="model-button" data-model="${e(id)}">${e(id)}</button>`;
  const sourceLink = id => { const s = sources.get(id); return `<a href="${e(s.url)}" target="_blank" rel="noopener noreferrer">${e(s.title)}</a>`; };
  const sorted = items => [...new Set(items)].sort((a,b) => a.localeCompare(b,'zh-CN',{numeric:true}));
  function fillSelect(id, values) {
    $(id).insertAdjacentHTML('beforeend', sorted(values.filter(Boolean)).map(v => `<option value="${e(v)}">${e(v)}</option>`).join(''));
    $(id).value = state[id];
    if ($(id).selectedIndex < 0) { $(id).value = ''; state[id] = ''; }
  }
  fillSelect('host', window.ModelSearch.hostScopes(c));
  fillSelect('medium', window.ModelSearch.storageMedia(c));
  fillSelect('review', c.products.map(p => p.review_state));
  fillSelect('lifecycle', c.products.map(p => p.lifecycle));
  const categories = ['全部型号', ...new Set(c.products.map(p => p.category))];
  if (state.category && !categories.includes(state.category)) state.category = '';
  $('categories').innerHTML = categories.map((cat,i) => `<button type="button" class="category" data-category="${i ? e(cat) : ''}" aria-pressed="false">${e(cat)}<span>${i ? c.products.filter(p => p.category===cat).length : c.products.length}</span></button>`).join('');
  $('updated').textContent = c.updated_at ? new Intl.DateTimeFormat('zh-CN', {timeZone:'Asia/Shanghai',year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',hour12:false}).format(new Date(c.updated_at)) : c.updated_on;
  $('updated').dateTime = c.updated_at || c.updated_on;
  $('content-updated').textContent = $('updated').textContent;
  $('content-updated').dateTime = $('updated').dateTime;
  const controllerCount = c.products.filter(p => p.id.startsWith('CX') && p.kind === '基础型号').length;
  const el1Count = c.products.filter(p => p.family === 'EL1xxx').length;
  const el2Count = c.products.filter(p => p.family === 'EL2xxx').length;
  $('catalog-count').textContent = `${controllerCount} 个 CX 控制器 · ${el1Count} 条 EL1xxx · ${el2Count} 条 EL2xxx · ${c.products.length} 条记录 · ${c.sources.length} 个来源`;
  $('issue-count').textContent = c.issues.length;
  $('query').value = state.q;
  function syncURL() {
    const p = new URLSearchParams();
    for (const key of ['q','category','host','medium','review','lifecycle']) if (state[key]) p.set(key,state[key]);
    if (state.selected) p.set('product',state.selected);
    if (state.tab !== 'search') p.set('tab',state.tab);
    try { history.replaceState(null,'',location.pathname + (p.size ? '?' + p : '') + location.hash); } catch { /* 离线文件浏览器可能不允许修改地址。 */ }
  }
  function showTab(tab) {
    state.tab = ['search','issues','sources','help'].includes(tab) ? tab : 'search';
    for (const name of ['search','issues','sources','help']) $(name+'-view').hidden = name !== state.tab;
    document.querySelectorAll('[data-tab]').forEach(b => {
      if (b.dataset.tab === state.tab) b.setAttribute('aria-current','page'); else b.removeAttribute('aria-current');
    });
    syncURL();
  }
  function renderDetails(id) {
    const p = products.get(id);
    if (!p) { $('detail').hidden = true; return; }
    $('detail').hidden = false;
    const issues = c.issues.filter(i => i.product_ids.includes(p.id));
    const inRelations = c.relations.filter(r => r.item === p.id);
    const outRelations = c.relations.filter(r => r.host === p.id);
    const variants = c.products.filter(v => v.specs['基础型号'] === p.id);
    $('detail').innerHTML = `<div class="detail-top"><span>${e(p.category)} / ${e(p.kind)}</span><button type="button" class="copy-button" data-copy="${e(p.id)}">复制型号</button></div>
      <h2>${e(p.id)}</h2><p class="detail-name">${e(p.name)}</p><div class="badges">${badge(p.review_state)}${badge(p.lifecycle)}</div><p class="detail-summary">${e(p.summary)}</p>
      ${p.review_state === '简录待补' && sources.get(p.source_ids[0]) ? `<p><a href="${e(sources.get(p.source_ids[0]).url)}" target="_blank" rel="noopener noreferrer">打开官方产品页</a></p>` : ''}
      <h3>规格参数</h3><table class="specs"><tbody>${Object.entries(p.specs).map(([k,v]) => `<tr><th scope="row">${e(k)}</th><td>${e(v)}</td></tr>`).join('')}</tbody></table>
      ${p.notes.length ? `<h3>选型注意</h3><ul>${p.notes.map(n => `<li>${e(n)}</li>`).join('')}</ul>` : ''}
      ${issues.map(i => `<h3>${e(i.type)}</h3><div class="issue-inline"><strong>${e(i.title)}</strong>${e(i.resolution)}<br><button type="button" data-issue="${e(i.id)}">查看双方记录</button></div>`).join('')}
      ${p.specs['基础型号'] ? `<h3>基础型号</h3>${modelButton(p.specs['基础型号'])}` : ''}
      ${variants.length ? `<h3>已收录订货配置</h3>${variants.map(v => `<div class="relation">${modelButton(v.id)}<p>${e(v.specs['操作系统'])}</p></div>`).join('')}` : ''}
      ${inRelations.length ? `<h3>适用主机 / 范围</h3>${inRelations.map(r => `<div class="relation"><strong>${e(r.host)} · ${e(r.role)}</strong><p>${e(r.conditions)}</p>${r.source_ids.map(sourceLink).join('、')}</div>`).join('')}` : ''}
      ${outRelations.length ? `<h3>已收录配套</h3>${outRelations.map(r => `<div class="relation">${modelButton(r.item)} <span>${e(r.role)}</span><p>${e(r.conditions)}</p>${r.source_ids.map(sourceLink).join('、')}</div>`).join('')}` : ''}
      <h3>依据与来源</h3>${p.evidence.map(ev => { const s = sources.get(ev.source_id); return `<div class="source-item">${sourceLink(s.id)}<p>区段：${e(ev.section)}</p><p>${e(ev.observation)}</p><table class="specs"><caption class="sr-only">此来源支持的参数观察</caption><tbody>${Object.entries(ev.claims).map(([k,v]) => `<tr><th scope="row">${e(k)}</th><td>${e(v)}</td></tr>`).join('')}</tbody></table><p>${e(s.locale)} · 查阅 ${e(s.checked_on)}<br>修订版本：${e(s.revision || '未核实')}</p></div>`; }).join('')}
      <p class="detail-meta">核查日期 ${e(p.checked_on)}<br>来源核对不等于实机兼容验证；配套和供货状态需按具体配置确认。</p>`;
  }
  function render() {
    const results = window.ModelSearch.search(c,state);
    const maxPage = Math.max(1,Math.ceil(results.length / pageSize));
    state.page = Math.max(1,Math.min(state.page,maxPage));
    const start = (state.page - 1) * pageSize;
    const shown = results.slice(start,start+pageSize);
    if (!results.some(p => p.id === state.selected)) state.selected = shown[0]?.id || '';
    if (!shown.some(p => p.id === state.selected) && shown.length) state.selected = shown[0].id;
    $('result-count').textContent = `找到 ${results.length} 条`;
    $('category-title').textContent = state.category || '全部型号';
    $('scope-note').hidden = !state.host;
    $('scope-note').textContent = `当前范围：${state.host}。仅显示已收录的主机、配置及有来源的配套项；系列范围不会自动推断到具体型号。`;
    $('results').innerHTML = shown.map(p => {
      const specs = Object.entries(p.specs).slice(0,2).map(([k,v]) => `<span>${e(k)}：${e(v)}</span>`).join('<br>');
      const official = p.review_state === '简录待补' ? sources.get(p.source_ids[0]) : null;
      const officialLink = official ? `<a class="official-link" href="${e(official.url)}" target="_blank" rel="noopener noreferrer">官方页面</a>` : '';
      return `<tr class="${p.id === state.selected ? 'selected' : ''}"><td><button type="button" class="model-button" data-select="${e(p.id)}" aria-pressed="${p.id === state.selected}" aria-controls="detail">${e(p.id)}</button><span class="product-name">${e(p.name)}</span>${officialLink}<span class="record-tag">${e(p.category)} · ${e(p.kind)}</span></td><td class="spec-snippet">${specs}</td><td>${badge(p.review_state)}<span class="record-tag">${p.source_ids.length} 个来源</span>${p.lifecycle === '产品公告' ? badge(p.lifecycle) : ''}</td></tr>`;
    }).join('');
    const typed = state.q.trim();
    $('empty-text').textContent = typed
      ? `没有与“${typed}”匹配的已收录资料。这个型号或关键词可能尚未收录，不表示该产品不存在、已停产或不兼容。`
      : '当前筛选下没有已收录资料。未收录不表示产品不存在或不兼容。';
    $('empty').hidden = results.length !== 0;
    document.querySelector('.table-scroll').hidden = results.length === 0;
    $('page-label').textContent = results.length ? `${start+1}–${Math.min(start+pageSize,results.length)} / ${results.length} 条` : '0 条记录';
    $('previous').disabled = state.page === 1;
    $('next').disabled = state.page === maxPage;
    document.querySelectorAll('[data-category]').forEach(b => b.setAttribute('aria-pressed',String(b.dataset.category === state.category)));
    renderDetails(state.selected);
    syncURL();
  }
  function reset() {
    Object.assign(state,{ q:'',category:'',host:'',medium:'',review:'',lifecycle:'',selected:'',page:1 });
    $('query').value = '';
    for (const id of ['host','medium','review','lifecycle']) $(id).value = '';
    showTab('search'); render();
  }
  function openModel(id) {
    reset(); state.q=id; state.selected=id; $('query').value=id; render();
    $('main').scrollIntoView({block:'start'});
  }
  function renderReferencePages() {
    $('issues-list').innerHTML = c.issues.map(i => `<article class="issue-record" id="issue-${e(i.id)}">${badge(i.type)}<h2>${e(i.title)}</h2><p>${e(i.description)}</p><div class="issue-models">${i.product_ids.map(modelButton).join('')}</div><ul>${i.observations.map(o => `<li>${e(o.statement)}<br>${sourceLink(o.source_id)}</li>`).join('')}</ul><p><strong>当前处理：</strong>${e(i.resolution)}</p></article>`).join('');
    $('sources-list').innerHTML = c.sources.map(s => `<article class="source-record"><h2>${sourceLink(s.id)}</h2><p>${e(s.kind)} · ${e(s.locale)} · 查阅 ${e(s.checked_on)}</p><p>定位：${e(s.section)}</p><p>修订版本：${e(s.revision || '未核实')} · 发布日期：${e(s.published_on || '未核实')} · 原文件归档：${s.archived_file ? e(s.archived_file) : '尚未归档'}</p><p class="source-url">${e(s.url)}</p></article>`).join('');
    $('backlog').innerHTML = c.backlog.map(b => `<li><strong>${e(b.topic)}</strong>：${e(b.reason)}</li>`).join('');
  }
  let debounce, toastTimer;
  $('query').addEventListener('input', () => {
    clearTimeout(debounce); debounce = setTimeout(() => { state.q = $('query').value; state.page=1; state.selected=''; render(); },120);
  });
  $('search-form').addEventListener('submit', ev => { ev.preventDefault(); clearTimeout(debounce); state.q=$('query').value; state.page=1; render(); });
  for (const id of ['host','medium','review','lifecycle']) $(id).addEventListener('change', () => { state[id]=$(id).value; state.page=1; state.selected=''; render(); });
  $('reset').addEventListener('click',reset);
  $('previous').addEventListener('click', () => { state.page--; render(); });
  $('next').addEventListener('click', () => { state.page++; render(); });
  document.addEventListener('click', async ev => {
    const b = ev.target.closest('button'); if (!b) return;
    if (b.hasAttribute('data-category')) { state.category=b.dataset.category; state.page=1; state.selected=''; showTab('search'); render(); }
    if (b.hasAttribute('data-query')) { reset(); state.q=b.dataset.query; $('query').value=state.q; render(); }
    if (b.hasAttribute('data-select')) {
      state.selected=b.dataset.select; render();
      const current = document.querySelector(`[data-select="${CSS.escape(state.selected)}"]`);
      if (matchMedia('(max-width:1180px)').matches) {
        $('detail').setAttribute('tabindex','-1'); $('detail').focus({preventScroll:true}); $('detail').scrollIntoView({block:'start'});
      } else current?.focus({preventScroll:true});
    }
    if (b.hasAttribute('data-model')) openModel(b.dataset.model);
    if (b.hasAttribute('data-reset')) reset();
    if (b.hasAttribute('data-tab')) showTab(b.dataset.tab);
    if (b.hasAttribute('data-issue')) { showTab('issues'); $('issue-'+b.dataset.issue)?.scrollIntoView({block:'start'}); }
    if (b.hasAttribute('data-copy')) {
      let message;
      try { await navigator.clipboard.writeText(b.dataset.copy); message='已复制 '+b.dataset.copy; }
      catch { message='浏览器不允许自动复制，请选中详情中的型号复制。'; }
      $('toast').textContent=message; $('toast').hidden=false; clearTimeout(toastTimer); toastTimer=setTimeout(() => { $('toast').hidden=true; },3500);
    }
  });
  state.page = window.ModelSearch.pageForSelection(window.ModelSearch.search(c,state),state.selected,pageSize);
  renderReferencePages(); render(); showTab(state.tab);
})();
