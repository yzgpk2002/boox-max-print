import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import dgram from 'node:dgram';
import { createHash, createHmac, randomBytes, timingSafeEqual } from 'node:crypto';
import { validateDocumentName } from './document-name.mjs';

export async function readWifiConfig(file) {
  let config;
  try { config=JSON.parse(await fs.readFile(file,'utf8')); }
  catch(error){ if(error.code==='ENOENT')return null; throw error; }
  if(!/^[0-9a-f]{64}$/.test(config.token)||!/^[a-zA-Z0-9-]{8,64}$/.test(config.deviceId)) throw new Error('Invalid BOOX Wi-Fi pairing configuration');
  return config;
}
export function validHost(host) {
  return typeof host==='string' && /^\d{1,3}(\.\d{1,3}){3}$/.test(host) && host.split('.').every(p=>Number(p)<=255);
}
export async function probeReceiver(host, config, timeout=1800) {
  if(!validHost(host))return null;
  const challenge=randomBytes(16).toString('hex');
  try {
    const reply=await fetch(`http://${host}:${config.port??8632}/health?challenge=${challenge}`,{signal:AbortSignal.timeout(timeout),redirect:'error'});
    if(!reply.ok)return null;
    const result=await reply.json();
    const expected=createHmac('sha256',config.token).update(`${config.deviceId}:${challenge}`).digest();
    if(result.deviceId!==config.deviceId || !/^[0-9a-f]{64}$/.test(result.proof) || !timingSafeEqual(Buffer.from(result.proof,'hex'),expected))return null;
    return {host,ready:result.ready===true,images:result.images===true};
  } catch {return null;}
}
export function broadcastAddresses() {
  const result=new Set();
  for(const addresses of Object.values(os.networkInterfaces()))for(const address of addresses??[]){
    if(address.family!=='IPv4'||address.internal||address.address.startsWith('169.254.'))continue;
    const ip=address.address.split('.').map(Number),mask=address.netmask.split('.').map(Number);
    result.add(ip.map((v,i)=>(v|(~mask[i]&255))).join('.'));
  }
  return [...result];
}
export async function discoverReceivers(config, timeout=1600) {
  return new Promise(resolve=>{
    const found=new Set(),socket=dgram.createSocket('udp4');
    let timer;
    const finish=()=>{clearTimeout(timer);try{socket.close();}catch{}resolve([...found]);};
    socket.once('error',finish);
    socket.on('message',(data,remote)=>{
      if(data.toString()===`BOOXMAXPRINT/1 ${config.deviceId} ${config.port??8632}`)found.add(remote.address);
    });
    socket.bind(0,'0.0.0.0',()=>{
      socket.setBroadcast(true);
      const query=Buffer.from(`BOOXMAXPRINT/1 ${config.deviceId}`);
      for(const host of broadcastAddresses())socket.send(query,config.port??8632,host,()=>{});
      timer=setTimeout(finish,timeout);
    });
  });
}
export async function findReceiver(config, configPath) {
  const known=await probeReceiver(config.host,config);
  if(known)return known;
  const discovered=await discoverReceivers(config);
  for(const host of discovered){
    const receiver=await probeReceiver(host,config);
    if(!receiver)continue;
    config.host=host;
    if(configPath){await fs.writeFile(`${configPath}.tmp`,JSON.stringify(config,null,2));await fs.rename(`${configPath}.tmp`,configPath);}
    return receiver;
  }
  return null;
}
export async function sendWifi(job, spool, receiver, config) {
  if(!receiver.ready)throw new Error('BOOX storage unavailable. Turn off USB storage sharing on BOOX.');
  if(!/^BOOX-\d{14}-\d+\.pdf$/.test(job.file))throw new Error('Invalid PDF job filename');
  const body=await fs.readFile(path.join(spool,job.file));
  const sha256=createHash('sha256').update(body).digest('hex');
  const documentHeaders=job.documentName?{'X-Document-Name':encodeURIComponent(validateDocumentName(job.documentName,job.file))}:{};
  // After uploading begins do not silently fall back to USB: a lost response
  // may mean the PDF was already opened. Keep an explicit failure for review.
  const response=await fetch(`http://${receiver.host}:${config.port??8632}/jobs/${job.file}`,{
    method:'POST',redirect:'error',signal:AbortSignal.timeout(180000),
    headers:{Authorization:`Bearer ${config.token}`,'Content-Type':'application/pdf','Content-Length':String(body.length),'X-Content-Sha256':sha256,...documentHeaders},body
  });
  const result=await response.json();
  if(!response.ok)throw new Error(`Wi-Fi receiver: ${result.error??response.status}`);
  if(result.deviceId!==config.deviceId||result.bytes!==body.length||result.sha256!==sha256||result.opened!==true)throw new Error('Wi-Fi receipt verification failed; check BOOX before retrying');
  return {transport:'wifi',devicePath:result.path,sha256,receiverHost:receiver.host};
}
