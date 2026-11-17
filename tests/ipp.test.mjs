import test from 'node:test';
import assert from 'node:assert/strict';
import { parseIpp, attr, attrs, TAG, printerAttributes, response } from '../desktop/ipp.mjs';
function request(...fields) { return Buffer.concat([Buffer.from([1,1,0,2,0,0,0,7,1]),...fields,Buffer.from([3])]); }
test('parses UTF-8 name and stops before binary PDF data', () => {
  const packet = Buffer.concat([request(attr(TAG.name,'job-name','\u4e2d\u6587\u6587\u6863'),attr(TAG.integer,'copies',1)), Buffer.from('%PDF-1.4')]);
  const p = parseIpp(packet);
  assert.equal(p.id,7); assert.equal(p.attributes['job-name'][0],'\u4e2d\u6587\u6587\u6863'); assert.equal(p.attributes.copies[0],1);
  assert.equal(packet.subarray(p.bodyOffset).toString(),'%PDF-1.4');
});
test('supports continuation attribute values',()=>assert.deepEqual(parseIpp(request(attrs(TAG.keyword,'requested-attributes',['printer-name','media-supported']))).attributes['requested-attributes'],['printer-name','media-supported']));
test('localized name/text values decode only their UTF-8 text and validate both byte lengths',()=>{
  const language=Buffer.from('zh-cn'),title=Buffer.from('\u4e2d\u6587\u6807\u9898');
  const len=value=>{const b=Buffer.alloc(2);b.writeUInt16BE(value);return b;};
  const value=Buffer.concat([len(language.length),language,len(title.length),title]);
  for(const tag of [TAG.nameWithLanguage,TAG.textWithLanguage])assert.equal(parseIpp(request(attr(tag,'job-name',value))).attributes['job-name'][0],'\u4e2d\u6587\u6807\u9898');
  for(const broken of [value.subarray(0,-1),Buffer.concat([value,Buffer.from([0])]),Buffer.from([255,255,0,0]),Buffer.from([0,0,0,1,255])])assert.throws(()=>parseIpp(request(attr(TAG.nameWithLanguage,'job-name',broken))));
});
test('rejects malformed or missing attribute terminators',()=>{
  for (const b of [Buffer.alloc(2),Buffer.from([1,1,0,2,0,0,0,1,0x42,0xff,0xff]),request(attr(TAG.name,'x','y')).subarray(0,-1)]) assert.throws(()=>parseIpp(b));
});
test('advertises only implemented PDF format and valid media collections',()=>{
  const r = {major:1,minor:1,id:1};
  const bytes = response(r,0,[printerAttributes('ipp://127.0.0.1:8631/ipp/print',10)]);
  const p = parseIpp(bytes);
  assert.deepEqual(p.attributes['document-format-supported'],['application/pdf']);
  assert.deepEqual(p.attributes['operations-supported'],[2,4,5,6,8,9,10,11]);
  assert.equal(p.bodyOffset,bytes.length);
  assert.equal(p.attributes['printer-name'][0],'BOOX Max');
});
