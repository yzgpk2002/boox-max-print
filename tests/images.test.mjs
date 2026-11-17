import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { once } from 'node:events';
import { createHash, createHmac } from 'node:crypto';
import { ImageClient, validateFrame, IMAGE_INTERVALS, DEFAULT_IMAGE_INTERVAL } from '../desktop/images.mjs';

test('image session checks identity, frame receipts, pause errors, and permits retry of the same sequence',async t=>{
  const folder=await fs.mkdtemp(path.join(os.tmpdir(),'boox-images-'));
  const config={deviceId:'images-test-device',token:'b'.repeat(64),host:'127.0.0.1'};
  const session='c'.repeat(32);
  const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j3ioAAAAASUVORK5CYII=','base64');
  let paused=false,badReceipt=false,frames=0,orientation='portrait',fullRefreshEvery=0;
  const server=http.createServer(async(req,res)=>{
    res.setHeader('Content-Type','application/json');
    if(req.url.startsWith('/health?')){const challenge=new URL(req.url,'http://localhost').searchParams.get('challenge');res.end(JSON.stringify({deviceId:config.deviceId,proof:createHmac('sha256',config.token).update(config.deviceId+':'+challenge).digest('hex'),images:true,ready:true}));return;}
    assert.equal(req.headers.authorization,`Bearer ${config.token}`);
    const chunks=[];for await(const c of req)chunks.push(c);const bytes=Buffer.concat(chunks);
    if(req.url==='/images/control'){const control=JSON.parse(bytes);if(control.action==='pause')paused=true;else if(['start','resume'].includes(control.action))paused=false;if(control.orientation)orientation=control.orientation;if(control.fullRefreshEvery!==undefined)fullRefreshEvery=control.fullRefreshEvery;res.end(JSON.stringify({deviceId:config.deviceId,session,paused,orientation,fullRefreshEvery}));return;}
    if(paused){res.writeHead(409);res.end(JSON.stringify({error:'Image session paused'}));return;}
    assert.deepEqual(bytes,png);frames++;
    res.end(JSON.stringify({deviceId:config.deviceId,session,sequence:Number(req.headers['x-image-sequence']),accepted:true,bytes:bytes.length,sha256:badReceipt?'0'.repeat(64):createHash('sha256').update(bytes).digest('hex')}));
  });
  server.listen(0,'127.0.0.1');await once(server,'listening');config.port=server.address().port;
  const configPath=path.join(folder,'wifi.json');await fs.writeFile(configPath,JSON.stringify(config));
  t.after(async()=>{server.closeAllConnections();await new Promise(r=>server.close(r));await fs.rm(folder,{recursive:true,force:true});});
  const client=new ImageClient(configPath);await client.control('start',session);
  await client.send(session,1,png);await client.send(session,1,png);assert.equal(frames,2);
  await client.control('pause',session);await assert.rejects(client.send(session,2,png),e=>e.status===409);
  const rotated=await client.control('orientation',session,'landscape');assert.equal(rotated.orientation,'landscape');assert.equal(rotated.paused,true);
  assert.throws(()=>client.control('orientation',session,'invalid'),/orientation/);
  const configured=await client.control('settings',session,undefined,5);assert.equal(configured.fullRefreshEvery,5);assert.equal(configured.paused,true);
  const refreshed=await client.control('refresh',session);assert.equal(refreshed.paused,true);
  assert.throws(()=>client.control('settings',session,undefined,-1),/refresh interval/);
  assert.throws(()=>client.control('settings',session,undefined,0.5),/refresh interval/);
  assert.throws(()=>client.control('settings',session,undefined,1001),/refresh interval/);
  await client.control('resume',session);badReceipt=true;await assert.rejects(client.send(session,2,png),/receipt verification/);
  assert.throws(()=>validateFrame(session,0,png,'image/png'));
  assert.throws(()=>validateFrame(session,1,Buffer.alloc(20),'image/png'));
  assert.deepEqual(IMAGE_INTERVALS,[0.1,0.2,0.5,1,2]);assert.equal(DEFAULT_IMAGE_INTERVAL,0.5);
});
