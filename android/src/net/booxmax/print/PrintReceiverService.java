package net.booxmax.print;

import android.app.*;
import android.content.*;
import android.net.Uri;
import android.net.wifi.WifiManager;
import android.os.*;
import org.json.JSONObject;
import java.io.*;
import java.net.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.*;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/** Small, authenticated PDF receiver. Uses only APIs present on Android 4.4. */
public class PrintReceiverService extends Service {
    public static final int PORT=8632;
    public static volatile boolean running=false;
    public static volatile String status="";
    private volatile boolean alive;
    private ServerSocket server;
    private DatagramSocket discovery;
    private ThreadPoolExecutor workers;
    private final Set<Socket> clients=Collections.synchronizedSet(new HashSet<Socket>());
    private WifiManager.WifiLock wifiLock;
    private PowerManager.WakeLock wakeLock;
    private Handler main;
    private String token,deviceId;
    private final Semaphore imageUpload=new Semaphore(1);
    private final Object pdfCommitLock=new Object();

    public static String address(Context context){
        try {int ip=((WifiManager)context.getApplicationContext().getSystemService(WIFI_SERVICE)).getConnectionInfo().getIpAddress();
            if(ip==0)return "\u672a\u8fde\u63a5";
            return (ip&255)+"."+((ip>>8)&255)+"."+((ip>>16)&255)+"."+((ip>>24)&255);
        }catch(Exception e){return "\u672a\u8fde\u63a5";}
    }
    public static String wifiName(Context context){
        try{
            String name=((WifiManager)context.getApplicationContext().getSystemService(WIFI_SERVICE)).getConnectionInfo().getSSID();
            if(name==null||"<unknown ssid>".equalsIgnoreCase(name))return "";
            if(name.length()>1&&name.startsWith("\"" )&&name.endsWith("\""))name=name.substring(1,name.length()-1);
            return name;
        }catch(Exception e){return "";}
    }
    @Override public IBinder onBind(Intent intent){return null;}
    @Override public void onCreate(){
        super.onCreate(); main=new Handler(Looper.getMainLooper());
        if(!getSharedPreferences("printer",0).getBoolean("enabled",false)){stopSelf();return;}
        token=getSharedPreferences("printer",0).getString("token","");
        deviceId=getSharedPreferences("printer",0).getString("deviceId","");
        if(token.length()!=64){status="\u7b49\u5f85\u7535\u8111\u914d\u5bf9";stopSelf();return;}
        Intent open=new Intent(this,MainActivity.class);
        PendingIntent pending=PendingIntent.getActivity(this,0,open,PendingIntent.FLAG_UPDATE_CURRENT);
        Notification notification=new Notification.Builder(this).setSmallIcon(android.R.drawable.stat_sys_upload)
            .setContentTitle("BOOX Max \u65e0\u7ebf\u63a5\u6536").setContentText("\u7b49\u5f85 PDF / \u56fe\u7247\uff0c\u70b9\u51fb\u7ba1\u7406").setContentIntent(pending).setOngoing(true).build();
        startForeground(8632,notification);
        wakeLock=((PowerManager)getSystemService(POWER_SERVICE)).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK,"BooxMaxPrint:receive");wakeLock.acquire();
        wifiLock=((WifiManager)getApplicationContext().getSystemService(WIFI_SERVICE)).createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF,"BooxMaxPrint");wifiLock.acquire();
        alive=true;
        workers=new ThreadPoolExecutor(2,2,30,TimeUnit.SECONDS,new ArrayBlockingQueue<Runnable>(2));
        new Thread(new Runnable(){public void run(){listen();}},"PDF receiver").start();
        new Thread(new Runnable(){public void run(){discover();}},"PDF discovery").start();
    }
    @Override public int onStartCommand(Intent intent,int flags,int startId){
        if(!getSharedPreferences("printer",0).getBoolean("enabled",false)){stopSelf();return START_NOT_STICKY;}
        return START_STICKY;
    }
    private void listen(){
        try{
            server=new ServerSocket();server.setReuseAddress(true);server.bind(new InetSocketAddress(PORT));
            running=true;status="\u7b49\u5f85\u6253\u5370";
            while(alive){
                final Socket socket=server.accept();socket.setSoTimeout(30000);clients.add(socket);
                try {workers.execute(new Runnable(){public void run(){handle(socket);}});}
                catch(RejectedExecutionException e){clients.remove(socket);socket.close();}
            }
        }catch(Exception e){if(alive){status=e.toString();main.post(new Runnable(){public void run(){stopSelf();}});}}
        finally{running=false;}
    }
    private void discover(){
        try{
            discovery=new DatagramSocket(null);discovery.setReuseAddress(true);discovery.bind(new InetSocketAddress(PORT));
            byte[] bytes=new byte[512];
            while(alive){
                DatagramPacket packet=new DatagramPacket(bytes,bytes.length);discovery.receive(packet);
                String query=new String(packet.getData(),0,packet.getLength(),"UTF-8");
                if(!query.equals("BOOXMAXPRINT/1 "+deviceId))continue;
                byte[] reply=("BOOXMAXPRINT/1 "+deviceId+" "+PORT).getBytes("UTF-8");
                discovery.send(new DatagramPacket(reply,reply.length,packet.getAddress(),packet.getPort()));
            }
        }catch(Exception e){if(alive)android.util.Log.w("BooxMaxPrint","Discovery unavailable",e);}
    }
    private static String line(InputStream in)throws IOException{
        ByteArrayOutputStream out=new ByteArrayOutputStream();int c;
        while((c=in.read())!=-1){if(c==10)break;if(c!=13)out.write(c);if(out.size()>4096)throw new IOException("Header too long");}
        if(c==-1&&out.size()==0)throw new EOFException();return out.toString("US-ASCII");
    }
    private void handle(Socket socket){
        File temporary=null;
        try{
            InputStream in=new BufferedInputStream(socket.getInputStream());OutputStream out=socket.getOutputStream();
            String[] request=line(in).split(" ");if(request.length!=3)throw new IOException("Bad request");
            Map<String,String> headers=new HashMap<String,String>();int headerBytes=0;
            while(true){String entry=line(in);if(entry.length()==0)break;headerBytes+=entry.length();if(headerBytes>16384)throw new IOException("Too many headers");
                int colon=entry.indexOf(':');if(colon<1)throw new IOException("Bad header");String name=entry.substring(0,colon).trim().toLowerCase(Locale.US);
                if(headers.containsKey(name))throw new IOException("Repeated header");headers.put(name,entry.substring(colon+1).trim());}
            if(headers.containsKey("origin")){respond(out,403,new JSONObject().put("error","Cross-origin request rejected"));return;}
            if(request[0].equals("GET") && request[1].startsWith("/health?challenge=")){
                String challenge=request[1].substring("/health?challenge=".length());
                if(!challenge.matches("[0-9a-f]{32}"))throw new IOException("Bad challenge");
                Mac mac=Mac.getInstance("HmacSHA256");mac.init(new SecretKeySpec(token.getBytes("UTF-8"),"HmacSHA256"));
                respond(out,200,new JSONObject().put("deviceId",deviceId).put("proof",hex(mac.doFinal((deviceId+":"+challenge).getBytes("UTF-8")))).put("ready",Environment.MEDIA_MOUNTED.equals(Environment.getExternalStorageState())).put("images",true).put("version","0.5.6"));return;
            }
            String authorization=headers.get("authorization");
            if(authorization==null||!MessageDigest.isEqual(authorization.getBytes("UTF-8"),("Bearer "+token).getBytes("UTF-8"))){respond(out,401,new JSONObject().put("error","Pairing required"));return;}
            if(request[1].startsWith("/images/")){handleImage(request,headers,in,out);return;}
            if(!request[0].equals("POST")||!request[1].matches("/jobs/BOOX-[0-9]{14}-[0-9]+\\.pdf")){respond(out,404,new JSONObject().put("error","Unknown endpoint"));return;}
            if(headers.containsKey("transfer-encoding")){respond(out,411,new JSONObject().put("error","Content-Length required"));return;}
            long length=Long.parseLong(headers.get("content-length"));
            String sha256=headers.get("x-content-sha256");
            if(length<12||length>64*1024*1024||sha256==null||!sha256.matches("[0-9a-f]{64}")){respond(out,400,new JSONObject().put("error","Invalid PDF length or checksum"));return;}
            if(!"application/pdf".equals(headers.get("content-type"))){respond(out,415,new JSONObject().put("error","PDF only"));return;}
            if(!Environment.MEDIA_MOUNTED.equals(Environment.getExternalStorageState())){respond(out,503,new JSONObject().put("error","BOOX storage unavailable; turn off USB storage sharing"));return;}
            File dir=new File(Environment.getExternalStorageDirectory(),"Documents/BooxMaxPrint");
            if(!dir.isDirectory()&&!dir.mkdirs())throw new IOException("Cannot create print folder");
            if(dir.getUsableSpace()<length+8*1024*1024){respond(out,507,new JSONObject().put("error","BOOX storage full"));return;}
            String name=request[1].substring("/jobs/".length());
            String requestedName=DocumentNames.decode(headers.get("x-document-name"),name);
            SharedPreferences receipts=getSharedPreferences("receipts",0);
            String storedName;
            synchronized(pdfCommitLock){
                storedName=receipts.getString("file:"+name,null);
                if(storedName==null){
                    // Keep an already delivered legacy path. A retry cannot move
                    // the same job to another filename or reopen it under a new key.
                    storedName=new File(dir,name).exists()?name:requestedName;
                    if(!receipts.edit().putString("file:"+name,storedName).commit())throw new IOException("Cannot persist PDF destination");
                }
            }
            File destination=new File(dir,storedName);
            if(!destination.getCanonicalFile().getParentFile().equals(dir.getCanonicalFile()))throw new IOException("Unsafe PDF destination");
            temporary=File.createTempFile("receive-",".part",dir);
            if("100-continue".equalsIgnoreCase(headers.get("expect"))){out.write("HTTP/1.1 100 Continue\r\n\r\n".getBytes("US-ASCII"));out.flush();}
            MessageDigest digest=MessageDigest.getInstance("SHA-256");
            FileOutputStream file=new FileOutputStream(temporary);
            byte[] buffer=new byte[32768];long remaining=length;
            try{while(remaining>0){int n=in.read(buffer,0,(int)Math.min(buffer.length,remaining));if(n<0)throw new EOFException("PDF interrupted");file.write(buffer,0,n);digest.update(buffer,0,n);remaining-=n;}file.getFD().sync();}finally{file.close();}
            if(!sha256.equals(hex(digest.digest())))throw new IOException("PDF checksum mismatch");
            RandomAccessFile verify=new RandomAccessFile(temporary,"r");
            try{byte[] head=new byte[5];verify.readFully(head);if(!"%PDF-".equals(new String(head,"US-ASCII")))throw new IOException("Not PDF");
                int count=(int)Math.min(4096,length);verify.seek(length-count);byte[] tail=new byte[count];verify.readFully(tail);if(!new String(tail,"ISO-8859-1").contains("%%EOF"))throw new IOException("PDF is truncated");
            }finally{verify.close();}
            boolean wasOpened;
            synchronized(pdfCommitLock){
                // Identical retried transfers are idempotent; never overwrite different content.
                if(destination.exists()){
                    if(!sha256.equals(hash(destination)))throw new IOException("Job name already exists with different content");
                    temporary.delete();temporary=null;
                }else{if(!temporary.renameTo(destination))throw new IOException("Cannot commit PDF");temporary=null;}
                wasOpened=receipts.getBoolean(name,false);
                if(!wasOpened){openPdf(destination);if(!receipts.edit().putBoolean(name,true).commit())throw new IOException("PDF opened; receipt persistence failed");}
            }
            status="\u5df2\u63a5\u6536\uff1a"+destination.getName();
            respond(out,200,new JSONObject().put("deviceId",deviceId).put("bytes",length).put("sha256",sha256).put("path",destination.getAbsolutePath()).put("opened",true).put("duplicate",wasOpened));
        }catch(Exception e){status=e.getMessage();try{respond(socket.getOutputStream(),500,new JSONObject().put("error",e.getMessage()));}catch(Exception ignored){}}
        finally{if(temporary!=null)temporary.delete();clients.remove(socket);try{socket.close();}catch(Exception ignored){}}
    }
    private void openPdf(final File file)throws Exception{
        final CountDownLatch done=new CountDownLatch(1);final Exception[] failure=new Exception[1];
        main.post(new Runnable(){public void run(){try{
            ImageSession.pause("pdf");
            Intent open=new Intent(Intent.ACTION_VIEW);open.setDataAndType(Uri.fromFile(file),"application/pdf");
            open.setClassName("com.onyx.kreader","com.onyx.kreader.ui.ReaderTabHostActivity");open.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);startActivity(open);
        }catch(Exception e){failure[0]=e;}finally{done.countDown();}}});
        if(!done.await(10,TimeUnit.SECONDS))throw new IOException("PDF saved; reader launch timed out");
        if(failure[0]!=null)throw failure[0];
    }
    private JSONObject onMain(final Callable<JSONObject> action)throws Exception {
        final CountDownLatch done=new CountDownLatch(1);final JSONObject[] result=new JSONObject[1];final Exception[] error=new Exception[1];
        final java.util.concurrent.atomic.AtomicBoolean canceled=new java.util.concurrent.atomic.AtomicBoolean(false);
        main.post(new Runnable(){public void run(){try{if(!canceled.get())result[0]=action.call();}catch(Exception e){error[0]=e;}finally{done.countDown();}}});
        if(!done.await(10,TimeUnit.SECONDS)){canceled.set(true);throw new IOException("Image UI timeout");}
        if(error[0]!=null)throw error[0];return result[0];
    }
    private byte[] imageBody(Map<String,String> headers,InputStream in,int maximum)throws Exception {
        if(headers.containsKey("transfer-encoding"))throw new IOException("Content-Length required");
        long length=Long.parseLong(headers.get("content-length"));
        if(length<1 || length>maximum)throw new IOException("Image request too large or empty");
        byte[] bytes=new byte[(int)length];int offset=0;
        while(offset<bytes.length){int n=in.read(bytes,offset,bytes.length-offset);if(n<0)throw new EOFException("Image interrupted");offset+=n;}
        return bytes;
    }
    private void handleImage(String[] request,Map<String,String> headers,InputStream in,OutputStream out)throws Exception {
        if("GET".equals(request[0]) && "/images/state".equals(request[1])) {
            respond(out,200,onMain(new Callable<JSONObject>(){public JSONObject call()throws Exception{return ImageSession.state().put("deviceId",deviceId).put("wifiSsid",wifiName(PrintReceiverService.this));}}));return;
        }
        if(!"POST".equals(request[0])){respond(out,404,new JSONObject().put("error","Unknown image endpoint"));return;}
        if("/images/control".equals(request[1])) {
            final JSONObject body=new JSONObject(new String(imageBody(headers,in,4096),"UTF-8"));
            try {respond(out,200,onMain(new Callable<JSONObject>(){public JSONObject call()throws Exception{
                if("home".equals(body.getString("action"))){
                    if(!body.getString("session").matches("[0-9a-f]{32}"))throw new Exception("Invalid image session");
                    ImageSession.goHome(PrintReceiverService.this);
                }else ImageSession.control(body.getString("action"),body.getString("session"),body.optString("orientation",""),body.optInt("fullRefreshEvery",-1));
                return ImageSession.state().put("deviceId",deviceId);
            }}));}
            catch(Exception e){respond(out,409,new JSONObject().put("error",e.getMessage()));}return;
        }
        if(!"/images/frame".equals(request[1])){respond(out,404,new JSONObject().put("error","Unknown image endpoint"));return;}
        if(!imageUpload.tryAcquire()){respond(out,429,new JSONObject().put("error","Image receiver busy"));return;}
        android.graphics.Bitmap decoded=null;
        try {
            final String session=headers.get("x-image-session"),sha=headers.get("x-content-sha256");
            final long sequence=Long.parseLong(headers.get("x-image-sequence"));
            if(session==null || !session.matches("[0-9a-f]{32}") || sha==null || !sha.matches("[0-9a-f]{64}") || sequence<1)throw new IOException("Invalid image metadata");
            String type=headers.get("content-type");if(!"image/png".equals(type)&&!"image/jpeg".equals(type))throw new IOException("PNG or JPEG required");
            final byte[] body=imageBody(headers,in,8*1024*1024);
            // Drain the bounded request before rejecting a paused frame. Closing
            // a socket with unread upload bytes can reset TCP and lose the reply.
            onMain(new Callable<JSONObject>(){public JSONObject call()throws Exception{if(!session.equals(ImageSession.id)||ImageSession.paused)throw new IOException("Image session paused or changed");return ImageSession.state();}});
            if(!sha.equals(hex(MessageDigest.getInstance("SHA-256").digest(body))))throw new IOException("Image checksum mismatch");
            android.graphics.BitmapFactory.Options bounds=new android.graphics.BitmapFactory.Options();bounds.inJustDecodeBounds=true;
            android.graphics.BitmapFactory.decodeByteArray(body,0,body.length,bounds);
            if(bounds.outWidth<1||bounds.outHeight<1||bounds.outWidth>16384||bounds.outHeight>16384||(long)bounds.outWidth*bounds.outHeight>16000000||!type.equals(bounds.outMimeType))throw new IOException("Invalid image dimensions or format");
            android.graphics.BitmapFactory.Options options=new android.graphics.BitmapFactory.Options();options.inSampleSize=1;
            while((long)(bounds.outWidth/options.inSampleSize)*(bounds.outHeight/options.inSampleSize)>4000000)options.inSampleSize*=2;
            decoded=android.graphics.BitmapFactory.decodeByteArray(body,0,body.length,options);
            if(decoded==null)throw new IOException("Image decode failed");final android.graphics.Bitmap bitmap=decoded;
            JSONObject receipt=onMain(new Callable<JSONObject>(){public JSONObject call()throws Exception{
                boolean changed=ImageSession.commit(PrintReceiverService.this,session,sequence,sha,bitmap);
                return ImageSession.state().put("deviceId",deviceId).put("accepted",true).put("bytes",body.length).put("duplicate",!changed);
            }});
            respond(out,200,receipt);
        }catch(OutOfMemoryError e){respond(out,413,new JSONObject().put("error","Image exceeds device memory"));}
        catch(Exception e){respond(out,409,new JSONObject().put("error",e.getMessage()));}
        finally{imageUpload.release();}
    }
    private static String hash(File file)throws Exception{
        MessageDigest digest=MessageDigest.getInstance("SHA-256");FileInputStream in=new FileInputStream(file);byte[] buffer=new byte[32768];int n;
        try{while((n=in.read(buffer))!=-1)digest.update(buffer,0,n);}finally{in.close();}return hex(digest.digest());
    }
    private static String hex(byte[] bytes){StringBuilder s=new StringBuilder();for(byte b:bytes)s.append(String.format(Locale.US,"%02x",b&255));return s.toString();}
    private static void respond(OutputStream out,int code,JSONObject body)throws IOException{
        byte[] bytes=body.toString().getBytes("UTF-8");
        out.write(("HTTP/1.1 "+code+" "+(code==200?"OK":"Error")+"\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: "+bytes.length+"\r\nConnection: close\r\n\r\n").getBytes("US-ASCII"));out.write(bytes);out.flush();
    }
    @Override public void onDestroy(){
        alive=false;running=false;
        try{if(server!=null)server.close();}catch(Exception ignored){}
        if(discovery!=null)discovery.close();
        synchronized(clients){for(Socket client:clients)try{client.close();}catch(Exception ignored){}clients.clear();}
        if(workers!=null)workers.shutdownNow();
        if(wifiLock!=null&&wifiLock.isHeld())wifiLock.release();
        if(wakeLock!=null&&wakeLock.isHeld())wakeLock.release();
        stopForeground(true);super.onDestroy();
    }
}
