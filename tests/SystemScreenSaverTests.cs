using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BooxImages;
class SystemScreenSaverTests {
 static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 [STAThread]static void Main(string[] args){
  if(args.Length>0&&args[0]=="guard"){
   Process saver=null;try{
    Check(DesktopGuard.CanCapture(false),"Baseline desktop not available");
    saver=Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"scrnsave.scr"),"/s"){UseShellExecute=false});
    Thread.Sleep(800);Check(!saver.HasExited,"Screen saver was dismissed before check");
    bool allowed=DesktopGuard.CanCapture(false);Console.WriteLine("Real system scrnsave.scr active; desktopAllowed="+allowed);Check(!allowed,"Real screen saver leaked into desktop capture");
    Console.WriteLine("PASS: real system screen saver is blocked");
   }catch(Exception e){Console.Error.WriteLine("FAIL: "+e.Message);Environment.ExitCode=1;}
   finally{if(saver!=null){if(!saver.HasExited){saver.Kill();saver.WaitForExit(2000);}saver.Dispose();}}
   return;
  }
  Application.EnableVisualStyles();
  using(var source=new Form{Text="BOOX system screensaver fixture",Size=new Size(580,380),BackColor=Color.RoyalBlue,StartPosition=FormStartPosition.CenterScreen}){
   source.Shown+=async delegate{Process saver=null;try{
    await Task.Delay(300);
    WindowTarget target;using(var current=Process.GetCurrentProcess())target=new WindowTarget{Handle=source.Handle.ToInt64(),ProcessId=current.Id,Started=current.StartTime.ToUniversalTime().Ticks};
    Check(DesktopGuard.CanCapture(false),"Baseline desktop not available");
    using(var capture=new WindowCaptureClient()){
     Console.WriteLine("Capturing baseline window");
     using(var before=await capture.CaptureAsync(target,false))Check(before.GetPixel(before.Width/2,before.Height/2).ToArgb()==Color.RoyalBlue.ToArgb(),"Baseline source mismatch");
     saver=Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"scrnsave.scr"),"/s"){UseShellExecute=false});
     await Task.Delay(600);Check(!saver.HasExited,"System screensaver ended before test (mouse/input may dismiss it)");
     bool desktopAllowed=DesktopGuard.CanCapture(false);Console.WriteLine("System scrnsave.scr active; desktopAllowed="+desktopAllowed);
     Check(!desktopAllowed,"Desktop capture would still send the real system screensaver");
     source.BackColor=Color.ForestGreen;source.Invalidate();
     using(var during=await capture.CaptureAsync(target,false))Check(during.GetPixel(during.Width/2,during.Height/2).ToArgb()==Color.ForestGreen.ToArgb(),"Selected window did not update behind real system screensaver");
     Check(!saver.HasExited,"Screensaver ended during capture");
     Check(!DesktopGuard.CanCapture(false),"Screensaver capture became allowed during test");
    }
    Console.WriteLine("PASS: real Windows scrnsave.scr rejected for desktop; selected window updated behind it");
   }catch(Exception e){Console.Error.WriteLine("FAIL: "+e.Message);Environment.ExitCode=1;}finally{
    if(saver!=null){try{if(!saver.HasExited){saver.Kill();saver.WaitForExit(2000);}}finally{saver.Dispose();}}
    source.Close();
   }};Application.Run(source);
  }
 }
}
