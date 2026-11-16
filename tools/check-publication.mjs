// Audit the Git index, never print matched secrets or file contents.
import fs from 'node:fs/promises';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
const tracked=execFileSync('git',['ls-files','--cached','-z'],{encoding:'utf8'}).split('\0').filter(Boolean);
if(!tracked.length)throw new Error('Stage the intended publication files before running this audit');
const secrets=[];
for(const name of ['wifi.json','image-client.json']) {
  try {const value=JSON.parse(await fs.readFile(path.join('.local',name),'utf8'));for(const key of ['token','key','deviceId'])if(typeof value[key]==='string'&&value[key].length>=16)secrets.push(value[key]);}
  catch(error){if(error.code!=='ENOENT')throw new Error('Cannot read local configuration for publication audit');}
}
const issues=[];
for(const file of tracked) {
  if(/^(?:\.local|build|dist|spool|logs|node_modules)\//.test(file)||/\.(?:keystore|jks|pem|key|p12|pfx|lnk|pdf|apk|exe|log)$/i.test(file)||/(?:^|\/)\.env(?:\.|$)/.test(file))issues.push({file,reason:'local/private/generated path'});
  const data=execFileSync('git',['show',':'+file],{maxBuffer:8*1024*1024});
  for(const secret of secrets)if(data.includes(Buffer.from(secret)))issues.push({file,reason:'actual local credential or device identity'});
  if(/\.(?:ico|png)$/.test(file)) {
    if(!file.startsWith('desktop/assets/'))issues.push({file,reason:'unreviewed image outside designed icon assets'});
    continue;
  }
  const text=data.toString('utf8');
  if(/(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)/.test(text))issues.push({file,reason:'credential-like content'});
  if(file.endsWith('.md'))for(const match of text.matchAll(/\]\(([^)]+)\)/g)) {
    const href=match[1];if(/^(?:https?:|mailto:|#)/.test(href))continue;
    const target=decodeURIComponent(href.split('#')[0]);
    try{await fs.access(path.resolve(path.dirname(file),target));}catch{issues.push({file,reason:'broken local documentation link: '+target});}
  }
}
console.log(JSON.stringify({files:tracked.length,localSecretsChecked:secrets.length,issues},null,2));
if(issues.length)process.exitCode=1;
