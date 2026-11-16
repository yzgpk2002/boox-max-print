// Sends generated test cards only. Receipt time excludes physical e-ink settling.
import fs from 'node:fs/promises';
import { randomBytes } from 'node:crypto';
import { ImageClient } from '../desktop/images.mjs';
const folder=process.argv[2]??'build/baseline-frames';
const client=new ImageClient('.local/wifi.json'),session=randomBytes(16).toString('hex');
const rows=[];let sequence=0;
try {
  await client.control('start',session,'portrait');
  for(let round=0;round<3;round++)for(let mode=0;mode<4;mode++) {
    const body=await fs.readFile(`${folder}/mode-${mode}.png`),start=performance.now();
    const result=await client.send(session,++sequence,body);
    const row={round,mode,bytes:body.length,receiptMs:Math.round(performance.now()-start),accepted:result.accepted};
    rows.push(row);console.log(JSON.stringify(row));
  }
} finally {await client.control('pause',session).catch(()=>{});}
await fs.writeFile(`${folder}/transfer.json`,JSON.stringify(rows,null,2));
