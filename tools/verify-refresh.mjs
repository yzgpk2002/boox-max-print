// Sends generated cards to the paired BOOX. Does not test physical waveform completion.
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';
import {setTimeout as delay} from 'node:timers/promises';
import {ImageClient} from '../desktop/images.mjs';
const client=new ImageClient('.local/wifi.json'),session=randomBytes(16).toString('hex');
const frames=await Promise.all([1,2,3].map(n=>fs.readFile(`build/image-fixtures/frame-${n}.png`)));
const report={started:new Date().toISOString(),checks:[]};
async function waitCount(expected){for(let i=0;i<40;i++){const state=await client.state();if(state.fullRefreshCount===expected)return state;await delay(100);}throw new Error('Full refresh request count did not settle to '+expected);}
try {
  const started=await client.control('start',session,'portrait',3);assert.equal(started.fullRefreshSupported,true);
  for(let i=1;i<=6;i++){
    await client.send(session,i,frames[(i-1)%3]);
    if(i%3===0)await waitCount(i/3);else await delay(200);
  }
  let state=await waitCount(2);assert.equal(state.acceptedFrames,6);report.checks.push('Two native GC requests for six committed frames, interval three');
  await client.send(session,6,frames[2]);state=await client.state();assert.equal(state.acceptedFrames,6);assert.equal(state.fullRefreshCount,2);report.checks.push('Duplicate sequence does not increment counters');
  await client.control('pause',session);state=await client.control('refresh',session);assert.equal(state.paused,true);assert.equal(state.fullRefreshCount,3);report.checks.push('Manual full refresh preserves pause');
  state=await client.control('settings',session,undefined,0);assert.equal(state.paused,true);assert.equal(state.fullRefreshEvery,0);
  await client.control('resume',session);
  for(let i=7;i<=10;i++)await client.send(session,i,frames[i%3]);
  state=await client.state();assert.equal(state.fullRefreshCount,3);report.checks.push('Zero disables periodic refresh');
  await client.control('settings',session,undefined,2);await client.send(session,11,frames[0]);await delay(100);await client.send(session,12,frames[1]);state=await waitCount(4);report.checks.push('Changed interval restarts cadence');
  report.state={paused:state.paused,acceptedFrames:state.acceptedFrames,fullRefreshCount:state.fullRefreshCount,fullRefreshSupported:state.fullRefreshSupported};
}finally{await client.control('pause',session).catch(()=>{});}
report.completed=new Date().toISOString();await fs.writeFile('build/refresh-verification.json',JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
