package net.booxmax.print;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.charset.Charset;
import java.nio.charset.CodingErrorAction;
import java.text.Normalizer;

/** No Android dependencies: strict UTF-8 header and path validation is unit-testable. */
public final class DocumentNames {
    public static String decode(String encoded,String jobName)throws Exception {
        if(!jobName.matches("BOOX-[0-9]{14}-[0-9]+\\.pdf"))throw new IOException("Invalid job name");
        if(encoded==null)return jobName; // Legacy senders keep the old name.
        if(encoded.length()<1||encoded.length()>1500)throw new IOException("Invalid document name length");
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();
        for(int i=0;i<encoded.length();i++) {
            char c=encoded.charAt(i);
            if(c=='%') {
                if(i+2>=encoded.length())throw new IOException("Invalid document name escape");
                int hi=Character.digit(encoded.charAt(++i),16),lo=Character.digit(encoded.charAt(++i),16);
                if(hi<0||lo<0)throw new IOException("Invalid document name escape");bytes.write((hi<<4)|lo);
            }else {
                if(!((c>='a'&&c<='z')||(c>='A'&&c<='Z')||(c>='0'&&c<='9')||"-_.!~*'()".indexOf(c)>=0))throw new IOException("Non-encoded document name header");
                bytes.write(c);
            }
        }
        String name=Charset.forName("UTF-8").newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes.toByteArray())).toString();
        String suffix=" -- "+jobName;
        if(!name.endsWith(suffix)||name.length()<=suffix.length()||bytes.size()>220||!Normalizer.isNormalized(name,Normalizer.Form.NFC)||name.startsWith(".")||name.startsWith(" ")||name.endsWith(" ")||name.endsWith("."))throw new IOException("Unsafe document name");
        for(int i=0;i<name.length();i++) {
            char c=name.charAt(i);
            if(c<32||(c>=127&&c<=159)||(c>=0x202a&&c<=0x202e)||(c>=0x2066&&c<=0x2069)||c==0xfffd||"<>:\"/\\|?*".indexOf(c)>=0)throw new IOException("Unsafe document name");
        }
        return name;
    }
}
