using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BooxImages;

class SenderExitTests {
 static object Get(object o,string n){return o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);}
 static void Set(object o,string n,object value){o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,value);}
 sealed class Transport:HttpMessageHandler {
  internal bool offline;internal int pauses;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
   if(request.RequestUri.AbsolutePath=="/control"){
    pauses++;if(offline){var pending=new TaskCompletionSource<HttpResponseMessage>();token.Register(()=>pending.TrySetCanceled());return pending.Task;}
   }
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"session\":\"\",\"paused\":true,\"sha256\":\"\",\"computer\":{\"ssids\":[\"test\"],\"status\":\"connected\"},\"boox\":{\"online\":false,\"ssid\":\"\"},\"pairing\":{\"running\":false,\"status\":\"idle\"}}")});
  }
 }
 [STAThread]static void Main(string[] args){
  string mode=args.Length==0?"idle":args[0],identity="Local\\BooxExitTest."+Guid.NewGuid().ToString("N");
  var clock=Stopwatch.StartNew();
  using(var watchdog=new System.Threading.Timer(delegate{Console.Error.WriteLine("FAIL: process did not exit after tray Exit ("+mode+")");Environment.Exit(2);},null,12000,Timeout.Infinite))try{
   string root=Path.GetFullPath("build/exit-test-"+mode);Directory.CreateDirectory(Path.Combine(root,".local"));
   File.WriteAllText(Path.Combine(root,".local/image-client.json"),"{\"url\":\"http://127.0.0.1:1\",\"key\":\"test-only\"}");
   var transport=new Transport{offline=mode=="offline"};bool created;
   using(var mutex=new Mutex(true,identity,out created)){
    using(var form=new SenderForm(root,transport))using(var activation=new WindowActivation(form,identity+".Activate")){
     if(mode!="idle")Set(form,"session",new string('a',32));
     var exitTimer=new System.Windows.Forms.Timer{Interval=100};
     exitTimer.Tick+=delegate{
      exitTimer.Stop();exitTimer.Dispose();
      ((System.Windows.Forms.Timer)Get(form,"pollTimer")).Stop();form.Hide();
      var tray=(NotifyIcon)Get(form,"tray");
      foreach(ToolStripItem item in tray.ContextMenuStrip.Items)if(item.Text=="\u9000\u51fa"){clock.Restart();item.PerformClick();return;}
      throw new Exception("Tray Exit item missing");
     };
     form.Shown+=delegate{exitTimer.Start();};
     Application.Run(form);
    }
   }
   using(var mutex=new Mutex(true,identity,out created)){if(!created)throw new Exception("Single instance mutex remains after exit");}
   if(mode=="idle"&&transport.pauses!=0)throw new Exception("Idle exit must not send network control");
   if(mode!="idle"&&transport.pauses!=1)throw new Exception("Expected exactly one best-effort pause");
   if(clock.ElapsedMilliseconds>4500)throw new Exception("Exit exceeded bounded timeout");
   Console.WriteLine("PASS: tray Exit "+mode+" returned from Application.Run, released single-instance ownership, elapsedMs="+clock.ElapsedMilliseconds);
  }catch(Exception e){Console.Error.WriteLine("FAIL: "+e);Environment.ExitCode=1;}
 }
}
