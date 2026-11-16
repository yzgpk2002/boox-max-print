import http from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { createHash } from 'node:crypto';
import { parseIpp, first, response, printerAttributes, jobAttributes } from './ipp.mjs';
import { readWifiConfig, findReceiver, sendWifi } from './wifi.mjs';

const exec = promisify(execFile);
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const port = Number(process.env.BOOX_PORT ?? 8631);
const uri = `ipp://127.0.0.1:${port}/ipp/print`;
const spool = path.resolve(process.env.BOOX_SPOOL ?? path.join(root, 'spool'));
const adb = process.env.BOOX_ADB ?? 'E:\\codexblender\\film-001\\tools\\platform-tools\\adb.exe';
const serial = process.env.BOOX_SERIAL ?? '0123456789ABCDEF';
const maxBytes = 64 * 1024 * 1024;
const wifiConfigPath=process.env.BOOX_WIFI_CONFIG??path.join(root,'.local','wifi.json');
const jobs = new Map();
let nextId = 1, delivery = Promise.resolve();
await fs.mkdir(spool, { recursive: true });
// Never silently retry an interrupted job: the document may already be on the reader.
for (const name of await fs.readdir(spool)) {
  if (!/^job-\d+\.json$/.test(name)) continue;
  try {
    const job = JSON.parse(await fs.readFile(path.join(spool, name), 'utf8'));
    if (job.state < 7) { job.state = 8; job.reason = 'aborted-by-system'; job.message = 'Service restarted. Reprint if needed; original PDF retained.'; }
    jobs.set(job.id, job); nextId = Math.max(nextId, job.id + 1);
  } catch (error) { console.error('Ignored invalid job metadata:', name, error.message); }
}
const log = (event, details = {}) => console.log(JSON.stringify({ time: new Date().toISOString(), event, ...details }));
const save = job => fs.writeFile(path.join(spool, `job-${job.id}.json`), JSON.stringify(job, null, 2));
async function adbRun(args, timeout = 30000) {
  const result = await exec(adb, ['-s', serial, ...args], { timeout, maxBuffer: 1024 * 1024, windowsHide: true });
  return result.stdout.trim();
}
async function deliver(job) {
  if (job.state === 7) return;
  job.state = 5; job.reason = 'job-printing'; job.message = 'Sending PDF to BOOX'; await save(job);
  try {
    if (process.env.BOOX_TEST_MODE !== '1') {
      const config=await readWifiConfig(wifiConfigPath);
      const mode=process.env.BOOX_TRANSPORT??config?.mode??'auto';
      if(!['auto','wifi','usb'].includes(mode))throw new Error('Invalid transport mode');
      const receiver=config && mode!=='usb' ? await findReceiver(config,wifiConfigPath) : null;
      if(receiver){
        Object.assign(job,await sendWifi(job,spool,receiver,config));
      }else{
      if(mode==='wifi')throw new Error('Paired BOOX Wi-Fi receiver is unavailable. Check the receiver app and the shared Wi-Fi network.');
      if (await adbRun(['get-state']) !== 'device') throw new Error('BOOX is not connected or USB debugging is not authorized');
      // Do not change USB storage mode or network settings automatically.
      const writable = await adbRun(['shell', 'test -w /sdcard && echo writable']);
      if (!writable.includes('writable')) throw new Error('BOOX storage is unavailable. Turn off USB storage sharing on BOOX, keeping the USB cable connected, then print again.');
      const folder = '/sdcard/Documents/BooxMaxPrint';
      await adbRun(['shell', 'mkdir', '-p', folder]);
      const target = `${folder}/${job.file}`;
      await adbRun(['push', path.join(spool, job.file), `${target}.part`], 180000);
      if (job.state === 7) { await adbRun(['shell','rm','-f',`${target}.part`]); return; }
      await adbRun(['shell', 'mv', `${target}.part`, target]);
      // KitKat's toolbox has md5, but no wc. Check bytes by digest rather than
      // assuming modern Android shell utilities are installed.
      const remoteDigest = await adbRun(['shell', 'md5', target]);
      const expectedDigest = createHash('md5').update(await fs.readFile(path.join(spool, job.file))).digest('hex');
      if (remoteDigest.trim().split(/\s+/)[0].toLowerCase() !== expectedDigest) throw new Error('BOOX copy verification failed; local PDF retained');
      job.md5 = expectedDigest;
      job.devicePath = target;
      const result = await adbRun(['shell','am','start','-W','-a','android.intent.action.VIEW','-d',`file://${target}`,'-t','application/pdf','-n','com.onyx.kreader/.ui.ReaderTabHostActivity']);
      if (/Error:|Exception|Status:\s*(?!ok)\w+/i.test(result)) throw new Error(`PDF saved, but the reader could not open it: ${result.slice(0, 300)}`);
      job.transport='usb';
      }
    }
    job.state = 9; job.reason = 'job-completed-successfully'; job.message = 'PDF saved and reader opened';
    log('delivered', { job: job.id, bytes: job.bytes, transport:job.transport });
  } catch (error) {
    if (job.state !== 7) { job.state = 8; job.reason = 'aborted-by-system'; job.message = error.message; }
    log('delivery-error', { job: job.id, message: error.message });
  }
  await save(job);
}
function newJob(request) {
  const job = { id: nextId++, name: String(first(request, 'job-name', 'Document')).slice(0, 200), state: 4, reason: 'job-data-insufficient', message: 'Waiting for PDF', created: Date.now() };
  jobs.set(job.id, job);
  // Bound retained metadata in memory. PDFs and metadata remain on disk.
  if (jobs.size > 200) for (const [id, old] of jobs) { if (old.state >= 7) { jobs.delete(id); break; } }
  return job;
}
async function receivePdf(job, bytes) {
  if (bytes.length < 12 || !bytes.subarray(0, 8).toString('ascii').startsWith('%PDF-') || !bytes.subarray(Math.max(0, bytes.length - 4096)).includes(Buffer.from('%%EOF'))) throw new Error('Not a complete PDF document');
  const stamp = new Date().toISOString().replace(/[-:.TZ]/g, '').slice(0, 14);
  job.file = `BOOX-${stamp}-${job.id}.pdf`; job.bytes = bytes.length;
  await fs.writeFile(path.join(spool, `${job.file}.part`), bytes, { flag: 'wx' });
  await fs.rename(path.join(spool, `${job.file}.part`), path.join(spool, job.file));
  job.state = 3; job.reason = 'job-queued'; job.message = 'Queued for BOOX'; await save(job);
  delivery = delivery.then(() => deliver(job)).catch(error => log('queue-error', { message: error.message }));
}
function validate(request) {
  const format = first(request, 'document-format', 'application/pdf');
  if (!['application/pdf','application/octet-stream'].includes(format)) return [0x040a, `Only PDF is supported; received ${format}`];
  if (first(request, 'compression', 'none') !== 'none') return [0x040f, 'Compression is not supported'];
  if (first(request, 'copies', 1) !== 1 || first(request, 'sides', 'one-sided') !== 'one-sided') return [0x040b, 'Only one copy and one-sided output are supported'];
  return null;
}
export async function dispatch(request, body) {
  const op = request.operation;
  const error = [2,4,5,6].includes(op) ? validate(request) : null;
  if (error) return response(request, ...[error[0], [], error[1]]);
  if (op === 11) return response(request, 0, [printerAttributes(uri, Math.floor(process.uptime()))]);
  if (op === 4) return response(request);
  if (op === 5) { const job = newJob(request); await save(job); return response(request, 0, [jobAttributes(job, uri)]); }
  if (op === 2 || op === 6) {
    const job = op === 2 ? newJob(request) : jobs.get(Number(first(request, 'job-id', 0)));
    if (!job) return response(request, 0x0406, [], 'Unknown job');
    if (job.state !== 4) return response(request, 0x0404, [], 'Job already submitted or closed');
    if (op === 6 && first(request, 'last-document', true) !== true) return response(request, 0x0501, [], 'Only a single document is supported');
    try { await receivePdf(job, body); }
    catch (error) { job.state = 8; job.reason = 'document-format-error'; job.message = error.message; await save(job); return response(request, 0x0411, [], error.message); }
    return response(request, 0, [jobAttributes(job, uri)]);
  }
  if (op === 9 || op === 8) {
    const id = Number(first(request, 'job-id', String(first(request, 'job-uri', '')).split('/').pop()));
    const job = jobs.get(id);
    if (!job) return response(request, 0x0406, [], 'Unknown job');
    if (op === 8) {
      if (job.state >= 7 || job.state === 5) return response(request, 0x0404, [], 'Job already delivering or finished');
      job.state = 7; job.reason = 'job-canceled-by-user'; job.message = 'Canceled'; await save(job);
    }
    return response(request, 0, [jobAttributes(job, uri)]);
  }
  if (op === 10) {
    const which = first(request, 'which-jobs', 'not-completed');
    return response(request, 0, [...jobs.values()].filter(j => which === 'all' || (which === 'completed' ? j.state >= 7 : j.state < 7)).slice(-50).map(j=>jobAttributes(j,uri)));
  }
  return response(request, 0x0501, [], 'Operation not supported');
}

