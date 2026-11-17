import test from 'node:test';
import assert from 'node:assert/strict';
import {documentTitle,makeDocumentName,validateDocumentName,shellQuote,androidFileUri} from '../desktop/document-name.mjs';
const job='BOOX-20261116000000-12.pdf';
test('readable names preserve Unicode, remove computer paths and retain unique transport identity',()=>{
  const source='C:\\private\\folder\\\u4e2d\u6587\u9605\u8bfb (O\'Brien).pdf';
  const name=makeDocumentName(source,job);
  assert.equal(name,"\u4e2d\u6587\u9605\u8bfb (O'Brien) -- "+job);
  assert.equal(validateDocumentName(name,job),name);
  assert.equal(documentTitle('/private/\u6587\u7ae0.pdf'),'\u6587\u7ae0');
  const long=makeDocumentName('\u4e2d\ud83d\ude00'.repeat(300),job);
  assert.ok(Buffer.byteLength(long)<=220);assert.ok(!long.includes('\ufffd'));assert.ok(long.endsWith(job));
  assert.equal(makeDocumentName('e\u0301.pdf',job),makeDocumentName('\u00e9.pdf',job));
  assert.notEqual(makeDocumentName('same',job),makeDocumentName('same',job.replace('-12.','-13.')));
});
test('hostile names cannot escape the device folder or inject the Android shell',()=>{
  const name=makeDocumentName('unsafe\u0000<name>|:?.pdf',job);assert.doesNotMatch(name,/[\u0000<>|:?]/);
  for(const name of ['../outside.pdf','x -- '+job+'\r\nHeader: value','x/ -- '+job,'.hidden -- '+job,'x\u202e -- '+job])assert.throws(()=>validateDocumentName(name,job));
  assert.equal(shellQuote("a'b$(echo bad)"),"'a'\\''b$(echo bad)'");
  const uri=androidFileUri("/sdcard/\u4e2d\u6587 O'Brien.pdf");
  assert.equal(uri,'file:///sdcard/%E4%B8%AD%E6%96%87%20O%27Brien.pdf');
});
