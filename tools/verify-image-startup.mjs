// Real-device regression: burst generated frames while the image Activity is opening.
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';
import {setTimeout as delay} from 'node:timers/promises';
const config=JSON.parse(await fs.readFile('.local/image-client.json','utf8'));
let frames=await Promise.all([1,2,3].map(n=>fs.readFile(`build/image-fixtures/frame-${n}.png`)));
if(process.argv.includes('--tiny'))frames=Array(3).fill(Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAAFiUAABYlAUlSJPAAAAARSURBVChTYwCC/yBiRAMGBgAEEwEAnhVy6AAAAABJRU5ErkJggg==','base64'));
async function api(endpoint,body,headers={}) {
  const response=await fetch(config.url+endpoint,{method:body?'POST':'GET',body,headers:{'X-Boox-Client':config.key,...headers},signal:AbortSignal.timeout(25000)});
  const value=await response.json();if(!response.ok){const e=new Error(value.error);e.status=response.status;throw e;}return value;
}
let session='';
const control=(action,orientation)=>api('/control',JSON.stringify({action,session,orientation,fullRefreshEvery:20}),{'Content-Type':'application/json'});
const report={runs:[]};
try {
  for(const orientation of ['landscape','portrait','landscape']) {
    session=randomBytes(16).toString('hex');await control('home');await delay(2000);
    await control('start',orientation);const row={orientation,frames:[]};report.runs.push(row);
    for(let n=1;n<=12;n++) {
      const before=performance.now();
      await api('/frame',frames[n%3],{'Content-Type':'image/png','X-Image-Session':session,'X-Image-Sequence':String(n)});
      row.frames.push({sequence:n,ms:Math.round(performance.now()-before)});await delay(100);
    }
    await delay(500);const state=await api('/state');row.paused=state.paused;row.reason=state.reason;row.viewerVisible=state.viewerVisible;
    assert.equal(state.paused,false,'Opening the viewer must not pause receiving');assert.equal(state.viewerVisible,true);assert.equal(state.sequence,12);
    console.log(JSON.stringify(row));
  }
  report.passed=true;
}catch(e){report.passed=false;report.error=e.message;console.error(e.message);process.exitCode=1;}
finally{await control('home').catch(()=>{});await fs.writeFile('build/image-startup-verification.json',JSON.stringify(report,null,2));}
