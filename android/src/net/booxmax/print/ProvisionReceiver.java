package net.booxmax.print;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

public class ProvisionReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context, Intent intent) {
        String token = intent.getStringExtra("token");
        String deviceId = intent.getStringExtra("deviceId");
        if (token == null || !token.matches("[0-9a-f]{64}") || deviceId == null || !deviceId.matches("[a-zA-Z0-9-]{8,64}")) return;
        context.getSharedPreferences("printer",0).edit().putString("token",token).putString("deviceId",deviceId).putBoolean("enabled",true).commit();
        context.stopService(new Intent(context,PrintReceiverService.class));
        context.startService(new Intent(context,PrintReceiverService.class));
        setResultCode(1);
    }
}
