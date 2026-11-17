using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using BooxImages;

// Explicit opt-in only: creates its own editor process and sends input solely there.
class AutoScrollNativeTests {
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int count);
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    sealed class TestEditor:TextBox {
        int wheel,page;
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
        protected override void WndProc(ref Message m){
            bool observed=m.Msg==0x20a||m.Msg==0x100&&m.WParam.ToInt32()==0x22;
            if(m.Msg==0x20a)wheel++;if(m.Msg==0x100&&m.WParam.ToInt32()==0x22)page++;
            base.WndProc(ref m);
            if(observed&&FindForm()!=null){int row=SendMessage(Handle,0xce,IntPtr.Zero,IntPtr.Zero).ToInt32();FindForm().Text="BOOX controlled scroll test|"+row+"|"+wheel+"|"+page;}
        }
    }
    static int[] State(IntPtr hwnd){var text=new StringBuilder(200);GetWindowText(hwnd,text,text.Capacity);string[] fields=text.ToString().Split('|');Check(fields.Length==4,"Test window state unavailable");return new[]{int.Parse(fields[1]),int.Parse(fields[2]),int.Parse(fields[3])};}
    [STAThread] static void Main(string[] args){
        Application.EnableVisualStyles();
        if(args.Length>0&&args[0]=="--browser") {
            try {
                IntPtr hwnd=GetForegroundWindow();var title=new StringBuilder(300);GetWindowText(hwnd,title,title.Capacity);
                Check(title.ToString()=="BOOX controlled browser test - Google Chrome","Only the generated browser test may receive input");
                using(var scroll=new AutoScroller()) {string reason;Check(scroll.Arm(out reason),"Arm: "+reason);Check(scroll.Step(false,3,out reason),"Wheel: "+reason);for(int i=0;i<50;i++){Application.DoEvents();System.Threading.Thread.Sleep(10);}Check(scroll.Step(true,3,out reason),"Page: "+reason);Console.WriteLine("Browser test messages sent with foreground event loop; verify DOM scroll offset and recorded events separately.");}
            }catch(Exception e){Console.Error.WriteLine("FAIL: "+e.Message);Environment.ExitCode=1;}
            return;
        }
        if(args.Length>0&&args[0]=="--target") {
            using(var form=new Form{Text="BOOX controlled scroll test|0|0|0",Width=600,Height=400,StartPosition=FormStartPosition.CenterScreen}) {
                var edit=new TestEditor{Multiline=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,WordWrap=false};var content=new StringBuilder();for(int i=0;i<300;i++)content.AppendLine("Generated line "+i+" - BOOX automatic scrolling test only");edit.Text=content.ToString();form.Controls.Add(edit);form.Shown+=delegate{edit.SelectionStart=0;edit.SelectionLength=0;edit.ScrollToCaret();edit.Focus();};Application.Run(form);
            }
            return;
        }
        var context=new ApplicationContext();var timer=new Timer{Interval=20};Exception failure=null;
        timer.Tick+=async delegate {timer.Stop();Process child=null;try {
            child=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--target"){UseShellExecute=false,CreateNoWindow=true});
            for(int i=0;i<50&&child.MainWindowHandle==IntPtr.Zero;i++){await Task.Delay(100);child.Refresh();}
            IntPtr target=child.MainWindowHandle;Check(target!=IntPtr.Zero,"Controlled editor did not start");
            SetForegroundWindow(target);
            Console.WriteLine("Waiting up to 60 seconds for the controlled editor to be foreground; no input is sent while waiting.");
            for(int i=0;i<600&&GetForegroundWindow()!=target;i++)await Task.Delay(100);
            Check(GetForegroundWindow()==target,"Could not foreground controlled editor; no scroll sent");await Task.Delay(250);
            using(var scroll=new AutoScroller()) {
                string reason;Check(scroll.Arm(out reason),"Cannot arm controlled editor: "+reason);int[] before=State(target);
                Check(scroll.Step(false,3,out reason),"Wheel failed: "+reason);await Task.Delay(200);int[] afterWheel=State(target);
                Check(afterWheel[0]>before[0]&&afterWheel[1]>before[1],"Native editor did not scroll on targeted wheel");
                Check(scroll.Step(true,3,out reason),"Page failed: "+reason);await Task.Delay(200);int[] afterPage=State(target);
                Check(afterPage[0]>afterWheel[0]&&afterPage[2]>afterWheel[2],"Native editor did not page on targeted PageDown");
                using(var other=new Form{Text="BOOX controlled focus-loss test",Width=300,Height=180,StartPosition=FormStartPosition.CenterScreen}) {
                    other.Show();other.Activate();await Task.Delay(200);
                    Check(!scroll.Step(false,3,out reason),"Scroll must stop when controlled foreground changes");int[] stopped=State(target);Check(stopped[1]==afterPage[1]&&stopped[2]==afterPage[2],"Stopped target received extra scroll");other.Close();
                }
                Console.WriteLine("PASS: controlled native edit scrolled {0}->{1}->{2}; wheel and page messages delivered; focus switch blocked further input",before[0],afterWheel[0],afterPage[0]);
            }
        }catch(Exception e){failure=e;}finally{if(child!=null){try{child.Refresh();if(!child.HasExited){child.CloseMainWindow();if(!child.WaitForExit(3000))child.Kill();}}catch{}child.Dispose();}context.ExitThread();}};
        timer.Start();Application.Run(context);timer.Dispose();if(failure!=null){Console.Error.WriteLine("FAIL: "+failure.GetType().Name+": "+failure.Message);Environment.ExitCode=1;}
    }
}
