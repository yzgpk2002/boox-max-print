// IPP binary encoding (RFC 8010). No native or third-party dependencies.
export const TAG = { integer: 0x21, boolean: 0x22, enum: 0x23, textWithLanguage: 0x35, nameWithLanguage: 0x36, text: 0x41, name: 0x42, keyword: 0x44, uri: 0x45, charset: 0x47, language: 0x48, mime: 0x49 };
export function parseIpp(data) {
  if (data.length < 9) throw new Error('Truncated IPP header');
  const request = { major: data[0], minor: data[1], operation: data.readUInt16BE(2), id: data.readInt32BE(4), attributes: {}, bodyOffset: 0 };
  let offset = 8, name = '', depth = 0, fields = 0;
  while (offset < data.length && offset < 65536) {
    const tag = data[offset++];
    if (tag === 3 && depth === 0) { request.bodyOffset = offset; return request; }
    if (tag < 0x10) { name = ''; continue; }
    if (++fields > 2048 || offset + 2 > data.length) throw new Error('Invalid attributes');
    const nl = data.readUInt16BE(offset); offset += 2;
    if (offset + nl + 2 > data.length) throw new Error('Truncated attribute name');
    if (nl) name = data.toString('utf8', offset, offset + nl);
    offset += nl;
    const vl = data.readUInt16BE(offset); offset += 2;
    if (offset + vl > data.length) throw new Error('Truncated attribute value');
    const raw = data.subarray(offset, offset + vl); offset += vl;
    if (tag === 0x34) { depth++; continue; }
    if (tag === 0x37) { depth--; if (depth < 0) throw new Error('Invalid collection'); continue; }
    if (depth) continue;
    let value = raw.toString('utf8');
    // RFC 8010: localized values contain language-length, language, text-length,
    // text. Treating the whole value as UTF-8 leaks binary lengths into titles.
    if(tag===TAG.textWithLanguage||tag===TAG.nameWithLanguage) {
      if(raw.length<4)throw new Error('Truncated localized IPP value');
      const languageLength=raw.readUInt16BE(0),textOffset=languageLength+4;
      if(languageLength>32767||textOffset>raw.length)throw new Error('Invalid IPP language length');
      const textLength=raw.readUInt16BE(languageLength+2);
      if(textLength>32767||textOffset+textLength!==raw.length)throw new Error('Invalid localized IPP text length');
      value=new TextDecoder('utf-8',{fatal:true}).decode(raw.subarray(textOffset));
    }
    if ((tag === TAG.integer || tag === TAG.enum) && vl === 4) value = raw.readInt32BE();
    if (tag === TAG.boolean && vl === 1) value = raw[0] !== 0;
    if (name) (request.attributes[name] ??= []).push(value);
  }
  throw new Error('Missing IPP end-of-attributes');
}
export const first = (request, name, fallback) => request.attributes[name]?.[0] ?? fallback;
export function int(value) { const b = Buffer.alloc(4); b.writeInt32BE(value); return b; }
export function attr(tag, name, value) {
  const key = Buffer.from(name, 'utf8');
  const bytes = Buffer.isBuffer(value) ? value : tag === TAG.integer || tag === TAG.enum ? int(value) : tag === TAG.boolean ? Buffer.from([value ? 1 : 0]) : Buffer.from(String(value), 'utf8');
  const head = Buffer.alloc(3); head[0] = tag; head.writeUInt16BE(key.length, 1);
  const size = Buffer.alloc(2); size.writeUInt16BE(bytes.length);
  return Buffer.concat([head, key, size, bytes]);
}
export function attrs(tag, name, values) { return Buffer.concat(values.map((v, i) => attr(tag, i ? '' : name, v))); }
export function response(request, status = 0, groups = [], message) {
  const header = Buffer.alloc(8); header[0] = request.major; header[1] = request.minor;
  header.writeUInt16BE(status, 2); header.writeInt32BE(request.id, 4);
  return Buffer.concat([header, Buffer.from([1]), attr(TAG.charset, 'attributes-charset', 'utf-8'), attr(TAG.language, 'attributes-natural-language', 'en'), ...(message ? [attr(TAG.text, 'status-message', message)] : []), ...groups, Buffer.from([3])]);
}
function mediaCollection(name, media) {
  const member = (key, tag, value) => Buffer.concat([attr(0x4a, '', key), attr(tag, '', value)]);
  return Buffer.concat([
    attr(0x34, name, Buffer.alloc(0)), member('media-size-name', TAG.keyword, media[0]),
    attr(0x4a, '', 'media-size'), attr(0x34, '', Buffer.alloc(0)), member('x-dimension', TAG.integer, media[1]), member('y-dimension', TAG.integer, media[2]), attr(0x37, '', Buffer.alloc(0)),
    ...['bottom','left','right','top'].map(side => member(`media-${side}-margin`, TAG.integer, 0)),
    member('media-source', TAG.keyword, 'main'), member('media-type', TAG.keyword, 'stationery'), attr(0x37, '', Buffer.alloc(0))
  ]);
}
export function printerAttributes(uri, uptime, ready = true) {
  const a = (tag, name, value) => attr(TAG[tag], name, value);
  const many = (tag, name, value) => attrs(TAG[tag], name, value);
  const media = [['iso_a4_210x297mm',21000,29700], ['iso_a5_148x210mm',14800,21000], ['na_letter_8.5x11in',21590,27940]];
  const res = Buffer.concat([int(300), int(300), Buffer.from([3])]);
  return Buffer.concat([Buffer.from([4]),
    a('uri','printer-uri-supported',uri), a('uri','printer-uuid','urn:uuid:fcb4f604-4024-4b14-bbf9-361114e152ef'),
    a('keyword','uri-authentication-supported','none'), a('keyword','uri-security-supported','none'),
    a('name','printer-name','BOOX Max'), a('text','printer-info','Print documents to BOOX Max as PDF'), a('text','printer-make-and-model','BOOX Max PDF'), a('text','printer-location','Wi-Fi / USB BOOX Max'),
    a('uri','printer-more-info',uri.replace(/^ipp:/,'http:').replace('/ipp/print','/')),
    a('enum','printer-state',ready ? 3 : 5), a('keyword','printer-state-reasons',ready ? 'none' : 'connecting-to-device'),
    a('boolean','printer-is-accepting-jobs',true), a('integer','queued-job-count',0), a('integer','printer-up-time',uptime),
    many('keyword','ipp-versions-supported',['1.1','2.0']), many('enum','operations-supported',[2,4,5,6,8,9,10,11]),
    a('charset','charset-configured','utf-8'), a('charset','charset-supported','utf-8'), a('language','natural-language-configured','en'), a('language','generated-natural-language-supported','en'),
    a('mime','document-format-default','application/pdf'), a('mime','document-format-supported','application/pdf'), many('keyword','pdf-versions-supported',['adobe-1.3','adobe-1.4','adobe-1.5','adobe-1.6','adobe-1.7']),
    a('keyword','compression-supported','none'), a('keyword','pdl-override-supported','not-attempted'), a('boolean','color-supported',true),
    many('keyword','print-color-mode-supported',['monochrome','color']), a('keyword','print-color-mode-default','monochrome'),
    a('keyword','sides-supported','one-sided'), a('keyword','sides-default','one-sided'),
    many('enum','orientation-requested-supported',[3,4]), a('enum','orientation-requested-default',3),
    a('integer','copies-default',1), attr(0x33,'copies-supported',Buffer.concat([int(1),int(1)])), a('boolean','page-ranges-supported',false),
    a('enum','print-quality-default',4), a('enum','print-quality-supported',4),
    attr(0x32,'printer-resolution-default',res), attr(0x32,'printer-resolution-supported',res),
    many('keyword','media-supported',media.map(m=>m[0])), a('keyword','media-default',media[0][0]), a('keyword','media-ready',media[0][0]),
    mediaCollection('media-col-default',media[0]), mediaCollection('media-col-ready',media[0]), ...media.map((m,i)=>mediaCollection(i ? '' : 'media-col-database',m)),
    many('keyword','media-col-supported',['media-size','media-size-name','media-top-margin','media-bottom-margin','media-left-margin','media-right-margin','media-source','media-type']),
    ...['bottom','left','right','top'].map(side=>a('integer',`media-${side}-margin-supported`,0)),
    a('keyword','media-source-supported','main'), a('keyword','media-type-supported','stationery'),
    a('keyword','output-bin-supported','face-up'), a('keyword','output-bin-default','face-up'),
    a('boolean','multiple-document-jobs-supported',false), a('keyword','multiple-document-handling-default','separate-documents-uncollated-copies'), a('keyword','multiple-document-handling-supported','separate-documents-uncollated-copies'),
    a('integer','multiple-operation-time-out',120), a('keyword','multiple-operation-time-out-action','abort-job'),
    a('keyword','job-hold-until-default','no-hold'), a('keyword','job-hold-until-supported','no-hold'),
    a('integer','job-priority-default',50), a('integer','job-priority-supported',1),
    a('enum','finishings-default',3), a('enum','finishings-supported',3),
    many('keyword','job-creation-attributes-supported',['document-format','job-name','media','media-col','orientation-requested','print-color-mode','printer-resolution','print-quality','sides','copies'])
  ]);
}
export function jobAttributes(job, uri) {
  return Buffer.concat([Buffer.from([2]), attr(TAG.integer,'job-id',job.id), attr(TAG.uri,'job-uri',`${uri}/jobs/${job.id}`), attr(TAG.uri,'job-printer-uri',uri), attr(TAG.name,'job-name',job.name), attr(TAG.enum,'job-state',job.state), attr(TAG.keyword,'job-state-reasons',job.reason), attr(TAG.text,'job-state-message',job.message ?? ''), attr(TAG.integer,'job-impressions-completed',0)]);
}
