import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {parseWifiInterfaces,pairReceiver} from '../desktop/connection.mjs';
import {findReceiver} from '../desktop/wifi.mjs';

test('Wi-Fi display distinguishes SSID from BSSID and handles unavailable permission',()=>{
 assert.deepEqual(parseWifiInterfaces('SSID : \u5bb6\u91cc\u7f51\u7edc\nBSSID : 00:00:00:00:00:00\n'),{ssids:['\u5bb6\u91cc\u7f51\u7edc'],status:'connected'});
 assert.equal(parseWifiInterfaces('Function WlanQueryInterface returns error 5: elevation required').status,'unavailable');
 assert.equal(parseWifiInterfaces('State : disconnected').status,'disconnected');
});
test('cold connection retries authenticated known address even when discovery is unavailable',async()=>{
 const calls=[];const receiver={host:'192.0.2.4',images:true};
 const result=await findReceiver({host:receiver.host},undefined,{probe:async(h,c,timeout)=>{calls.push(timeout);return calls.length===1?null:receiver;},discover:async()=>[]});
 assert.equal(result,receiver);assert.deepEqual(calls,[2500,5000]);
 assert.equal(await findReceiver({host:receiver.host},undefined,{probe:async()=>null,discover:async()=>[]}),null);
});
test('USB re-pair targets only configured device, preserves pairing identity and checks Wi-Fi',async t=>{
 const folder=await fs.mkdtemp(path.join(os.tmpdir(),'boox-pair-'));t.after(()=>fs.rm(folder,{recursive:true,force:true}));
 const file=path.join(folder,'wifi.json');const config={host:'192.0.2.1',token:'d'.repeat(64),deviceId:'test-repair-device',mode:'auto'};await fs.writeFile(file,JSON.stringify(config));
 const calls=[];const exec=async(command,args)=>{
  assert.deepEqual(args.slice(0,2),['-s','test-boox']);calls.push(args.slice(2));
  if(args[2]==='get-state')return {stdout:'device\n'};
  if(args[3]==='netcfg')return {stdout:'wlan0 UP 192.0.2.2/24'};
  assert.equal(args[6],'net.booxmax.print.PROVISION');return {stdout:'Broadcast completed: result=1'};
 };
 const result=await pairReceiver(file,{serial:'test-boox',exec,probe:async(host,c)=>{assert.equal(c.token,config.token);return {host,ready:true};}});
 assert.equal(result.paired,true);assert.equal(calls.length,3);
 const saved=JSON.parse(await fs.readFile(file));assert.equal(saved.host,'192.0.2.2');assert.equal(saved.token,config.token);assert.equal(saved.deviceId,config.deviceId);
 await assert.rejects(pairReceiver(file,{exec:async()=>{throw new Error('missing');}}),/USB/);
});
