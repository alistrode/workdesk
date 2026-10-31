const q=s=>document.querySelector(s),titles={home:'工作概览',research:'调研与证据',prompt:'提示词调优',agent:'智能体协作',guio:'设备与操作',materials:'共享材料'};
let current='home',selected=null,materials=[];
function toast(text){q('#toast').textContent=text;q('#toast').classList.add('show');setTimeout(()=>q('#toast').classList.remove('show'),3500)}
async function api(url,body,method){const r=await fetch(url,{method:method||(body?'POST':'GET'),headers:{'Content-Type':'application/json'},body:body?JSON.stringify(body):undefined});const d=r.status===204?null:await r.json();if(!r.ok)throw Error(d?.error||'操作失败');return d}
function run(fn){return async(...args)=>{try{await fn(...args)}catch(e){toast(e.message)}}}
async function activate(view){current=view;document.querySelectorAll('.view').forEach(x=>x.hidden=x.id!==view);document.querySelectorAll('nav button').forEach(x=>x.classList.toggle('active',x.dataset.view===view));q('#pageTitle').textContent=titles[view];q('#capture').hidden=!['research','prompt','agent'].includes(view);if(['research','prompt','agent'].includes(view)&&!q('#'+view+' iframe')){await api('/api/modules/'+view+'/start',{});const frame=document.createElement('iframe');frame.title=titles[view];frame.src='/m/'+view+'/';q('#'+view).append(frame);frame.addEventListener('load',()=>refreshStatus());}if(view==='materials')await loadMaterials();}
document.querySelectorAll('[data-view]').forEach(x=>x.onclick=run(()=>activate(x.dataset.view)));document.querySelectorAll('[data-go]').forEach(x=>x.onclick=run(()=>activate(x.dataset.go)));
async function refreshStatus(){const list=await api('/api/modules');list.forEach(m=>{q('#'+m.key+'Status').className=m.ready?'ready':'';q('#'+m.key+'Status').title=m.ready?'已就绪':'点击后启动'});}
q('#refresh').onclick=run(async()=>{await refreshStatus();toast('状态已更新')});
async function loadMaterials(){materials=await api('/api/materials');const list=q('#materialList');list.replaceChildren();for(const m of materials){const b=document.createElement('button');b.className='materialItem'+(selected?.id===m.id?' selected':'');const title=document.createElement('b');title.textContent=m.title;const meta=document.createElement('small');meta.textContent=m.source+' · '+new Date(m.createdAt).toLocaleDateString();b.append(title,meta);b.onclick=()=>editMaterial(m);list.append(b);}if(!materials.length)list.textContent='还没有材料，先收集一个模块的结果。';}
function editMaterial(m){selected=m;q('#materialTitle').value=m?.title||'';q('#materialText').value=m?.text||'';q('#materialMeta').textContent=m?m.source+' · '+new Date(m.createdAt).toLocaleString(): '新材料';document.querySelectorAll('.materialItem').forEach((b,i)=>b.classList.toggle('selected',materials[i]?.id===m?.id));}
q('#newMaterial').onclick=()=>editMaterial(null);
q('#saveMaterial').onclick=run(async()=>{const saved=await api('/api/materials',{title:q('#materialTitle').value,text:q('#materialText').value,source:selected?.source||'手动'});editMaterial(saved);await loadMaterials();toast('已保存为独立材料，历史版本保留')});
q('#capture').onclick=run(async()=>{const frame=q('#'+current+' iframe');if(!frame?.contentWindow.workdeskBridge)throw Error('模块还在加载，请稍后重试');const material=await frame.contentWindow.workdeskBridge.capture();const saved=await api('/api/materials',material);await activate('materials');editMaterial(saved);toast('结果已收集到共享材料')});
document.querySelectorAll('[data-send]').forEach(b=>b.onclick=run(async()=>{const text=q('#materialText').value;if(!text.trim())throw Error('请先填写材料内容');if(b.dataset.send==='research'&&text.length>1000)throw Error('调研问题最多 1000 字，请先精简这份材料');await activate(b.dataset.send);const f=q('#'+b.dataset.send+' iframe');let bridge;for(let i=0;i<100;i++){bridge=f.contentWindow.workdeskBridge;if(bridge)break;await new Promise(r=>setTimeout(r,100));}if(!bridge)throw Error('模块加载失败，请重试');bridge.receive(text);toast('已发送到编辑区，请检查后开始处理')}));
q('#deleteMaterial').onclick=run(async()=>{if(!selected)throw Error('请先选择一份已保存材料');if(!confirm('删除这份共享材料？原模块中的记录仍保留。'))return;await api('/api/materials/'+selected.id,null,'DELETE');editMaterial(null);await loadMaterials();toast('材料已删除')});
q('#copyMaterial').onclick=run(async()=>{await navigator.clipboard.writeText(q('#materialText').value);toast('已复制')});
q('#exportMaterial').onclick=()=>{const blob=new Blob([JSON.stringify({title:q('#materialTitle').value,text:q('#materialText').value,source:selected?.source||'手动'},null,2)],{type:'application/json'});const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download='WorkDesk-material.json';a.click();setTimeout(()=>URL.revokeObjectURL(a.href),1000)};
q('#importMaterial').onclick=()=>q('#importFile').click();q('#importFile').onchange=run(async e=>{const file=e.target.files[0];if(!file)return;if(file.size>1500000)throw Error('文件太大');const d=JSON.parse(await file.text());if(typeof d.title!=='string'||typeof d.text!=='string')throw Error('文件需包含 title 和 text');const saved=await api('/api/materials',{title:d.title,text:d.text,source:typeof d.source==='string'?d.source:'导入'});await loadMaterials();editMaterial(saved);e.target.value='';toast('材料已导入')});
q('#openGuio').onclick=run(async()=>{await api('/api/guio/open',{});toast('GuIO 操作面板已打开')});for(const command of ['ping','devices'])q('#'+command).onclick=run(async()=>{q('#deviceOutput').textContent='正在检查…';const r=await api('/api/guio/'+command,{});q('#deviceOutput').textContent=(r.ok?'检查完成\n':'检查未成功\n')+r.output+(r.error?'\n'+r.error:'')});
refreshStatus().catch(()=>toast('状态读取失败'));setInterval(()=>refreshStatus().catch(()=>{}),30000);

