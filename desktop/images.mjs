import { createHash } from 'node:crypto';
import { readWifiConfig, findReceiver } from './wifi.mjs';

export const IMAGE_INTERVALS = Object.freeze([0.1, 0.2, 0.5, 1, 2]);
export const DEFAULT_IMAGE_INTERVAL = 0.5;
export function validateFrame(session, sequence, body, type) {
  if (!/^[0-9a-f]{32}$/.test(session) || !Number.isSafeInteger(sequence) || sequence < 1) throw new Error('Invalid image session or sequence');
  if (!Buffer.isBuffer(body) || body.length < 8 || body.length > 8 * 1024 * 1024) throw new Error('Image must be between 8 bytes and 8 MB');
  if (type === 'image/png') {
    if (!body.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10]))) throw new Error('Invalid PNG');
  } else if (type === 'image/jpeg') {
    if (body[0] !== 255 || body[1] !== 216 || body[2] !== 255) throw new Error('Invalid JPEG');
  } else throw new Error('PNG or JPEG required');
}

export class ImageClient {
  constructor(configPath, locate=findReceiver) { this.configPath=configPath; this.receiver=null; this.checked=0; this.locate=locate; this.connecting=null; }
  async connect() {
    if(this.connecting)return this.connecting;
    this.connecting=this.resolveConnection();
    try{return await this.connecting;}finally{this.connecting=null;}
  }
  async resolveConnection() {
    const config=await readWifiConfig(this.configPath);
    if(!config) throw new Error('Pair BOOX over USB before sending images');
    if(!this.receiver || Date.now()-this.checked>10000) {
      // Persist authenticated discovery so periodic checks do not return to a previous router's IP.
      this.receiver=await this.locate(config,this.configPath); this.checked=Date.now();
      if(!this.receiver) throw new Error('BOOX is offline; keep both devices on the same Wi-Fi');
      if(!this.receiver.images) {this.receiver=null;throw new Error('Update BOOX Max Print to version 0.4.0');}
    }
    return {config,receiver:this.receiver};
  }
  async request(endpoint, method='GET', body, headers={}) {
    const {config,receiver}=await this.connect();
    let response;
    try {
      response=await fetch(`http://${receiver.host}:${config.port??8632}/images/${endpoint}`,{
        method,redirect:'error',signal:AbortSignal.timeout(15000),
        headers:{Authorization:`Bearer ${config.token}`,...headers},body
      });
    } catch(error) {this.receiver=null;throw error;}
    const result=await response.json();
    if(!response.ok){const error=new Error(result.error??`Receiver error ${response.status}`);error.status=response.status;throw error;}
    if(result.deviceId!==config.deviceId)throw new Error('Image receiver identity mismatch');
    return result;
  }
  state(){return this.request('state');}
  control(action,session,orientation,fullRefreshEvery){
    if(!['start','pause','resume','stop','orientation','settings','refresh','home'].includes(action)||!/^[0-9a-f]{32}$/.test(session))throw new Error('Invalid image control');
    if(orientation!==undefined&&!['portrait','landscape'].includes(orientation))throw new Error('Invalid image orientation');
    if(fullRefreshEvery!==undefined&&(!Number.isInteger(fullRefreshEvery)||fullRefreshEvery<0||fullRefreshEvery>1000))throw new Error('Invalid full refresh interval');
    return this.request('control','POST',JSON.stringify({action,session,orientation,fullRefreshEvery}),{'Content-Type':'application/json'});
  }
  async send(session,sequence,body,type='image/png') {
    validateFrame(session,sequence,body,type);
    const sha256=createHash('sha256').update(body).digest('hex');
    const result=await this.request('frame','POST',body,{'Content-Type':type,'Content-Length':String(body.length),'X-Image-Session':session,'X-Image-Sequence':String(sequence),'X-Content-Sha256':sha256});
    if(result.session!==session || result.sequence!==sequence || result.sha256!==sha256 || result.bytes!==body.length || result.accepted!==true)throw new Error('Image receipt verification failed');
    return result;
  }
}
