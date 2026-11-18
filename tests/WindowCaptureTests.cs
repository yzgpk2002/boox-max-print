using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BooxImages;
class WindowCaptureTests {
 sealed class Fixture:Form {
  public bool Stall;
  protected override void WndProc(ref Message m){if(Stall&&(m.Msg==0x317||m.Msg==0x318))Thread.Sleep(4000);base.WndProc(ref m);}
 }
 static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 static int Child(WindowCaptureClient capture){var child=(Process)typeof(WindowCaptureClient).GetField("child",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(capture);return child==null?0:child.Id;}
 static bool Gone(int id){try{using(var p=Process.GetProcessById(id))return p.WaitForExit(1500);}catch(ArgumentException){return true;}}
 [STAThread]static void Main(){
  Application.EnableVisualStyles();
  using(var watchdog=new System.Threading.Timer(delegate{Console.Error.WriteLine("FAIL watchdog");Environment.Exit(2);},null,20000,Timeout.Infinite))
  using(var source=new Fixture{Text="BOOX generated capture fixture",Size=new Size(580,380),BackColor=Color.RoyalBlue,StartPosition=FormStartPosition.CenterScreen}){
   source.Shown+=async delegate{try{
    using(var black=new Bitmap(321,80,PixelFormat.Format24bppRgb)){
     Check(ScreenCapture.IsSolidBlack(black),"Black desktop must be held before adding cursor");
     black.SetPixel(320,79,Color.FromArgb(0,0,1));
     Check(!ScreenCapture.IsSolidBlack(black),"Dark content at the last pixel must survive row padding");
    }
    Check(DesktopGuard.Allows(true,true,true,true),"Window mode must allow normal saver");
    Check(!DesktopGuard.Allows(false,true,true,true),"Desktop must reject saver");
    Check(!DesktopGuard.Allows(true,false,true,false),"Secure desktop must reject capture");
    Check(!DesktopGuard.Allows(false,true,false,false),"Unknown saver state must reject desktop capture");
    WindowTarget target;using(var p=Process.GetCurrentProcess())target=new WindowTarget{Handle=source.Handle.ToInt64(),ProcessId=p.Id,Started=p.StartTime.ToUniversalTime().Ticks};
    using(var capture=new WindowCaptureClient()){
     using(var first=await capture.CaptureAsync(target,false))Check(first.GetPixel(first.Width/2,first.Height/2).ToArgb()==Color.RoyalBlue.ToArgb(),"First capture pixel");
     using(var overlay=new Form{Text="BOOX owned occlusion fixture",Bounds=source.Bounds,StartPosition=FormStartPosition.Manual,BackColor=Color.Red}){
      overlay.Show();source.BackColor=Color.ForestGreen;source.Invalidate();await Task.Delay(150);
      using(var second=await capture.CaptureAsync(target,false)){Check(second.GetPixel(second.Width/2,second.Height/2).ToArgb()==Color.ForestGreen.ToArgb(),"Occluded update must show source, not red overlay");second.Save("build/window-capture-second.png",ImageFormat.Png);}
     }
     int pid=Child(capture);capture.Dispose();Check(Gone(pid),"Disposed helper remains");
    }
    source.Hide();bool hiddenRejected=false;try{WindowSource.Validate(target);}catch(InvalidOperationException){hiddenRejected=true;}Check(hiddenRejected,"Hidden target accepted");source.Show();
    var invalid=new WindowTarget{Handle=target.Handle,ProcessId=target.ProcessId,Started=target.Started-1};bool identityRejected=false;try{WindowSource.Validate(invalid);}catch(InvalidOperationException){identityRejected=true;}Check(identityRejected,"Reused identity accepted");
    using(var capture=new WindowCaptureClient(Path.GetFullPath("build/StalledCaptureHost.exe"))){
     source.Stall=true;
     var clock=Stopwatch.StartNew();var work=capture.CaptureAsync(target,false);int stalledPid=Child(capture);bool timeout=false;try{using(var image=await work){}}catch(TimeoutException){timeout=true;}
     Check(timeout,"Blocking capture was not bounded");long elapsed=clock.ElapsedMilliseconds;Check(Gone(stalledPid),"Timed-out process remains");
     Check(elapsed<3000,"Capture timeout exceeded bound");Check(Child(capture)==0,"Timed-out helper retained");source.Stall=false;
    }
    Console.WriteLine("PASS: changed source behind overlay, hidden/identity rejection, desktop policy, bounded stalled capture, helper cleanup");
   }catch(Exception e){Console.Error.WriteLine("FAIL: "+e);Environment.ExitCode=1;}finally{source.Close();}};
   Application.Run(source);
  }
 }
}
