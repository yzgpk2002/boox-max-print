import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { createHash,createHmac } from 'node:crypto';
import { once } from 'node:events';
import { validHost,probeReceiver,sendWifi } from '../desktop/wifi.mjs';
import { makeDocumentName } from '../desktop/document-name.mjs';

test('Wi-Fi authenticates the paired receiver and checks the delivered PDF receipt',async t=>{
  const config={deviceId:'test-device-1234',token:'a'.repeat(64)};
  const spool=await fs.mkdtemp(path.join(os.tmpdir(),'boox-wifi-'));
  const job={file:'BOOX-20261116000000-1.pdf'};
  const pdf=Buffer.from('%PDF-1.4\ntransport fixture\n%%EOF');
  await fs.writeFile(path.join(spool,job.file),pdf);
  let wrongProof=false,wrongReceipt=false,received=0;
  const server=http.createServer(async(req,res)=>{
    res.setHeader('Content-Type','application/json');
    if(req.method==='GET'){
      const challenge=new URL(req.url,'http://localhost').searchParams.get('challenge');
      const proof=wrongProof?'0'.repeat(64):createHmac('sha256',config.token).update(`${config.deviceId}:${challenge}`).digest('hex');
      res.end(JSON.stringify({deviceId:config.deviceId,proof,ready:true}));return;
    }
    assert.equal(req.headers.authorization,`Bearer ${config.token}`);
    if(job.documentName)assert.equal(decodeURIComponent(req.headers['x-document-name']),job.documentName);
    else assert.equal(req.headers['x-document-name'],undefined);
    const chunks=[];for await(const c of req)chunks.push(c);
    assert.deepEqual(Buffer.concat(chunks),pdf);received++;
    res.end(JSON.stringify({deviceId:config.deviceId,sha256:wrongReceipt?'0'.repeat(64):createHash('sha256').update(pdf).digest('hex'),bytes:pdf.length,path:'/sdcard/Documents/BooxMaxPrint/'+job.file,opened:true}));
  });
  server.listen(0,'127.0.0.1');await once(server,'listening');config.port=server.address().port;
  t.after(async()=>{server.closeAllConnections();await new Promise(r=>server.close(r));await fs.rm(spool,{recursive:true,force:true});});
  const receiver=await probeReceiver('127.0.0.1',config);assert.equal(receiver.ready,true);
  const receipt=await sendWifi(job,spool,receiver,config);assert.equal(receipt.transport,'wifi');assert.equal(received,1);
  job.documentName=makeDocumentName("C:\\private\\\u4e2d\u6587 O'Brien.pdf",job.file);
  await sendWifi(job,spool,receiver,config);await sendWifi(job,spool,receiver,config);assert.equal(received,3,'readable name preserves stable transport retry');
  wrongProof=true;assert.equal(await probeReceiver('127.0.0.1',config),null);
  wrongReceipt=true;await assert.rejects(sendWifi(job,spool,receiver,config),/receipt verification/);
  assert.equal(validHost('192.0.2.123'),true);assert.equal(validHost('999.1.1.1'),false);assert.equal(validHost('example.com/path'),false);
});
