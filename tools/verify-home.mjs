// Explicit real-device test: generated picture only; leaves the paired receiver at Home.
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';
import {setTimeout as delay} from 'node:timers/promises';
const config=JSON.parse(await fs.readFile('.local/image-client.json','utf8'));
const session=randomBytes(16).toString('hex');
async function api(endpoint,body,headers={}) {
  const response=await fetch(config.url+endpoint,{method:body?'POST':'GET',body,headers:{'X-Boox-Client':config.key,...headers},signal:AbortSignal.timeout(25000)});
  const data=await response.json();if(!response.ok){const error=new Error(data.error);error.status=response.status;throw error;}return data;
}
const control=(action,id=session)=>api('/control',JSON.stringify({action,session:id}),{'Content-Type':'application/json'});
const png=await fs.readFile('build/image-fixtures/frame-1.png');
const send=sequence=>api('/frame',png,{'Content-Type':'image/png','X-Image-Session':session,'X-Image-Sequence':String(sequence)});
async function waitViewer(visible){
  const deadline=Date.now()+8000;
  do{const state=await api('/state');if(state.viewerVisible===visible&&state.paused===!visible)return state;await delay(200);}while(Date.now()<deadline);
  throw new Error('Viewer did not reach expected visibility/paused state within 8 seconds');
}
const report={scenario:'home',checks:[],transport:'Wi-Fi HTTP',fixture:'generated PNG'};
try {
  assert.equal((await control('home')).paused,true);report.checks.push('Home works without matching image session');
  await delay(2000);await control('start');await send(1);
  await waitViewer(true);report.checks.push('Generated picture visible and receiving');
  await control('home');await waitViewer(false);await delay(2000);
  let state=await api('/state');assert.equal(state.paused,true);assert.equal(state.viewerVisible,false);
  await assert.rejects(send(2),e=>e.status===409);report.checks.push('Home pauses receiver, hides viewer and rejects subsequent frame');
  await control('resume');await send(2);
  await waitViewer(true);report.checks.push('Explicit resume opens viewer again');
  await control('home');state=await waitViewer(false);
  assert.equal(state.paused,true);assert.equal(state.viewerVisible,false);report.checks.push('Final state paused at Home');
  await fs.writeFile('build/home-verification.json',JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}finally{await control('home').catch(()=>{});}
