// Conservative metadata-only repair for one observed Windows print-driver bug:
// /Title equals the low eight bits of every UTF-16 code unit of the intact IPP
// job name. Unknown, signed, encrypted, XMP and non-classic PDFs pass unchanged.
// No existing byte is rewritten: a new Info object and classic xref are appended.
import { documentTitle } from './document-name.mjs';

const whitespace=c=>c===0||c===9||c===10||c===12||c===13||c===32;
const delimiter=c=>whitespace(c)||[40,41,60,62,91,93,123,125,47,37].includes(c);
class Reader {
  constructor(bytes,offset=0,limit=bytes.length){this.bytes=bytes;this.pos=offset;this.limit=limit;this.count=0;}
  skip(){while(this.pos<this.limit){if(whitespace(this.bytes[this.pos]))this.pos++;else if(this.bytes[this.pos]===37){while(this.pos<this.limit&&![10,13].includes(this.bytes[this.pos]))this.pos++;}else break;}}
  word(){this.skip();const start=this.pos;while(this.pos<this.limit&&!delimiter(this.bytes[this.pos]))this.pos++;if(start===this.pos)throw Error('Expected PDF token');return this.bytes.toString('ascii',start,this.pos);}
  node(depth=0){
    if(depth>12||++this.count>4096)throw Error('PDF metadata nesting limit');
    this.skip();const start=this.pos,c=this.bytes[this.pos++];if(this.pos>this.limit)throw Error('Truncated PDF value');
    let node;
    if(c===47){
      while(this.pos<this.limit&&!delimiter(this.bytes[this.pos]))this.pos++;
      const value=this.bytes.toString('latin1',start+1,this.pos).replace(/#([0-9a-f]{2})/gi,(_,h)=>String.fromCharCode(parseInt(h,16)));node={type:'name',value};
    }else if(c===40){
      const data=[];let nesting=1;
      while(this.pos<this.limit&&nesting){let b=this.bytes[this.pos++];
        if(b===92){if(this.pos>=this.limit)throw Error('Truncated string escape');b=this.bytes[this.pos++];
          if(b>=48&&b<=55){let oct=String.fromCharCode(b);for(let i=0;i<2&&this.bytes[this.pos]>=48&&this.bytes[this.pos]<=55;i++)oct+=String.fromCharCode(this.bytes[this.pos++]);data.push(parseInt(oct,8)&255);}
          else if(b===13){if(this.bytes[this.pos]===10)this.pos++;}else if(b!==10)data.push(({110:10,114:13,116:9,98:8,102:12})[b]??b);
        }else if(b===40){nesting++;data.push(b);}else if(b===41){if(--nesting)data.push(b);}else if(b===13){if(this.bytes[this.pos]===10)this.pos++;data.push(10);}else data.push(b);
      }
      if(nesting)throw Error('Unterminated PDF string');node={type:'string',value:Buffer.from(data)};
    }else if(c===60&&this.bytes[this.pos]!==60){
      let hex='';while(this.pos<this.limit&&this.bytes[this.pos]!==62){const b=this.bytes[this.pos++];if(whitespace(b))continue;if(!/[0-9a-f]/i.test(String.fromCharCode(b)))throw Error('Invalid PDF hex string');hex+=String.fromCharCode(b);}
      if(this.bytes[this.pos++]!==62)throw Error('Unterminated PDF hex string');node={type:'string',value:Buffer.from(hex.length%2?hex+'0':hex,'hex')};
    }else if(c===60&&this.bytes[this.pos]===60){
      this.pos++;const entries=new Map();
      while(true){this.skip();if(this.bytes[this.pos]===62&&this.bytes[this.pos+1]===62){this.pos+=2;break;}
        const key=this.node(depth+1);if(key.type!=='name'||entries.has(key.value))throw Error('Invalid or duplicate PDF dictionary key');entries.set(key.value,this.node(depth+1));}
      node={type:'dict',entries};
    }else if(c===91){
      const values=[];while(true){this.skip();if(this.bytes[this.pos]===93){this.pos++;break;}values.push(this.node(depth+1));}node={type:'array',values};
    }else{
      this.pos=start;const word=this.word();
      if(/^[+-]?(?:\d+\.?\d*|\.\d+)$/.test(word)){
        const value=Number(word);if(!Number.isFinite(value))throw Error('Invalid PDF number');node={type:'number',value};
        // Only integer integer R denotes an indirect reference; restore lookahead.
        if(/^\d+$/.test(word)){const after=this.pos;try{const generation=this.word();if(/^\d+$/.test(generation)&&this.word()==='R')node={type:'ref',object:value,generation:Number(generation)};else this.pos=after;}catch{this.pos=after;}}
      }else if(['true','false','null'].includes(word))node={type:word};else throw Error('Unsupported PDF metadata token');
    }
    return {...node,start,end:this.pos};
  }
}
function xrefAt(bytes,offset){
  if(!Number.isSafeInteger(offset)||offset<0||offset>=bytes.length)throw Error('Invalid xref offset');
  const reader=new Reader(bytes,offset);if(reader.word()!=='xref')throw Error('Unsupported xref stream');
  const rows=new Map();let total=0;
  while(true){reader.skip();if(bytes.toString('ascii',reader.pos,reader.pos+7)==='trailer'){reader.pos+=7;break;}
    const firstWord=reader.word(),countWord=reader.word();if(!/^\d+$/.test(firstWord)||!/^\d+$/.test(countWord))throw Error('Invalid xref subsection');
    const first=Number(firstWord),count=Number(countWord);if(!Number.isSafeInteger(first)||count<1||(total+=count)>200000)throw Error('Unsupported xref size');
    for(let i=0;i<count;i++){
      const offsetWord=reader.word(),genWord=reader.word(),flag=reader.word();
      if(!/^\d{10}$/.test(offsetWord)||!/^\d{5}$/.test(genWord)||!['n','f'].includes(flag)||rows.has(first+i))throw Error('Invalid xref entry');
      const entry={offset:Number(offsetWord),generation:Number(genWord),active:flag==='n'};if(entry.active&&(entry.offset>=offset||entry.generation>65535))throw Error('Invalid xref object address');rows.set(first+i,entry);
    }
  }
  const trailer=reader.node();if(trailer.type!=='dict')throw Error('Invalid trailer');return {rows,trailer};
}
function indirectDictionary(bytes,ref,rows,xrefOffset){
  if(ref?.type!=='ref')throw Error('Missing indirect reference');const row=rows.get(ref.object);
  if(!row?.active||row.generation!==ref.generation)throw Error('Unresolved PDF reference');
  const reader=new Reader(bytes,row.offset,Math.min(xrefOffset,row.offset+1024*1024));
  if(reader.word()!==String(ref.object)||reader.word()!==String(ref.generation)||reader.word()!=='obj')throw Error('Xref target mismatch');
  const node=reader.node();if(node.type!=='dict'||reader.word()!=='endobj')throw Error('Unsupported dictionary object');return node;
}
function unicodeHex(value){const bytes=Buffer.from(value,'utf16le');bytes.swap16();return '<FEFF'+bytes.toString('hex').toUpperCase()+'>';}
function editDictionary(bytes,dict,replacements){
  // Keep all unknown Info entries byte-for-byte; replace only selected values.
  const edits=[];for(const [name,value] of Object.entries(replacements)){const field=dict.entries.get(name);if(!field)throw Error('Required dictionary field absent');edits.push({start:field.start,end:field.end,value});}
  edits.sort((a,b)=>a.start-b.start);const parts=[];let offset=dict.start;
  for(const edit of edits){parts.push(bytes.subarray(offset,edit.start),Buffer.from(edit.value,'ascii'));offset=edit.end;}
  parts.push(bytes.subarray(offset,dict.end));return Buffer.concat(parts);
}

export function repairPdfTitle(bytes,ippTitle){
  const unchanged=reason=>({bytes,changed:false,reason});
  const source=String(ippTitle??'');if(!/[^\x00-\xff]/.test(source))return unchanged('no-non-latin-title');
  if(bytes.length>64*1024*1024||!bytes.subarray(0,8).toString('ascii').startsWith('%PDF-'))return unchanged('unsupported-pdf');
  try {
    // Conservative signatures exclusion; false positives simply leave the PDF intact.
    const literal=bytes.toString('latin1');if(/\/ByteRange\b|\/Type\s*\/Sig\b/.test(literal))return unchanged('signed-pdf');
    const end=/startxref\s+(\d+)\s+%%EOF\s*$/.exec(literal.slice(-4096));if(!end)return unchanged('unsupported-trailer');
    const previous=Number(end[1]),{rows,trailer}=xrefAt(bytes,previous),t=trailer.entries;
    if(t.has('Encrypt'))return unchanged('encrypted-pdf');
    if(t.has('Prev')||t.has('XRefStm'))return unchanged('incremental-or-hybrid-pdf');
    const size=t.get('Size');if(size?.type!=='number'||!Number.isSafeInteger(size.value)||size.value<2||size.value>200000||[...rows.keys()].some(id=>id>=size.value))return unchanged('invalid-xref-size');
    const freeHead=rows.get(0);if(!freeHead||freeHead.active||freeHead.generation!==65535)return unchanged('unsupported-free-list');
    const root=indirectDictionary(bytes,t.get('Root'),rows,previous);if(root.entries.has('Metadata'))return unchanged('xmp-metadata-present');
    const info=indirectDictionary(bytes,t.get('Info'),rows,previous),title=info.entries.get('Title');
    if(title?.type!=='string'||!title.value.equals(Buffer.from(source,'latin1')))return unchanged('title-not-known-driver-corruption');
    const fixedInfo=editDictionary(bytes,info,{Title:unicodeHex(documentTitle(source))});
    const infoStart=bytes.length+1,newObject=Buffer.concat([Buffer.from(`\n${size.value} 0 obj\n`,'ascii'),fixedInfo,Buffer.from('\nendobj\n','ascii')]);
    const newXref=bytes.length+newObject.length;
    if(newXref>9999999999)throw Error('PDF offset overflow');
    // Preserve trailer keys such as ID. Prev links this incremental section to
    // the original xref; original pages, objects and byte offsets remain intact.
    const rewrittenTrailer=editDictionary(bytes,trailer,{Size:String(size.value+1),Info:`${size.value} 0 R`});
    const trailerWithPrev=Buffer.concat([rewrittenTrailer.subarray(0,-2),Buffer.from(`\n/Prev ${previous}\n>>`,'ascii')]);
    const tail=Buffer.concat([Buffer.from(`xref\n0 1\n${String(freeHead.offset).padStart(10,'0')} 65535 f \n${size.value} 1\n${String(infoStart).padStart(10,'0')} 00000 n \ntrailer\n`,'ascii'),trailerWithPrev,Buffer.from(`\nstartxref\n${newXref}\n%%EOF\n`,'ascii')]);
    if(bytes.length+newObject.length+tail.length>64*1024*1024)return unchanged('repair-exceeds-size-limit');
    return {bytes:Buffer.concat([bytes,newObject,tail]),changed:true,reason:'repaired-windows-title-low-byte-loss'};
  }catch{return unchanged('unsupported-pdf-structure');}
}
