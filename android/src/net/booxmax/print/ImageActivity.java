package net.booxmax.print;

import android.app.Activity;
import android.os.Bundle;
import android.graphics.*;
import android.view.*;
import android.widget.*;
import android.content.pm.ActivityInfo;
import java.io.*;

/** Software-drawn static frames; no animation or assumed vendor refresh API. */
public class ImageActivity extends Activity {
    boolean visible;
    private ImageCanvas canvas;
    private LinearLayout controls;
    private Button pause;
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        getWindow().setFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN,WindowManager.LayoutParams.FLAG_FULLSCREEN);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        FrameLayout root=new FrameLayout(this);
        canvas=new ImageCanvas();root.addView(canvas);
        controls=new LinearLayout(this);controls.setOrientation(LinearLayout.VERTICAL);controls.setBackgroundColor(Color.WHITE);
        LinearLayout row=new LinearLayout(this);controls.addView(row);
        pause=button(row,"\u6682\u505c",new Runnable(){public void run(){if(ImageSession.paused){ImageSession.paused=false;ImageSession.reason="receiving";canvas.reset();}else ImageSession.pause("reader");updateControls();}});
        button(row,"\u5168\u56fe",new Runnable(){public void run(){canvas.fitWidth=false;canvas.reset();}});
        button(row,"\u9002\u5408\u5bbd\u5ea6",new Runnable(){public void run(){ImageSession.pause("reading");canvas.fitWidth=true;canvas.reset();}});
        button(row,"\u653e\u5927",new Runnable(){public void run(){ImageSession.pause("reading");canvas.zoom=Math.min(4,canvas.zoom*1.5f);canvas.invalidate();}});
        LinearLayout second=new LinearLayout(this);controls.addView(second);
        button(second,"\u65cb\u8f6c",new Runnable(){public void run(){ImageSession.orientation="landscape".equals(ImageSession.orientation)?"portrait":"landscape";applyOrientation();}});
        button(second,"\u5168\u5c40\u5237\u65b0",new Runnable(){public void run(){if(!fullRefresh())Toast.makeText(ImageActivity.this,"\u6b64\u56fa\u4ef6\u4e0d\u652f\u6301\u5168\u5c40\u5237\u65b0",Toast.LENGTH_LONG).show();}});
        button(second,"\u4fdd\u5b58",new Runnable(){public void run(){saveImage();}});
        button(second,"\u9000\u51fa",new Runnable(){public void run(){ImageSession.pause("left");finish();}});
        FrameLayout.LayoutParams lp=new FrameLayout.LayoutParams(-1,-2,Gravity.BOTTOM);root.addView(controls,lp);
        controls.setVisibility(View.GONE);setContentView(root);ImageSession.viewer=this;
    }
    private Button button(LinearLayout row,String title,final Runnable action){Button b=new Button(this);b.setText(title);b.setTextSize(18);row.addView(b,new LinearLayout.LayoutParams(0,-2,1));b.setOnClickListener(new View.OnClickListener(){public void onClick(View v){action.run();}});return b;}
    void updateControls(){if(pause!=null)pause.setText(ImageSession.paused?"\u7ee7\u7eed":"\u6682\u505c");}
    void showFrame(){if(canvas!=null){canvas.reset();updateControls();}}
    boolean fullRefresh(){
        if(canvas==null||!visible)return false;
        boolean ok=EinkRefresh.request(canvas);
        if(ok){ImageSession.refresh.requested();ImageSession.fullRefreshPending=false;}
        return ok;
    }
    void applyOrientation(){int desired="landscape".equals(ImageSession.orientation)?ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE:ActivityInfo.SCREEN_ORIENTATION_PORTRAIT;if(getRequestedOrientation()!=desired)setRequestedOrientation(desired);}
    @Override protected void onResume(){super.onResume();visible=true;ImageSession.viewer=this;applyOrientation();showFrame();}
    @Override protected void onPause(){visible=false;if(!isChangingConfigurations())ImageSession.pause("left");super.onPause();}
    @Override protected void onDestroy(){if(ImageSession.viewer==this)ImageSession.viewer=null;super.onDestroy();}
    @Override public void onBackPressed(){ImageSession.pause("left");super.onBackPressed();}
    private void saveImage(){
        final Bitmap copy=ImageSession.bitmap;if(copy==null)return;
        new Thread(new Runnable(){public void run(){String message;
            try{File dir=new File(android.os.Environment.getExternalStorageDirectory(),"Pictures/BooxMaxPrint");if(!dir.isDirectory()&&!dir.mkdirs())throw new IOException("Storage unavailable");
                File file=new File(dir,"Image-"+System.currentTimeMillis()+".png");FileOutputStream stream=new FileOutputStream(file);try{if(!copy.compress(Bitmap.CompressFormat.PNG,100,stream))throw new IOException("Encode failed");}finally{stream.close();}message="\u5df2\u4fdd\u5b58: "+file.getName();
            }catch(Exception e){message=e.getMessage();}final String result=message;runOnUiThread(new Runnable(){public void run(){Toast.makeText(ImageActivity.this,result,Toast.LENGTH_LONG).show();}});
        }},"Save image").start();
    }
    private class ImageCanvas extends View {
        private final Paint paint=new Paint(Paint.FILTER_BITMAP_FLAG);
        boolean fitWidth=false;float zoom=1,dx=0,dy=0,lastX,lastY,downX,downY;boolean moved;
        boolean refreshPosted;
        ImageCanvas(){super(ImageActivity.this);setLayerType(View.LAYER_TYPE_SOFTWARE,null);setBackgroundColor(Color.WHITE);}
        void reset(){zoom=1;dx=dy=0;invalidate();}
        @Override protected void onSizeChanged(int w,int h,int ow,int oh){reset();}
        @Override protected void onDraw(Canvas c){
            super.onDraw(c);Bitmap b=ImageSession.bitmap;if(b==null)return;
            float scale=(fitWidth?(float)getWidth()/b.getWidth():Math.min((float)getWidth()/b.getWidth(),(float)getHeight()/b.getHeight()))*zoom;
            float w=b.getWidth()*scale,h=b.getHeight()*scale;
            dx=Math.max(Math.min(0,getWidth()-w),Math.min(0,dx));dy=Math.max(Math.min(0,getHeight()-h),Math.min(0,dy));
            float x=w<getWidth()?(getWidth()-w)/2:dx,y=h<getHeight()?(getHeight()-h)/2:dy;
            c.drawBitmap(b,null,new RectF(x,y,x+w,y+h),paint);
            // Refresh the rendered buffer after this draw. Ordinary invalidate() is not a GC request.
            if(ImageSession.fullRefreshPending&&!refreshPosted&&visible) {
                refreshPosted=true;
                post(new Runnable(){public void run(){refreshPosted=false;if(!visible)return;ImageSession.fullRefreshPending=false;fullRefresh();}});
            }
        }
        @Override public boolean onTouchEvent(MotionEvent e){
            if(e.getAction()==MotionEvent.ACTION_DOWN){lastX=downX=e.getX();lastY=downY=e.getY();moved=false;return true;}
            if(e.getAction()==MotionEvent.ACTION_MOVE){if(Math.abs(e.getX()-downX)+Math.abs(e.getY()-downY)>15){moved=true;ImageSession.pause("reading");dx+=e.getX()-lastX;dy+=e.getY()-lastY;invalidate();}lastX=e.getX();lastY=e.getY();return true;}
            if(e.getAction()==MotionEvent.ACTION_UP){if(!moved){controls.setVisibility(controls.getVisibility()==View.VISIBLE?View.GONE:View.VISIBLE);updateControls();}return true;}return true;
        }
    }
}
