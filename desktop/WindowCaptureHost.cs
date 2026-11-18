using System;
using System.IO;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Threading;
using System.Runtime.InteropServices;
namespace BooxImages {
    static class WindowCaptureHost {
        [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
        static void Main(string[] args){
            if(args.Length!=2)return;SetProcessDPIAware();int parentId=int.Parse(args[0]);long started=long.Parse(args[1]);
            using(var watch=new Timer(delegate{try{using(var parent=Process.GetProcessById(parentId))if(parent.HasExited||parent.StartTime.ToUniversalTime().Ticks!=started)Environment.Exit(0);}catch{Environment.Exit(0);}},null,0,500))
            using(var output=new BinaryWriter(Console.OpenStandardOutput())){
                WindowTarget bound=null;
                string line;while((line=Console.ReadLine())!=null){try{
                    string[] values=line.Split(' ');var target=new WindowTarget{Handle=long.Parse(values[0]),ProcessId=int.Parse(values[1]),Started=long.Parse(values[2])};
                    bool locked=values.Length==5 && values[4]=="1";
                    if(locked && (bound==null || bound.Handle!=target.Handle || bound.ProcessId!=target.ProcessId || bound.Started!=target.Started))
                        throw new InvalidOperationException("等待解锁后绑定窗口。");
                    using(var bitmap=WindowSource.Capture(target,!locked && values[3]=="1",locked))
                    using(var stream=new MemoryStream()){
                        if(!locked)bound=target;
                        bitmap.Save(stream,ImageFormat.Png);
                        byte[] bytes=stream.ToArray();output.Write(bytes.Length);output.Write(bytes);output.Flush();
                    }
                }catch(Exception e){output.Write(-1);output.Write(e.Message);output.Flush();}}
            }
        }
    }
}
