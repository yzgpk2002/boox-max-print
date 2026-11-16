package net.booxmax.print;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
public class BootReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context, Intent intent) {
        String action=intent.getAction();
        boolean networkChanged=android.net.ConnectivityManager.CONNECTIVITY_ACTION.equals(action);
        if (!Intent.ACTION_BOOT_COMPLETED.equals(action) && !Intent.ACTION_MY_PACKAGE_REPLACED.equals(action) && !networkChanged) return;
        android.content.SharedPreferences prefs=context.getSharedPreferences("printer",0);
        if (!prefs.getBoolean("enabled",false) || prefs.getString("token","").length()!=64) return;
        // This Max firmware filters third-party BOOT_COMPLETED receivers.
        // Its system connectivity broadcast still starts enabled receivers at boot.
        if (networkChanged) {
            android.net.ConnectivityManager manager=(android.net.ConnectivityManager)context.getSystemService(Context.CONNECTIVITY_SERVICE);
            android.net.NetworkInfo network=manager.getActiveNetworkInfo();
            if (network==null || !network.isConnected() || PrintReceiverService.running) return;
        }
        if (Intent.ACTION_BOOT_COMPLETED.equals(action)) prefs.edit().putLong("lastBootStart",System.currentTimeMillis()).commit();
        android.util.Log.i("BooxMaxPrint","Starting receiver after "+action);
        context.startService(new Intent(context,PrintReceiverService.class));
    }
}
