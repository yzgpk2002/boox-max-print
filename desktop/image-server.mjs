import http from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomBytes, timingSafeEqual } from 'node:crypto';
import { ImageClient, IMAGE_INTERVALS, DEFAULT_IMAGE_INTERVAL } from './images.mjs';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const port=Number(process.env.BOOX_IMAGE_PORT??8633);
const clientConfigPath=path.join(root,'.local','image-client.json');
let key=randomBytes(32).toString('hex');
try {const saved=JSON.parse(await fs.readFile(clientConfigPath,'utf8'));if(/^[0-9a-f]{64}$/.test(saved.key))key=saved.key;}catch(error){if(error.code!=='ENOENT')throw error;}
await fs.mkdir(path.join(root,'.local'),{recursive:true});
await fs.writeFile(clientConfigPath,JSON.stringify({url:`http://127.0.0.1:${port}`,key}));
const client=new ImageClient(process.env.BOOX_WIFI_CONFIG??path.join(root,'.local','wifi.json'));
let uploading=false;
function json(res,status,result){res.writeHead(status,{'Content-Type':'application/json'});res.end(JSON.stringify(result));}
async function body(req,limit){const chunks=[];let n=0;for await(const c of req){n+=c.length;if(n>limit)throw new Error('Request too large');chunks.push(c);}return Buffer.concat(chunks);}
const server=http.createServer(async(req,res)=>{
  if(req.headers.origin || req.headers.host!==`127.0.0.1:${port}`){json(res,403,{error:'Local desktop client only'});return;}
  if(req.method==='GET'&&req.url==='/health'){json(res,200,{name:'BOOX Max Images',intervals:IMAGE_INTERVALS,defaultInterval:DEFAULT_IMAGE_INTERVAL});return;}
  const supplied=String(req.headers['x-boox-client']??'');
  if(!/^[0-9a-f]{64}$/.test(supplied)||!timingSafeEqual(Buffer.from(supplied),Buffer.from(key))){json(res,403,{error:'Local client authentication required'});return;}
  try {
    if(req.method==='GET'&&req.url==='/state'){json(res,200,await client.state());return;}
    if(req.method==='POST'&&req.url==='/control'){
      const value=JSON.parse((await body(req,4096)).toString());json(res,200,await client.control(value.action,value.session,value.orientation,value.fullRefreshEvery));return;
    }
    if(req.method==='POST'&&req.url==='/frame'){
      if(uploading){json(res,429,{error:'A frame is already being sent'});return;}
      uploading=true;
      try {const bytes=await body(req,8*1024*1024);json(res,200,await client.send(String(req.headers['x-image-session']??''),Number(req.headers['x-image-sequence']),bytes,req.headers['content-type']));}
      finally{uploading=false;}return;
    }
    json(res,404,{error:'Unknown image endpoint'});
  }catch(error){json(res,error.status??503,{error:error.message});}
});
server.requestTimeout=20000;server.headersTimeout=5000;server.maxConnections=8;
server.listen(port,'127.0.0.1',()=>{
  console.log(`BOOX Max Images listening on 127.0.0.1:${port}; capture starts only from the desktop UI`);
});
server.on('error',error=>{console.error(error.message);process.exitCode=1;});
