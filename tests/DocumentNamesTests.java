import net.booxmax.print.DocumentNames;
import java.net.URLEncoder;
public class DocumentNamesTests {
    static void check(boolean value)throws Exception{if(!value)throw new Exception("Document name assertion failed");}
    static void reject(String value,String job)throws Exception{try{DocumentNames.decode(value,job);}catch(Exception expected){return;}throw new Exception("Unsafe name accepted");}
    static String encode(String text)throws Exception{return URLEncoder.encode(text,"UTF-8").replace("+","%20");}
    public static void main(String[] args)throws Exception {
        String job="BOOX-20261116000000-12.pdf",name="\u4e2d\u6587 O'Brien -- "+job;
        check(DocumentNames.decode(encode(name),job).equals(name));check(DocumentNames.decode(null,job).equals(job));
        reject(encode("../outside -- "+job),job);reject(encode("bad\n -- "+job),job);reject("%C0%AF",job);reject("%GG",job);reject(encode("wrong.pdf"),job);
        StringBuilder longName=new StringBuilder();for(int i=0;i<100;i++)longName.append('\u4e2d');reject(encode(longName+" -- "+job),job);
        System.out.println("PASS: UTF-8 Chinese and apostrophe names, legacy fallback, malformed UTF-8, path/length rejection");
    }
}
