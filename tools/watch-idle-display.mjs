// Observe an existing full-screen session while the operator reproduces the idle transition.
// Run before triggering it. Keep a stable, non-black page on BOOX for at least five seconds.
import fs from 'node:fs/promises';
import {setTimeout as delay} from 'node:timers/promises';

const config = JSON.parse(await fs.readFile('.local/image-client.json', 'utf8'));
const log = 'logs/sender.log';
let offset = (await fs.stat(log)).size;
let partial = '', baseline, heldAt, samples = 0, confirmed = false;
let generation, stableSince;
const deadline = Date.now() + 20 * 60_000;
console.log('Waiting for live BOOX frames and a Windows display/lock/screensaver event');
while (Date.now() < deadline) {
  const response = await fetch(config.url + '/state', {
    headers: {'X-Boox-Client': config.key}, signal: AbortSignal.timeout(5000)
  });
  if (!response.ok) throw new Error('Receiver state unavailable: ' + response.status);
  const state = await response.json();
  const file = await fs.open(log);
  let lines;
  try {
    const size = (await file.stat()).size;
    if (size < offset) throw new Error('Sender log rotated during test');
    const buffer = Buffer.alloc(size - offset);
    const {bytesRead} = await file.read(buffer, 0, buffer.length, offset);
    offset += bytesRead;
    lines = (partial + buffer.toString('utf8', 0, bytesRead)).split('\n');
    partial = lines.pop();
  } finally { await file.close(); }

  const active = !state.paused && state.viewerVisible && !!state.sha256;
  const stamp = [state.session, state.sequence, state.sha256].join('/');
  for (const line of lines) {
    if (!heldAt && / display off\s*$| session locked\s*$|capture held: (system-screensaver-running|visible-screensaver-window); source=desktop/.test(line)) {
      if (!baseline || Date.now() - stableSince < 5000) throw new Error('No stable pre-event receiver baseline');
      heldAt = Date.now();
      console.log('Windows event observed. Comparing BOOX receipts for 90 seconds');
    }
    if (heldAt && / display on\s*$| session unlocked\s*$|capture resumed; source=desktop/.test(line)) {
      if (!confirmed) throw new Error('Windows resumed before 90 seconds were confirmed');
      console.log('Windows resumed. Change the page to verify delivery');
      generation = 'resumed';
    }
  }
  if (!heldAt) {
    if (!active || baseline !== stamp) stableSince = Date.now();
    baseline = active ? stamp : null;
  } else if (generation === 'resumed' && active && state.session === baseline.split('/')[0] && stamp !== baseline) {
    console.log('PASS: active receiver retained the pre-event frame through 90 seconds and received a changed frame after resume');
    process.exit(0);
  } else {
    if (!active || stamp !== baseline) throw new Error('Receiver session, visibility or frame changed during hold');
    samples++;
    if (!confirmed && Date.now() - heldAt >= 90_000) {
      confirmed = true;
      console.log('90 seconds confirmed, samples=' + samples + '. Wake/unlock and change the page');
    }
  }
  await delay(750);
}
throw new Error('Timed out before the complete hold and resume sequence');
