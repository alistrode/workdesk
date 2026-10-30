// Loaded after each original module's script; originals stay untouched.
const workdeskTheme = document.createElement('link');
workdeskTheme.rel = 'stylesheet';
workdeskTheme.href = '/themes/' + window.workdeskModule + '.css?v=20261010';
document.head.append(workdeskTheme);
window.workdeskBridge={
 async capture(){
  if(window.workdeskModule==='research'){
   if(!activeSession)throw Error('请先打开一份调研结果');
   const s=await api('/api/sessions/'+encodeURIComponent(activeSession));
   return {title:s.question,text:[s.question,s.report,...(s.sources||[]).map(x=>`[${x.id}] ${x.title}\n${x.url}\n${x.snippet}`)].filter(Boolean).join('\n\n'),source:'ResearchDesk / '+s.id};
  }
  if(window.workdeskModule==='prompt'){
   const r=state.run;if(!r||!document.querySelector('#editor').classList.contains('hidden'))return {title:document.querySelector('#goal').value||'提示词草稿',text:document.querySelector('#prompt').value,source:'PromptLab / 草稿'};
   return {title:r.goal||r.request?.goal||'提示词调优',text:r.humanDecision==='adopted'?r.candidatePrompt:r.originalPrompt||r.request?.originalPrompt||'',source:'PromptLab / '+r.id+' / '+(r.humanDecision==='adopted'?'已采纳':'原版')};
  }
  const s=state.current;if(!s)throw Error('请先打开一个协作任务');
  return {title:s.title||s.goal,text:[s.goal,...(s.timeline||[]).map(e=>`${e.role}：\n${e.text}`)].join('\n\n'),source:'AgentHub / '+s.id};
 },
 receive(text){
  let input;
  if(window.workdeskModule==='research'){input=document.querySelector('#question');const previous=input.value;if((previous.length+(previous?2:0)+text.length)>1000)throw Error('合并后的调研问题超过 1000 字，请精简后再发送');startNew();input.value=previous;}
  if(window.workdeskModule==='prompt'){if(state.running)throw Error('调优正在运行，请等待完成后再发送');showEditor();input=document.querySelector('#prompt');}
  if(window.workdeskModule==='agent'){showDialog();input=document.querySelector('#goalInput');}
  input.value+=(input.value?'\n\n':'')+text;input.dispatchEvent(new Event('input',{bubbles:true}));input.focus();
 }
};
