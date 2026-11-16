using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using BooxImages;

// Runs the production form in a hidden message loop with an isolated configuration.
// No real BOOX request and no changes to the user's cursor or windows.
class SenderLifecycleTests {
    static object Get(object target,string name){return target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(target);}
    static void Set(object target,string name,object value){target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(target,value);}
    static object Call(object target,string name,params object[] args){return target.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(target,args);}
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    [STAThread] static void Main(){
        Application.EnableVisualStyles();var context=new ApplicationContext();Exception failure=null;
        var timer=new Timer{Interval=20};timer.Tick+=async delegate {
            timer.Stop();string root=Path.GetFullPath("build/sender-lifecycle");Directory.CreateDirectory(Path.Combine(root,".local"));
            string settings=Path.Combine(root,".local/image-settings.json");if(File.Exists(settings))File.Delete(settings);
            File.WriteAllText(Path.Combine(root,".local/image-client.json"),"{\"url\":\"http://127.0.0.1:1\",\"key\":\"test-local-only\"}");
            try {
                using(var form=new SenderForm(root)) {
                    ((Timer)Get(form,"pollTimer")).Stop();
                    Check(((Timer)Get(form,"captureTimer")).Interval==500,"Default interval must be 500ms");
                    Check(((ComboBox)Get(form,"interval")).SelectedIndex==0,"Default interval selector");
                    Check(!(bool)Get(form,"automatic"),"Opening must not start capture");
                    var cursor=(CheckBox)Get(form,"showCursor");Check(!cursor.Checked,"Cursor default off");cursor.Checked=true;
                    ((ComboBox)Get(form,"direction")).SelectedIndex=1;((ComboBox)Get(form,"enhancement")).SelectedIndex=2;
                    Check(ImageSettings.Load(settings).ShowCursor,"Cursor toggle persisted immediately");
                    // Hold a capture across a settings generation change. Its old pixels must be discarded.
                    Set(form,"session",new string('a',32));Set(form,"busy",true);Set(form,"fullScreen",false);Set(form,"region",new Rectangle(0,0,16,16));
                    Task first=(Task)Call(form,"CaptureTick");Task overlapping=(Task)Call(form,"CaptureTick");Check(Object.ReferenceEquals(first,overlapping),"Only one capture worker permitted");
                    Set(form,"generation",(int)Get(form,"generation")+1);await first;
                    Check(Get(form,"pending")==null,"Invalidated capture must not be queued");
                    // Latest pending replaces old pending instead of accumulating frames.
                    Call(form,"Queue",new byte[]{1,2,3});Call(form,"Queue",new byte[]{4,5,6});
                    object frame=Get(form,"pending");var bytes=(byte[])frame.GetType().GetField("Bytes",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(frame);Check(bytes[0]==4,"Only latest pending frame retained");
                    Set(form,"session","");((NotifyIcon)Get(form,"tray")).Dispose();
                }
                using(var restored=new SenderForm(root)) {
                    ((Timer)Get(restored,"pollTimer")).Stop();
                    Check(((ComboBox)Get(restored,"direction")).SelectedIndex==1,"Restored orientation");
                    Check(((ComboBox)Get(restored,"enhancement")).SelectedIndex==2,"Restored enhancement");
                    Check(((CheckBox)Get(restored,"showCursor")).Checked,"Restored cursor option");
                    Check(((Timer)Get(restored,"captureTimer")).Interval==500,"Reopen default interval");
                    ((NotifyIcon)Get(restored,"tray")).Dispose();
                }
                Console.WriteLine("PASS: hidden form lifecycle, default 0.5s, cursor/rotation/enhancement persistence, one worker, stale capture discarded, latest frame only");
                using(var cursorImage=new Bitmap(128,128)) {
                    using(var graphics=Graphics.FromImage(cursorImage)){graphics.Clear(Color.White);Point point=Cursor.Position;ScreenCapture.DrawCursor(graphics,new Point(point.X-32,point.Y-32));}
                    int changed=0;for(int y=0;y<128;y++)for(int x=0;x<128;x++)if(cursorImage.GetPixel(x,y).ToArgb()!=Color.White.ToArgb())changed++;
                    Check(changed>0,"Native cursor overlay returned an empty image; rerun in the interactive desktop session");cursorImage.Save(Path.Combine(root,"cursor.png"));Console.WriteLine("PASS: native cursor overlay, non-white pixels="+changed);
                }
            }catch(Exception e){failure=e;}finally{context.ExitThread();}
        };timer.Start();Application.Run(context);timer.Dispose();if(failure!=null){Console.Error.WriteLine("FAIL: "+failure.GetType().Name+": "+failure.Message);Environment.ExitCode=1;}
    }
}
