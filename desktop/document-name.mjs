// A readable device filename is separate from the stable ASCII transport/job name.
export function documentTitle(value) {
  let title=String(value??'Document').toWellFormed().split(/[\\/]/).pop().normalize('NFC');
  title=title.replace(/\.pdf$/i,'').replace(/[<>:"/\\|?*\u0000-\u001f\u007f-\u009f\u202a-\u202e\u2066-\u2069\ufffd]/g,'_').replace(/\s+/gu,' ').replace(/^[ .]+|[ .]+$/g,'');
  return title||'Document';
}
export function makeDocumentName(title,jobFile) {
  if(!/^BOOX-\d{14}-\d+\.pdf$/.test(jobFile))throw new Error('Invalid PDF job filename');
  const suffix=' -- '+jobFile,budget=220-Buffer.byteLength(suffix,'utf8');
  if(budget<8)throw new Error('PDF job filename is too long');
  let prefix='',used=0;
  for(const c of documentTitle(title)){const size=Buffer.byteLength(c,'utf8');if(used+size>budget)break;prefix+=c;used+=size;}
  return prefix.trimEnd()+suffix;
}
export function validateDocumentName(name,jobFile) {
  if(!/^BOOX-\d{14}-\d+\.pdf$/.test(jobFile)||typeof name!=='string'||name.length<=jobFile.length+4||!name.endsWith(' -- '+jobFile)||Buffer.byteLength(name,'utf8')>220||name!==name.toWellFormed()||name!==name.normalize('NFC')||/[<>:"/\\|?*\u0000-\u001f\u007f-\u009f\u202a-\u202e\u2066-\u2069\ufffd]/.test(name)||/^[ .]|[ .]$/.test(name))throw new Error('Invalid readable PDF filename');
  return name;
}
export function shellQuote(value) {return "'"+String(value).replace(/'/g,"'\\''")+"'";}
export function androidFileUri(devicePath) {return 'file://'+devicePath.split('/').map(p=>encodeURIComponent(p).replace(/'/g,'%27')).join('/');}
