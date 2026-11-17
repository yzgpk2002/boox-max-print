using System;
using System.IO;
using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using BooxImages;

// Opt-in integration test: production form/timers, mock transport, generated Chrome page only.
class ChromeScrollIntegrationTests {
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int count);
    [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    static bool MatchesTarget(string[] args,StringBuilder title){
        foreach(string arg in args)if(arg.StartsWith("target=")){
            string[] pair=arg.Substring(7).Split(':');uint pid;IntPtr hwnd=GetForegroundWindow();GetWindowThreadProcessId(hwnd,out pid);
            return pair.Length==2&&hwnd==new IntPtr(long.Parse(pair[0]))&&pid==uint.Parse(pair[1]);
        }
        return title.ToString()=="BOOX controlled browser test - Google Chrome";
    }
    static object Get(object x,string n){return x.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(x);}
    static void Set(object x,string n,object v){x.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(x,v);}
    static object Call(object x,string n,params object[] args){return x.GetType().GetMethod(n,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(x,args);}
    static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    sealed class Transport:HttpMessageHandler {
        internal int frames;string session="";bool paused;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
            if(request.RequestUri.AbsolutePath=="/control") {var body=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(await request.Content.ReadAsStringAsync());session=(string)body["session"];paused=(string)body["action"]=="pause";}
            if(request.RequestUri.AbsolutePath=="/frame")frames++;
            string json=new JavaScriptSerializer().Serialize(new {session=session,paused=paused,sha256="",fullRefreshSupported=true});
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)};
        }
    }
    [STAThread] static void Main(string[] args){
        Application.EnableVisualStyles();var context=new ApplicationContext();var timer=new System.Windows.Forms.Timer{Interval=20};Exception failure=null;
        timer.Tick+=async delegate {timer.Stop();SenderForm form=null;try {
            var title=new StringBuilder(200);
            Console.WriteLine("WAITING: activate the generated BOOX controlled browser test page within 60 seconds; no input until exact target matches.");
            for(int i=0;i<600;i++){title.Clear();GetWindowText(GetForegroundWindow(),title,title.Capacity);if(MatchesTarget(args,title))break;await Task.Delay(100);}
            Check(MatchesTarget(args,title),"Only the generated browser or explicitly selected HWND/PID may receive input");
            string root=Path.GetFullPath("build/chrome-scroll-integration");Directory.CreateDirectory(Path.Combine(root,".local"));
            bool live=Array.IndexOf(args,"live")>=0;
            if(live)File.Copy(".local/image-client.json",Path.Combine(root,".local/image-client.json"),true);
            else File.WriteAllText(Path.Combine(root,".local/image-client.json"),"{\"url\":\"http://127.0.0.1:1\",\"key\":\"test-only\"}");
            bool page=args.Length>0&&args[0]=="page";
            Rect native;Check(GetWindowRect(GetForegroundWindow(),out native),"Cannot locate generated browser window");
            Rectangle bounds=Rectangle.Intersect(new Rectangle(native.Left,native.Top,native.Right-native.Left,native.Bottom-native.Top),SystemInformation.VirtualScreen);
            if(!live)bounds=new Rectangle(bounds.Left,bounds.Top,20,20);
            new ImageSettings {AutoScroll=true,ScrollSeconds=.5,ScrollLines=3,ScrollMode=page?1:0,IntervalSeconds=.2,FullScreen=false,RegionX=bounds.X,RegionY=bounds.Y,RegionWidth=bounds.Width,RegionHeight=bounds.Height}.Save(Path.Combine(root,".local/image-settings.json"));
            var transport=new Transport();form=new SenderForm(root,live?null:transport);
            await (Task)Call(form,"Begin",true,false);
            await Task.Delay(live?6000:3500);
            Console.WriteLine("DIAGNOSTIC: automatic={0}; capture={1}; transfer={2}; scroll={3}",Get(form,"automatic"),Get(form,"capturing"),Get(form,"transferStatus"),Get(form,"scrollStatus"));
            Check((bool)Get(form,"automatic"),"Capture should be running");
            Check(((AutoScroller)Get(form,"scroller")).Armed,"Foreground lock unexpectedly stopped");
            Check(live?((string)Get(form,"ackHash")).Length>0:transport.frames>0,"No frame receipt");
            Console.WriteLine("STATE before pause: sequence={0}; acknowledged={1}; busy={2}; status={3}; native={4}",Get(form,"sequence"),((string)Get(form,"ackHash")).Length>0,Get(form,"scrollBusy"),Get(form,"scrollStatus"),((AutoScroller)Get(form,"scroller")).Diagnostic);
            if(Array.IndexOf(args,"focus")>=0){
                using(var guard=new Form{Text="BOOX focus guard verification",Width=400,Height=180,StartPosition=FormStartPosition.CenterScreen}){
                    guard.Show();guard.Activate();
                    await Task.Delay(800);
                    Check(GetForegroundWindow()==guard.Handle,"Controlled focus-loss window did not become foreground");
                    Check(!((AutoScroller)Get(form,"scroller")).Armed&&!((System.Windows.Forms.Timer)Get(form,"scrollTimer")).Enabled,"Foreground switch did not stop scroll timer");
                    Console.WriteLine("PASS: switching to the controlled focus window stopped automatic scrolling");
                    guard.Close();
                }
            }
            await (Task)Call(form,"Pause",false);
            Check(!((AutoScroller)Get(form,"scroller")).Armed&&!((System.Windows.Forms.Timer)Get(form,"scrollTimer")).Enabled,"Pause must stop scrolling");
            await Task.Delay(1000);
            Console.WriteLine("PASS: production start/timer/capture/receipt/pause completed ({0}, {1}); verify Chrome DOM events separately",page?"page":"lines",live?"real BOOX":"mock");
        }catch(Exception e){failure=e;}finally{if(form!=null){((NotifyIcon)Get(form,"tray")).Dispose();form.Dispose();}context.ExitThread();}};
        timer.Start();Application.Run(context);timer.Dispose();if(failure!=null){Console.Error.WriteLine("FAIL: "+failure);Environment.ExitCode=1;}
    }
}