const server = http.createServer(async (req, res) => {
  // Loopback only. A web page cannot use this API cross-origin: no CORS, no raw upload endpoint.
  if (req.headers.origin) { res.writeHead(403); res.end('Cross-origin requests are not supported'); return; }
  if (req.method === 'GET' && req.url === '/health') { res.setHeader('Content-Type','application/json'); res.end(JSON.stringify({ name:'BOOX Max', uri, jobs:[...jobs.values()].slice(-10) })); return; }
  if (req.method === 'GET' && req.url === '/') { res.setHeader('Content-Type','text/plain; charset=utf-8'); res.end('BOOX Max PDF printer is running. Select BOOX Max in your application print dialog.'); return; }
  if (req.method !== 'POST' || req.url !== '/ipp/print' || !String(req.headers['content-type']).startsWith('application/ipp')) { res.writeHead(404); res.end(); return; }
  try {
    const chunks = []; let size = 0;
    for await (const chunk of req) { size += chunk.length; if (size > maxBytes) { res.writeHead(413); res.end('Job exceeds 64 MB'); return; } chunks.push(chunk); }
    const bytes = Buffer.concat(chunks), request = parseIpp(bytes);
    log('ipp', { operation: request.operation, request: request.id, format: first(request,'document-format',''), bytes:size, requested: request.operation === 11 ? request.attributes['requested-attributes'] : undefined });
    const reply = request.major === 1 || request.major === 2 ? await dispatch(request, bytes.subarray(request.bodyOffset)) : response({ ...request, major: 1, minor: 1 },0x0503,[],'Unsupported version');
    res.writeHead(200, { 'Content-Type':'application/ipp', 'Content-Length':reply.length }); res.end(reply);
  } catch(error) { log('request-error',{ message:error.message }); if (!res.headersSent) res.writeHead(400); res.end('Invalid IPP request'); }
});
server.requestTimeout = 180000;
server.headersTimeout = 15000;
server.maxConnections = 12;
server.listen(port, '127.0.0.1', () => log('listening', { uri, spool, serial }));
server.on('error', error => { console.error(error); process.exitCode = 1; });
setInterval(() => { for (const job of jobs.values()) if (job.state === 4 && Date.now()-job.created > 120000) { job.state=8; job.reason='aborted-by-system'; job.message='Document upload timed out'; save(job).catch(console.error); } }, 30000).unref();
