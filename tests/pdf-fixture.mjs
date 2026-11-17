export function makePdfFixture(titleBytes,{extraTrailer='',metadata=false,extraInfo=''}={}) {
  const stream='BT /F1 16 Tf 30 100 Td (BOOX metadata test) Tj ET';
  const objects=[
    `<< /Type /Catalog /Pages 2 0 R${metadata?' /Metadata 5 0 R':''} >>`,
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
    `<< /Length ${stream.length} >>\nstream\n${stream}\nendstream`,
    `<< /Title <${titleBytes.toString('hex')}> /Producer (Synthetic Windows print fixture) ${extraInfo} >>`
  ];
  let content='%PDF-1.7\n',offsets=[0];
  objects.forEach((object,i)=>{offsets.push(Buffer.byteLength(content,'latin1'));content+=`${i+1} 0 obj\n${object}\nendobj\n`;});
  const xref=Buffer.byteLength(content,'latin1');
  content+=`xref\n0 ${objects.length+1}\n0000000000 65535 f \n`;
  for(const offset of offsets.slice(1))content+=`${String(offset).padStart(10,'0')} 00000 n \n`;
  content+=`trailer\n<< /Size ${objects.length+1} /Root 1 0 R /Info 6 0 R ${extraTrailer} >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(content,'latin1');
}
