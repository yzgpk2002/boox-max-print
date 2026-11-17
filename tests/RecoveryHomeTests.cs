using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BooxImages;

// Only generated frames, mock HTTP, and this test's own window/events are used.
class RecoveryHomeTests {
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr h,int m,IntPtr w,IntPtr l);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int RegisterWindowMessage(string name);
 static object Get(object o,string n){return o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);}
 static void Set(object o,string n,object v){o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v);}
 static object Call(object o,string n,params object[] a){return o.GetType().GetMethod(n,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,a);}
 static void Check(bool b,string message){if(!b)throw new Exception(message);}
 static byte[] Png(Color c){using(var b=new Bitmap(2,2)){b.SetPixel(0,0,c);using(var s=new MemoryStream()){b.Save(s,ImageFormat.Png);return s.ToArray();}}}
 sealed class Transport:HttpMessageHandler {
  internal int frames,homes;internal bool completed;internal readonly TaskCompletionSource<bool> release=new TaskCompletionSource<bool>();
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
   if(request.RequestUri.AbsolutePath=="/frame"){frames++;await release.Task;completed=true;}
   if(request.RequestUri.AbsolutePath=="/control"){
    var body=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(await request.Content.ReadAsStringAsync());
    Check((string)body["action"]=="home","Home must not start or resume an image session");
    Check(((string)body["session"]).Length==32,"Home carries valid request identity even before any image session");
    Check(completed,"Home waits for the in-flight upload to finish");homes++;
   }
   return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"accepted\":true}")};
  }
 }
 [STAThread]static void Main(){
  var context=new ApplicationContext();var timer=new System.Windows.Forms.Timer{Interval=20};Exception failure=null;
  timer.Tick+=async delegate{timer.Stop();SenderForm form=null;try{
   string root=Path.GetFullPath("build/recovery-home");Directory.CreateDirectory(Path.Combine(root,".local"));
   File.WriteAllText(Path.Combine(root,".local/image-client.json"),"{\"url\":\"http://127.0.0.1:1\",\"key\":\"test-only\"}");
   var transport=new Transport();form=new SenderForm(root,transport);((System.Windows.Forms.Timer)Get(form,"pollTimer")).Stop();
   Set(form,"automatic",true);Set(form,"session",new string('b',32));
   var capture=(System.Windows.Forms.Timer)Get(form,"captureTimer");capture.Interval=60000;capture.Start();
   var scroll=(System.Windows.Forms.Timer)Get(form,"scrollTimer");scroll.Interval=60000;scroll.Start();
   Call(form,"Queue",Png(Color.Black));Call(form,"Queue",Png(Color.Gray));
   Task home=(Task)Call(form,"ReturnHome");
   Check(!capture.Enabled&&!scroll.Enabled&&!(bool)Get(form,"automatic")&&Get(form,"pending")==null,"Home stops capture/scroll and drops pending frames");
   Check(!home.IsCompleted&&transport.homes==0,"Home must wait for in-flight frame");
   transport.release.SetResult(true);await home;
   Check(transport.frames==1&&transport.homes==1,"Only in-flight frame precedes home; pending frame discarded");
   Set(form,"session","");await (Task)Call(form,"ReturnHome");Check(transport.homes==2,"Home also works without a local session");
   var tray=(NotifyIcon)Get(form,"tray");form.Hide();tray.Visible=false;
   string eventName="Local\\BooxImagesTest."+Guid.NewGuid().ToString("N");
   using(var activation=new WindowActivation(form,eventName)){
    Check(await Task.Run(()=>Program.ActivateExisting(eventName)),"Activation request acknowledged by UI thread");
    Check(form.Visible&&form.WindowState==FormWindowState.Normal&&tray.Visible,"Hidden window and missing tray restored");
    form.Hide();tray.Visible=false;
    PostMessage(form.Handle,RegisterWindowMessage("TaskbarCreated"),IntPtr.Zero,IntPtr.Zero);await Task.Delay(100);
    Check(tray.Visible&&!form.Visible,"Taskbar restart restores icon without opening window");
   }
   Check(transport.frames==1,"Recovery never restarts capture");
   Set(form,"closing",true);Check(!form.RecoverWindow(),"Closing instance must not acknowledge successful activation");
   Console.WriteLine("PASS: home drains in-flight frame, drops queued frame, stops timers and works without session; named activation restores hidden window/tray; TaskbarCreated restores only tray");
  }catch(Exception e){failure=e;}finally{if(form!=null){Set(form,"closing",true);((System.Windows.Forms.Timer)Get(form,"captureTimer")).Stop();((System.Windows.Forms.Timer)Get(form,"scrollTimer")).Stop();((NotifyIcon)Get(form,"tray")).Dispose();form.Close();form.Dispose();}context.ExitThread();}};
  timer.Start();Application.Run(context);timer.Dispose();if(failure!=null){Console.Error.WriteLine("FAIL: "+failure);Environment.ExitCode=1;}
 }
}
