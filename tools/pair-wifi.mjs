import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomBytes,randomUUID } from 'node:crypto';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { readWifiConfig,probeReceiver } from '../desktop/wifi.mjs';
const exec=promisify(execFile);
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const adb=process.env.BOOX_ADB??'E:\\codexblender\\film-001\\tools\\platform-tools\\adb.exe';
const serial=process.env.BOOX_SERIAL??'0123456789ABCDEF';
const run=async args=>{
  try{return (await exec(adb,['-s',serial,...args],{windowsHide:true,timeout:60000})).stdout;}
  catch{throw new Error('ADB pairing operation failed; check USB connection and debugging authorization');}
};
const configFile=path.join(root,'.local','wifi.json');
await fs.mkdir(path.dirname(configFile),{recursive:true});
let config=await readWifiConfig(configFile);
if(!config)config={token:randomBytes(32).toString('hex'),deviceId:randomUUID(),port:8632,mode:'auto'};
const netcfg=await run(['shell','netcfg']);
const match=netcfg.match(/wlan0\s+UP\s+(\d+\.\d+\.\d+\.\d+)\//);
if(!match)throw new Error('Connect BOOX to Wi-Fi before pairing');
config.host=match[1];
// Explicit protected receiver only accepts callers with DUMP permission (ADB shell).
const provisioned=await run(['shell','am','broadcast','-a','net.booxmax.print.PROVISION','-n','net.booxmax.print/.ProvisionReceiver','--es','token',config.token,'--es','deviceId',config.deviceId]);
if(!/result=1\b/.test(provisioned))throw new Error('BOOX receiver did not accept pairing; confirm APK is installed');
await fs.writeFile(configFile,JSON.stringify(config,null,2));
let receiver;
for(let i=0;i<10;i++){receiver=await probeReceiver(config.host,config);if(receiver)break;await new Promise(r=>setTimeout(r,500));}
if(!receiver)throw new Error('Paired over USB, but Wi-Fi receiver did not respond');
console.log(JSON.stringify({paired:true,host:receiver.host,ready:receiver.ready,mode:config.mode}));