/* ==================== 模型一键配置（自动注入） ==================== */
(function () {
  if (window.__modelSetupMounted) return;
  window.__modelSetupMounted = true;

  var css = document.createElement('style');
  css.textContent = [
    '#msetup{position:fixed;right:16px;bottom:16px;z-index:9999;background:#153a35;color:#eaf4f1;',
    'border-radius:12px;padding:12px 14px;min-width:280px;max-width:340px;',
    'box-shadow:0 8px 28px rgba(0,0,0,.28);font:13px/1.55 system-ui,"Segoe UI",sans-serif}',
    '#msetup .ms-title{font-weight:600;margin-bottom:6px}',
    '#msetup .ms-row{display:flex;align-items:center;gap:8px;margin-bottom:8px}',
    '#msetup .ms-state{opacity:.88}',
    '#msetup button{background:#2ea88a;color:#fff;border:0;border-radius:8px;padding:7px 14px;font-size:13px;cursor:pointer}',
    '#msetup button:disabled{opacity:.55;cursor:default}',
    '#msetup .ms-bar{height:8px;border-radius:6px;background:rgba(255,255,255,.18);overflow:hidden}',
    '#msetup .ms-bar > i{display:block;height:100%;width:0;background:#4dd0aa;transition:width .25s}',
    '#msetup .ms-text{font-size:12px;opacity:.82;margin-top:6px;word-break:break-all}',
    '#msetup .ms-err{color:#ffb4a8}'
  ].join('');

  function mount() {
    if (document.getElementById('msetup')) return;
    document.head.appendChild(css);

    var box = document.createElement('div');
    box.id = 'msetup';
    box.innerHTML =
      '<div class="ms-title">本地模型</div>' +
      '<div class="ms-row"><span class="ms-state">检测中…</span></div>' +
      '<div class="ms-row"><button type="button">一键配置</button></div>' +
      '<div class="ms-prog" hidden><div class="ms-bar"><i></i></div><div class="ms-text"></div></div>';
    document.body.appendChild(box);

    var stateEl = box.querySelector('.ms-state');
    var btn = box.querySelector('button');
    var prog = box.querySelector('.ms-prog');
    var bar = box.querySelector('.ms-bar > i');
    var text = box.querySelector('.ms-text');
    var es = null;

    function pct(s) {
      if (!s.total || s.total <= 0) return null;
      return Math.max(0, Math.min(100, Math.round(s.completed * 100 / s.total)));
    }

    function mb(v) { return (v / 1048576).toFixed(0); }

    function render(s) {
      if (s.ready) {
        stateEl.textContent = '● 已就绪（' + s.model + '）';
        btn.hidden = true;
        prog.hidden = true;
        if (es) { es.close(); es = null; }
        return;
      }
      btn.hidden = false;

      if (s.running) {
        btn.disabled = true;
        btn.textContent = '配置中…';
        prog.hidden = false;
        var p = pct(s);
        bar.style.width = (p === null ? 100 : p) + '%';
        bar.style.opacity = (p === null ? 0.35 : 1);
        var label = s.status || '处理中';
        if (p !== null) label += '　' + p + '%　' + mb(s.completed) + ' / ' + mb(s.total) + ' MB';
        text.textContent = label;
        text.className = 'ms-text';
        return;
      }

      if (s.phase === 'error') {
        btn.disabled = false;
        btn.textContent = '重试';
        prog.hidden = false;
        bar.style.width = '100%';
        bar.style.opacity = 0.25;
        text.textContent = s.error || '配置失败';
        text.className = 'ms-text ms-err';
        if (es) { es.close(); es = null; }
        return;
      }

      btn.disabled = false;
      btn.textContent = '一键配置';
      prog.hidden = true;
      stateEl.textContent = s.ollamaRunning ? '● 未安装模型' : '● Ollama 未运行（将自动启动）';
    }

    function poll() {
      fetch('/api/setup/state').then(function (r) { return r.json(); }).then(render).catch(function () { });
    }

    function listen() {
      if (es) { es.close(); es = null; }
      es = new EventSource('/api/setup/progress');
      es.onmessage = function (ev) {
        try {
          var s = JSON.parse(ev.data);
          render(s);
          if (!s.running) { es.close(); es = null; }
        } catch (e) { }
      };
      es.onerror = function () { if (es) { es.close(); es = null; } };
    }

    btn.addEventListener('click', function () {
      btn.disabled = true;
      btn.textContent = '配置中…';
      prog.hidden = false;
      bar.style.width = '0%';
      bar.style.opacity = 1;
      text.textContent = '正在启动…';
      text.className = 'ms-text';
      stateEl.textContent = '● 配置中';
      fetch('/api/setup/ensure-model', { method: 'POST' })
        .then(function () { listen(); })
        .catch(function (e) {
          btn.disabled = false;
          btn.textContent = '重试';
          text.textContent = '请求失败：' + e.message;
          text.className = 'ms-text ms-err';
        });
    });

    poll();
    setInterval(function () { if (!es && !btn.disabled) poll(); }, 5000);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount);
  else mount();
})();
