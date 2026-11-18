// Real-device test: sends only the two generated Chrome fixture captures, then pauses.
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
import {randomBytes,createHash} from 'node:crypto';
import {setTimeout as delay} from 'node:timers/promises';
const config=JSON.parse(await fs.readFile('.local/image-client.json','utf8'));
const session=randomBytes(16).toString('hex');
async function api(endpoint,body,extra={}){
  const response=await fetch(config.url+endpoint,{method:body?'POST':'GET',body,headers:{'X-Boox-Client':config.key,...extra},signal:AbortSignal.timeout(25000)});
  const data=await response.json();if(!response.ok){const error=new Error('Verification request failed: '+response.status);error.status=response.status;throw error;}return data;
}
const control=action=>api('/control',JSON.stringify({action,session,orientation:'landscape'}),{'Content-Type':'application/json'});
const frames=await Promise.all(['first','second'].map(name=>fs.readFile('build/chrome-window-'+name+'.png')));
assert.notDeepEqual(frames[0],frames[1]);
try{
  const previous=await api('/state');
  if(previous.session){await api('/control',JSON.stringify({action:'pause',session:previous.session}),{'Content-Type':'application/json'});await delay(3000);}
  await control('start');
  for(let i=0;i<frames.length;i++){
    for(let attempt=0;;attempt++)try{await api('/frame',frames[i],{'Content-Type':'image/png','X-Image-Session':session,'X-Image-Sequence':String(i+1)});break;}catch(error){if(error.status!==429||attempt>=5)throw error;await delay(500);}
    const state=await api('/state');assert.equal(state.session,session);assert.equal(state.sha256,createHash('sha256').update(frames[i]).digest('hex'));
  }
  console.log('PASS: two changed occluded Chrome captures delivered over authenticated Wi-Fi; both receiver SHA-256 receipts match');
}catch(error){console.error(error.message);process.exitCode=1;}finally{await control('pause').catch(error=>{if(error.status!==409)throw error;});}
