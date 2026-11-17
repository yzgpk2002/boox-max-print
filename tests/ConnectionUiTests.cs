using System;
using System.IO;
using System.Reflection;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BooxImages;
class ConnectionUiTests {
 static object Get(object o,string n){return o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);}
 static void Set(object o,string n,object v){o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v);}
 static object Call(object o,string n){return o.GetType().GetMethod(n,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);}
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 sealed class Transport:HttpMessageHandler {
  internal int pairs;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
   if(request.RequestUri.AbsolutePath=="/pair")pairs++;
   string body="{\"computer\":{\"ssids\":[\"Test WiFi\"],\"status\":\"connected\"},\"boox\":{\"online\":true,\"ssid\":\"Test WiFi\"},\"pairing\":{\"running\":false,\"status\":\"success\",\"message\":\"ok\"},\"session\":\"\",\"sha256\":\"\",\"paused\":true}";
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});
  }
 }
 [STAThread]static void Main(){
  Exception failure=null;var context=new ApplicationContext();var timer=new System.Windows.Forms.Timer{Interval=100};
  timer.Tick+=async delegate{timer.Stop();SenderForm form=null;try{
   string root=Path.GetFullPath("build/connection-ui");Directory.CreateDirectory(Path.Combine(root,".local"));
   File.WriteAllText(Path.Combine(root,".local/image-client.json"),"{\"url\":\"http://127.0.0.1:1\",\"key\":\"test-only\"}");
   var transport=new Transport();form=new SenderForm(root,transport);form.Show();((System.Windows.Forms.Timer)Get(form,"pollTimer")).Stop();
   Set(form,"automatic",true);Set(form,"session",new string('c',32));
   var capture=(System.Windows.Forms.Timer)Get(form,"captureTimer");capture.Interval=60000;capture.Start();
   var button=(Button)Get(form,"pairButton");button.PerformClick();
   Check(transport.pairs==1,"Pair button dispatches exactly one pairing request");
   for(int wait=0;wait<100&&(bool)Get(form,"controlling");wait++)await Task.Delay(20);
   Check(!(bool)Get(form,"controlling"),"Pair request completed within deadline");
   Check(!(bool)Get(form,"automatic")&&!capture.Enabled&&(string)Get(form,"session")=="","Pairing stops capture and releases old session");
   Set(form,"nextNetwork",DateTime.MinValue);await (Task)Call(form,"RefreshNetwork");
   var label=(Label)Get(form,"networkLabel");Check(label.Text.Contains("Test WiFi")&&label.Text.Contains("BOOX Wi-Fi"),"Footer displays both Wi-Fi names");
   Check(!(bool)Get(form,"pairPending")&&button.Enabled,"Successful pairing completes and re-enables button");
   Check(label.Height>=40&&button.Bottom<=button.Parent.ClientSize.Height,"Connection footer fits its row");
   using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(root,"layout.png"),ImageFormat.Png);}
   Console.WriteLine("PASS: production re-pair button, idle capture after pairing, both Wi-Fi labels, completion state and footer layout");
  }catch(Exception e){failure=e;}finally{if(form!=null){Set(form,"closing",true);((System.Windows.Forms.Timer)Get(form,"pollTimer")).Stop();((System.Windows.Forms.Timer)Get(form,"captureTimer")).Stop();((NotifyIcon)Get(form,"tray")).Dispose();form.Dispose();}context.ExitThread();}};
  timer.Start();Application.Run(context);timer.Dispose();if(failure!=null){Console.Error.WriteLine("FAIL: "+failure);Environment.ExitCode=1;}
 }
}
