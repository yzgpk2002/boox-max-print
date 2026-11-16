package net.booxmax.print;
import android.app.Activity;
import android.os.Bundle;
import android.os.Handler;
import android.content.Intent;
import android.graphics.Color;
import android.view.View;
import android.widget.*;

public class MainActivity extends Activity {
    private TextView status;
    private Handler handler = new Handler();
    private Runnable refresh = new Runnable() { public void run() { update(); handler.postDelayed(this,3000); } };
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        LinearLayout layout=new LinearLayout(this); layout.setOrientation(1); layout.setPadding(30,35,30,30); layout.setBackgroundColor(Color.WHITE);
        TextView title=new TextView(this); title.setText("BOOX Max \u65e0\u7ebf\u6253\u5370"); title.setTextSize(30); title.setTextColor(Color.BLACK); layout.addView(title);
        status=new TextView(this); status.setTextSize(21); status.setTextColor(Color.BLACK); status.setPadding(0,25,0,25); layout.addView(status);
        Button start=new Button(this); start.setText("\u542f\u52a8\u63a5\u6536"); layout.addView(start);
        start.setOnClickListener(new View.OnClickListener(){public void onClick(View v){getSharedPreferences("printer",0).edit().putBoolean("enabled",true).commit();startService(new Intent(MainActivity.this,PrintReceiverService.class));update();}});
        Button stop=new Button(this); stop.setText("\u505c\u6b62\u63a5\u6536"); layout.addView(stop);
        stop.setOnClickListener(new View.OnClickListener(){public void onClick(View v){getSharedPreferences("printer",0).edit().putBoolean("enabled",false).commit();stopService(new Intent(MainActivity.this,PrintReceiverService.class));update();}});
        Button images=new Button(this);images.setText("\u67e5\u770b\u6700\u8fd1\u56fe\u7247");layout.addView(images);
        images.setOnClickListener(new View.OnClickListener(){public void onClick(View v){if(ImageSession.bitmap==null){Toast.makeText(MainActivity.this,"\u8bf7\u5148\u4ece\u7535\u8111\u53d1\u9001\u56fe\u7247",Toast.LENGTH_LONG).show();return;}startActivity(new Intent(MainActivity.this,ImageActivity.class));}});
        TextView help=new TextView(this); help.setTextSize(19); help.setTextColor(Color.BLACK); help.setPadding(0,25,0,0);
        help.setText("\u7535\u8111\u4e0e BOOX \u8fde\u63a5\u540c\u4e00 Wi-Fi\uff0c\u5728\u7535\u8111\u6253\u5370\u83dc\u5355\u9009\u62e9 BOOX Max\u3002\n\n\u6536\u5230 PDF \u540e\u81ea\u52a8\u7528\u81ea\u5e26\u9605\u8bfb\u5668\u6253\u5f00\u3002\u6309\u8fd4\u56de\u952e\u4e0d\u4f1a\u505c\u6b62\u63a5\u6536\u3002\n\n\u4fdd\u6301\u63a5\u6536\u4f1a\u589e\u52a0\u8017\u7535\uff1b\u4e0d\u7528\u65f6\u53ef\u70b9\u505c\u6b62\u3002"); layout.addView(help);
        setContentView(layout);
        if(getSharedPreferences("printer",0).getBoolean("enabled",false))startService(new Intent(this,PrintReceiverService.class));
    }
    private void update(){status.setText((PrintReceiverService.running ? "\u63a5\u6536\u4e2d" : "\u5df2\u505c\u6b62")+"\nWi-Fi: "+PrintReceiverService.address(this)+"\n"+PrintReceiverService.status);}
    @Override protected void onResume(){super.onResume();handler.post(refresh);}
    @Override protected void onPause(){handler.removeCallbacks(refresh);super.onPause();}
}
