import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { attr, TAG, parseIpp } from '../desktop/ipp.mjs';
import { makePdfFixture } from './pdf-fixture.mjs';

test('IPP HTTP flow, format rejection, multi-step job and durable PDF bytes', async t => {
  const spool = await fs.mkdtemp(path.join(os.tmpdir(),'boox-ipp-test-'));
  const port = 20000 + (process.pid % 20000);
  const base = `http://127.0.0.1:${port}`;
  const child = spawn(process.execPath,['desktop/server.mjs'],{env:{...process.env,BOOX_TEST_MODE:'1',BOOX_PORT:String(port),BOOX_SPOOL:spool},stdio:['ignore','pipe','pipe']});
  let output=''; child.stdout.on('data',b=>output+=b); child.stderr.on('data',b=>output+=b);
  t.after(async()=>{ child.kill(); await once(child,'exit'); await fs.rm(spool,{recursive:true,force:true}); });
  for(let i=0;i<100;i++) { if(output.includes('listening')) break; if(child.exitCode!==null) throw new Error(output); await new Promise(r=>setTimeout(r,30)); }
  assert.match(output,/listening/);
  let id=1;
  async function send(op, fields=[], body=Buffer.alloc(0)) {
    const head=Buffer.from([1,1,0,op,0,0,0,id++,1]);
    const packet=Buffer.concat([head,attr(TAG.charset,'attributes-charset','utf-8'),attr(TAG.language,'attributes-natural-language','en'),...fields,Buffer.from([3]),body]);
    const result=await fetch(`${base}/ipp/print`,{method:'POST',headers:{'content-type':'application/ipp'},body:packet});
    assert.equal(result.status,200);
    const bytes=Buffer.from(await result.arrayBuffer());
    return {status:bytes.readUInt16BE(2),...parseIpp(bytes)};
  }
  const caps=await send(11);
  assert.equal(caps.status,0); assert.deepEqual(caps.attributes['document-format-supported'],['application/pdf']);
  const badFormat=await send(4,[attr(TAG.mime,'document-format','image/pwg-raster')]);
  assert.equal(badFormat.status,0x040a);
  const broken=await send(2,[],Buffer.from('%PDF-1.4\ntruncated'));
  assert.equal(broken.status,0x0411);
  const created=await send(5,[attr(TAG.name,'job-name','Test document')]);
  const jobId=created.attributes['job-id'][0];
  // A small synthetic envelope tests transport byte preservation, not PDF rendering.
  const pdf=Buffer.from('%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n');
  const sent=await send(6,[attr(TAG.integer,'job-id',jobId),attr(TAG.boolean,'last-document',true)],pdf);
  assert.equal(sent.status,0);
  let state;
  for(let i=0;i<50;i++){ const job=await send(9,[attr(TAG.integer,'job-id',jobId)]); state=job.attributes['job-state'][0]; if(state===9)break; await new Promise(r=>setTimeout(r,10)); }
  assert.equal(state,9);
  const duplicate=await send(6,[attr(TAG.integer,'job-id',jobId)],pdf);
  assert.equal(duplicate.status,0x0404);
  const metadata=JSON.parse(await fs.readFile(path.join(spool,`job-${jobId}.json`),'utf8'));
  assert.deepEqual(await fs.readFile(path.join(spool,metadata.file)),pdf);
  const unicodeTitle='C:\\private\\\u4e2d\u6587\u6587\u6863.pdf';
  const driverPdf=makePdfFixture(Buffer.from(unicodeTitle,'latin1'));
  const repairedJob=await send(2,[attr(TAG.name,'job-name',unicodeTitle)],driverPdf);
  assert.equal(repairedJob.status,0);
  const repairedId=repairedJob.attributes['job-id'][0];
  for(let i=0;i<50;i++){const status=await send(9,[attr(TAG.integer,'job-id',repairedId)]);if(status.attributes['job-state'][0]===9)break;await new Promise(r=>setTimeout(r,10));}
  const repairedMetadata=JSON.parse(await fs.readFile(path.join(spool,`job-${repairedId}.json`),'utf8'));
  assert.equal(repairedMetadata.state,9);assert.equal(repairedMetadata.titleRepair,'repaired-windows-title-low-byte-loss');
  assert.ok(repairedMetadata.documentName.startsWith('\u4e2d\u6587\u6587\u6863 -- BOOX-'));
  assert.deepEqual(await fs.readFile(path.join(spool,repairedMetadata.originalFile)),driverPdf);
  const deliveredPdf=await fs.readFile(path.join(spool,repairedMetadata.file));assert.deepEqual(deliveredPdf.subarray(0,driverPdf.length),driverPdf);assert.ok(deliveredPdf.length>driverPdf.length);
  const crossOrigin=await fetch(`${base}/ipp/print`,{method:'POST',headers:{origin:'https://example.com'}});
  assert.equal(crossOrigin.status,403);
});
