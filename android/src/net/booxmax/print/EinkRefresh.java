package net.booxmax.print;

import android.view.View;
import android.util.Log;
import java.lang.reflect.Method;

/** Scoped full-screen waveform request; never changes the system/global default mode. */
final class EinkRefresh {
    private static Method refreshScreen;
    private static int fullMode;
    private static String error="";
    static {
        try {
            // These public firmware extensions were observed on the target Max's View class.
            // UI_GC_MODE combines GC16, FULL and WAIT. Read the field, do not guess on other devices.
            fullMode=View.class.getField("UI_GC_MODE").getInt(null);
            int full=View.class.getField("EINK_UPDATE_MODE_FULL").getInt(null);
            if((fullMode&full)!=full)throw new NoSuchFieldException("Full waveform flag unavailable");
            refreshScreen=View.class.getMethod("refreshScreen",int.class);
        }catch(Exception e){error=e.getClass().getSimpleName()+": firmware full refresh unavailable";}
    }
    static boolean supported(){return refreshScreen!=null;}
    static String error(){return error;}
    static boolean request(View view) {
        if(!supported()||view==null||view.getWindowToken()==null||view.getWidth()==0||view.getHeight()==0)return false;
        try {refreshScreen.invoke(view,fullMode);error="";Log.i("BooxMaxPrint","Full refresh requested: mode="+fullMode);return true;}
        catch(Exception e){error=e.getClass().getSimpleName()+": full refresh request failed";Log.w("BooxMaxPrint",error);return false;}
    }
}
