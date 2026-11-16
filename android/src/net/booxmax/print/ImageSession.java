package net.booxmax.print;

import android.content.Context;
import android.content.Intent;
import android.graphics.Bitmap;
import org.json.JSONObject;

/** All state mutations run on the Android main thread, including frame commit. */
final class ImageSession {
    static String id="", reason="idle", digest="";
    static String orientation="portrait";
    static boolean paused=true;
    static long sequence=0;
    static Bitmap bitmap;
    static ImageActivity viewer;

    static JSONObject state() throws Exception {
        return new JSONObject().put("session",id).put("paused",paused).put("reason",reason)
            .put("sequence",sequence).put("sha256",digest).put("orientation",orientation).put("viewerVisible",viewer!=null && viewer.visible);
    }
    static void pause(String why) {
        paused=true; reason=why;
        if(viewer!=null)viewer.updateControls();
    }
    static void control(String action,String session,String direction) throws Exception {
        if(!session.matches("[0-9a-f]{32}"))throw new Exception("Invalid image session");
        if(direction.length()>0 && !"portrait".equals(direction) && !"landscape".equals(direction))throw new Exception("Invalid image orientation");
        if("start".equals(action)) {
            if(!session.equals(id)){ id=session; sequence=0; digest=""; }
            paused=false; reason="receiving";
        } else {
            if(!id.equals(session))throw new Exception("Image session changed");
            if("resume".equals(action)){paused=false;reason="receiving";}
            else if("pause".equals(action)||"stop".equals(action))pause(action);
            else if("orientation".equals(action)) { /* preserve paused state */ }
            else throw new Exception("Unknown image action");
        }
        if(direction.length()>0)orientation=direction;
        if(viewer!=null){if(viewer.visible)viewer.applyOrientation();viewer.updateControls();}
    }
    static boolean commit(Context context,String session,long frame,String sha,Bitmap next) throws Exception {
        if(!session.equals(id))throw new Exception("Image session changed");
        if(paused)throw new Exception("Image display paused: "+reason);
        if(frame<sequence)throw new Exception("Stale image frame");
        if(frame==sequence){if(!sha.equals(digest))throw new Exception("Frame sequence conflict");return false;}
        // A hidden viewer never reopens except after an explicit start/resume.
        boolean open=viewer==null || !viewer.visible;
        if(open) {
            Intent intent=new Intent(context,ImageActivity.class);
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TOP);
            context.startActivity(intent);
        }
        bitmap=next; sequence=frame; digest=sha;
        if(viewer!=null)viewer.showFrame();
        return true;
    }
}
