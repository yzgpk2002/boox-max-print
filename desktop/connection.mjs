import fs from 'node:fs/promises';
import path from 'node:path';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {randomBytes,randomUUID} from 'node:crypto';
import {readWifiConfig,probeReceiver} from './wifi.mjs';
const execute=promisify(execFile);
export function parseWifiInterfaces(text){
  const ssids=[...text.matchAll(/^\s*SSID\s*:\s*(.*?)\s*$/gm)].map(m=>m[1]).filter(Boolean);
  return {ssids:[...new Set(ssids)],status:ssids.length?'connected':/error\s*5|denied|elevation|\u62d2\u7edd|\u6743\u9650/i.test(text)?'unavailable':'disconnected'};
}
let computerCache,computerChecked=0,computerPending;
export async function computerWifi(){
  if(computerCache&&Date.now()-computerChecked<10000)return computerCache;
  if(computerPending)return computerPending;
  computerPending=(async()=>{
    try{
      const {stdout}=await execute('netsh.exe',['wlan','show','interfaces'],{encoding:'buffer',windowsHide:true,timeout:5000});
      let text;try{text=new TextDecoder('utf-8',{fatal:true}).decode(stdout);}catch{text=new TextDecoder('gb18030').decode(stdout);}
      computerCache=parseWifiInterfaces(text);
    }catch{computerCache={ssids:[],status:'unavailable'};}
    computerChecked=Date.now();return computerCache;
  })();
  try{return await computerPending;}finally{computerPending=null;}
}
export async function pairReceiver(configFile,options={}){
  const adb=options.adb??process.env.BOOX_ADB??'E:\\codexblender\\film-001\\tools\\platform-tools\\adb.exe';
  const serial=options.serial??process.env.BOOX_SERIAL??'0123456789ABCDEF';
  const exec=options.exec??execute,probe=options.probe??probeReceiver;
  const run=async args=>{
    try{return (await exec(adb,['-s',serial,...args],{windowsHide:true,timeout:10000})).stdout;}
    catch{throw new Error('Connect BOOX by USB and authorize USB debugging; no other device was selected');}
  };
  if((await run(['get-state'])).trim()!=='device')throw new Error('BOOX USB debugging is not ready');
  let config=await readWifiConfig(configFile);
  if(!config)config={token:randomBytes(32).toString('hex'),deviceId:randomUUID(),port:8632,mode:'auto'};
  const netcfg=await run(['shell','netcfg']);
  const match=netcfg.match(/wlan0\s+UP\s+(\d+\.\d+\.\d+\.\d+)\//);
  if(!match)throw new Error('Connect BOOX to Wi-Fi before pairing');
  config.host=match[1];
  const answer=await run(['shell','am','broadcast','-a','net.booxmax.print.PROVISION','-n','net.booxmax.print/.ProvisionReceiver','--es','token',config.token,'--es','deviceId',config.deviceId]);
  if(!/result=1\b/.test(answer))throw new Error('BOOX did not accept pairing; verify receiver app is installed');
  await fs.mkdir(path.dirname(configFile),{recursive:true});
  const temporary=configFile+'.'+randomUUID()+'.tmp';await fs.writeFile(temporary,JSON.stringify(config,null,2));await fs.rename(temporary,configFile);
  for(let attempt=0;attempt<3;attempt++){
    const receiver=await probe(config.host,config,5000);
    if(receiver)return {paired:true,ready:receiver.ready,host:receiver.host};
    await new Promise(r=>setTimeout(r,500));
  }
  throw new Error('USB pairing saved, but Wi-Fi verification failed; check receiver and network access');
}
