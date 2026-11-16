import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { readWifiConfig,probeReceiver,discoverReceivers } from '../desktop/wifi.mjs';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const config=await readWifiConfig(path.join(root,'.local','wifi.json'));
if(!config)throw new Error('BOOX Wi-Fi has not been paired');
const receiver=await probeReceiver(config.host,config);
const discovered=await discoverReceivers(config);
console.log(JSON.stringify({configuredHost:config.host,authenticated:!!receiver,storageReady:receiver?.ready??false,mode:config.mode,discovered},null,2));
