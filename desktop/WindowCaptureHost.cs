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
                string line;while((line=Console.ReadLine())!=null){try{
                    string[] values=line.Split(' ');var target=new WindowTarget{Handle=long.Parse(values[0]),ProcessId=int.Parse(values[1]),Started=long.Parse(values[2])};
                    using(var bitmap=WindowSource.Capture(target,values[3]=="1"))using(var stream=new MemoryStream()){bitmap.Save(stream,ImageFormat.Png);byte[] bytes=stream.ToArray();output.Write(bytes.Length);output.Write(bytes);output.Flush();}
                }catch(Exception e){output.Write(-1);output.Write(e.Message);output.Flush();}}
            }
        }
    }
}
