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
using BooxImages;

// No native input or screen capture. Exercises real queue and HTTP pipeline with generated PNGs.
class ReadingToggleTests {
 static object Get(object o,string n){return o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);}
 static void Set(object o,string n,object v){o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v);}
 static object Call(object o,string n,params object[] a){return o.GetType().GetMethod(n,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,a);}
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static byte[] Png(Color color){using(var b=new Bitmap(2,2)){b.SetPixel(0,0,color);using(var stream=new MemoryStream()){b.Save(stream,ImageFormat.Png);return stream.ToArray();}}}
 sealed class Transport:HttpMessageHandler {
  internal int frames,pauses;internal readonly TaskCompletionSource<bool> release=new TaskCompletionSource<bool>();
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
   if(request.RequestUri.AbsolutePath=="/frame"){frames++;if(frames==1)await release.Task;}
   else if(request.RequestUri.AbsolutePath=="/control"){string body=await request.Content.ReadAsStringAsync();if(body.Contains("pause")||body.Contains("stop"))pauses++;}
   return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"accepted\":true}")};
  }
 }
 [STAThread]static void Main(){
  var context=new ApplicationContext();var timer=new System.Windows.Forms.Timer{Interval=20};Exception failure=null;
  timer.Tick+=async delegate{timer.Stop();SenderForm form=null;try{
   string root=Path.GetFullPath("build/reading-toggle");Directory.CreateDirectory(Path.Combine(root,".local"));
   File.WriteAllText(Path.Combine(root,".local/image-client.json"),"{\"url\":\"http://127.0.0.1:1\",\"key\":\"test-only\"}");
   var transport=new Transport();form=new SenderForm(root,transport);((System.Windows.Forms.Timer)Get(form,"pollTimer")).Stop();
   ((CheckBox)Get(form,"autoScroll")).Checked=true;Set(form,"automatic",true);Set(form,"session",new string('b',32));
   var capture=(System.Windows.Forms.Timer)Get(form,"captureTimer");capture.Interval=60000;capture.Start();
   var scroll=(System.Windows.Forms.Timer)Get(form,"scrollTimer");scroll.Interval=60000;scroll.Start();
   Call(form,"Queue",Png(Color.Black));Call(form,"Queue",Png(Color.Gray));
   Check((bool)Get(form,"busy")&&Get(form,"pending")!=null,"First frame in flight and second pending");
   int generation=(int)Get(form,"generation");Call(form,"StopReadingOnly");
   Check((bool)Get(form,"automatic")&&capture.Enabled,"Stopping reading must leave capture active");
   Check(!scroll.Enabled&&!((AutoScroller)Get(form,"scroller")).Armed,"Reading timer and target must stop");
   Check((int)Get(form,"generation")==generation&&Get(form,"pending")!=null,"Stopping reading preserves in-flight and pending frames");
   transport.release.SetResult(true);await (Task)Get(form,"sendingTask");
   Check(transport.frames==2&&transport.pauses==0,"Both queued frames delivered without remote pause");
   Call(form,"Queue",Png(Color.White));await (Task)Get(form,"sendingTask");Check(transport.frames==3,"Later frame still delivered after stopping reading");
   await (Task)Call(form,"Pause",false);
   Check(!(bool)Get(form,"automatic")&&!capture.Enabled&&transport.pauses==1,"Whole-session pause still stops capture and pauses receiver");
   Console.WriteLine("PASS: reading-only pause preserves capture, session, in-flight and pending frames; later frame delivered; whole-session pause remains distinct");
  }catch(Exception e){failure=e;}finally{if(form!=null){((System.Windows.Forms.Timer)Get(form,"captureTimer")).Stop();((System.Windows.Forms.Timer)Get(form,"scrollTimer")).Stop();((NotifyIcon)Get(form,"tray")).Dispose();form.Dispose();}context.ExitThread();}};
  timer.Start();Application.Run(context);timer.Dispose();if(failure!=null){Console.Error.WriteLine("FAIL: "+failure);Environment.ExitCode=1;}
 }
}
