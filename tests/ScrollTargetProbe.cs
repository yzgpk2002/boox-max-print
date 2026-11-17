using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Automation;
using BooxImages;

// Explicit HWND/PID only; never selects a user window by guessing a title.
class ScrollTargetProbe {
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 static string Position(IntPtr hwnd){try{var root=AutomationElement.FromHandle(hwnd);var elements=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty,true));string result="";for(int i=0;i<elements.Count&&i<8;i++){var p=(ScrollPattern)elements[i].GetCurrentPattern(ScrollPattern.Pattern);if(p.Current.VerticallyScrollable)result+=p.Current.VerticalScrollPercent.ToString("F3")+";";}return result.Length>0?result:"unavailable";}catch{return "unavailable";}}
 [STAThread]static void Main(string[] args){
  var target=new IntPtr(long.Parse(args[0]));uint pid=uint.Parse(args[1]);bool page=args.Length>2&&args[2]=="page";
  var context=new ApplicationContext();var timer=new Timer{Interval=20};Exception error=null;
  timer.Tick+=async delegate{timer.Stop();try{Console.WriteLine("WAIT: keep the explicitly selected Chrome window foreground for 10 seconds.");uint actual;
   for(int i=0;i<600&&GetForegroundWindow()!=target;i++)await Task.Delay(100);
   GetWindowThreadProcessId(target,out actual);if(GetForegroundWindow()!=target||actual!=pid)throw new Exception("Selected window not foreground or identity changed; no input sent");
   using(var scroll=new AutoScroller()){string reason;if(!scroll.Arm(out reason))throw new Exception(reason);Console.WriteLine("before="+Position(target));
    for(int i=0;i<3;i++){if(!scroll.Step(page,3,out reason))throw new Exception(reason);await Task.Delay(800);Console.WriteLine("after="+Position(target)+" "+scroll.Diagnostic);}
    scroll.Stop();Console.WriteLine("DONE: 3 bounded production scroll steps; no further input.");}
  }catch(Exception e){error=e;}finally{context.ExitThread();}};
  timer.Start();Application.Run(context);timer.Dispose();if(error!=null){Console.Error.WriteLine("FAIL: "+error.Message);Environment.ExitCode=1;}
 }
}
