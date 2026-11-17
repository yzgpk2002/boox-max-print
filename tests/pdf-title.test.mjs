import test from 'node:test';
import assert from 'node:assert/strict';
import {repairPdfTitle} from '../desktop/pdf-title.mjs';
import {makePdfFixture} from './pdf-fixture.mjs';
const source='C:\\private\\\u4e2d\u6587 (O\'Brien).pdf';
test('repairs only proven Windows title low-byte loss using an incremental Info object',()=>{
  const original=makePdfFixture(Buffer.from(source,'latin1'));
  const result=repairPdfTitle(original,source);assert.equal(result.changed,true);
  assert.deepEqual(result.bytes.subarray(0,original.length),original,'all original objects and page bytes remain identical');
  const tail=result.bytes.subarray(original.length).toString('ascii');
  const expected=Buffer.from("\u4e2d\u6587 (O'Brien)",'utf16le').swap16().toString('hex').toUpperCase();
  assert.ok(tail.includes('/Title <FEFF'+expected+'>'));assert.match(tail,/\/Producer \(Synthetic Windows print fixture\)/);
  assert.match(tail,/\/Prev \d+/);assert.match(tail,/\/Size 8/);assert.match(tail,/\/Info 7 0 R/);
  const xref=Number(tail.match(/startxref\s+(\d+)/)[1]);assert.equal(result.bytes.toString('ascii',xref,xref+4),'xref');
  assert.equal(repairPdfTitle(result.bytes,source).changed,false,'second processing must not append another repair');
});
test('normal, unrelated, signed, encrypted, XMP and unsupported PDFs remain byte-for-byte unchanged',()=>{
  const utf16=Buffer.concat([Buffer.from([254,255]),Buffer.from(source,'utf16le').swap16()]);
  const candidates=[makePdfFixture(utf16),makePdfFixture(Buffer.from('unrelated')),makePdfFixture(Buffer.from(source,'latin1'),{extraTrailer:'/Encrypt 7 0 R'}),makePdfFixture(Buffer.from(source,'latin1'),{extraInfo:'/ByteRange [0 1 2 3]'}),makePdfFixture(Buffer.from(source,'latin1'),{metadata:true}),makePdfFixture(Buffer.from(source,'latin1'),{extraTrailer:'/Prev 123'}),Buffer.from('%PDF-1.7\nunsupported\n%%EOF')];
  for(const original of candidates){const result=repairPdfTitle(original,source);assert.equal(result.changed,false,result.reason);assert.strictEqual(result.bytes,original);}
});
