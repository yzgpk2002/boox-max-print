// Real-device acceptance test; sends only generated fixtures to the paired BOOX.
import fs from 'node:fs/promises';
import { randomBytes } from 'node:crypto';
import assert from 'node:assert/strict';
import { setTimeout as delay } from 'node:timers/promises';
const local=JSON.parse(await fs.readFile('.local/image-client.json','utf8'));
async function api(endpoint,body,headers={}) {
  const r=await fetch(local.url+endpoint,{method:body?'POST':'GET',headers:{'X-Boox-Client':local.key,...headers},body,signal:AbortSignal.timeout(22000)});
  const data=await r.json();if(!r.ok){const e=new Error(data.error);e.status=r.status;throw e;}return data;
}
const session=randomBytes(16).toString('hex');
const frames=await Promise.all([1,2,3].map(n=>fs.readFile(`build/image-fixtures/frame-${n}.png`)));
const control=action=>api('/control',JSON.stringify({session,action}),{'Content-Type':'application/json'});
const send=(sequence,bytes=frames[0])=>api('/frame',bytes,{'Content-Type':'image/png','X-Image-Session':session,'X-Image-Sequence':String(sequence)});
const report={started:new Date().toISOString(),session,checks:[],frames:[]};
try {
  await control('start');await send(1);
  assert.equal((await send(1)).duplicate,true);report.checks.push('Duplicate frame idempotent');
  await control('pause');await assert.rejects(send(2),e=>e.status===409);assert.equal((await api('/state')).sequence,1);report.checks.push('Paused session rejects new frames');
  await control('resume');await send(3,frames[1]);await assert.rejects(send(2),e=>e.status===409);report.checks.push('Old sequence rejected');
  const baseline=Date.now();
  for(let i=0;i<10;i++){
    await delay(Math.max(0,baseline+i*500-Date.now()));const started=Date.now();const result=await send(4+i,frames[i%3]);
    report.frames.push({sequence:result.sequence,ms:Date.now()-started,bytes:result.bytes,accepted:result.accepted});
  }
  report.checks.push('Ten changed frames scheduled at 0.5-second intervals');
  // Opening another foreground app is tested separately through actual PDF printing.
  await control('pause');
  for(const orientation of ['landscape','portrait']){
    const rotated=await api('/control',JSON.stringify({session,action:'orientation',orientation}),{'Content-Type':'application/json'});
    assert.equal(rotated.orientation,orientation);assert.equal(rotated.paused,true);
  }
  report.checks.push('Desktop orientation control switches landscape and portrait without resuming paused frames');
  report.state=await api('/state');
  assert.equal(report.state.viewerVisible,true);report.checks.push('Image viewer visible');
  const unauthorized=await fetch(local.url+'/frame',{method:'POST',body:frames[0]});assert.equal(unauthorized.status,403);report.checks.push('Unauthenticated local upload rejected');
  const crossOrigin=await fetch(local.url+'/state',{headers:{'X-Boox-Client':local.key,Origin:'https://example.invalid'}});assert.equal(crossOrigin.status,403);report.checks.push('Cross-origin local access rejected');
  report.completed=new Date().toISOString();await fs.writeFile('build/image-verification.json',JSON.stringify(report,null,2));
  console.log(JSON.stringify(report,null,2));
}finally{await control('pause').catch(()=>{});}
