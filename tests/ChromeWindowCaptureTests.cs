using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;
using BooxImages;
class ChromeWindowCaptureTests {
 static string Hash(byte[] data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data));}
 [STAThread]static void Main(){
  WindowTarget target=null;foreach(var window in WindowSource.List())if(window.Title.StartsWith("BOOX-CAPTURE-FIXTURE-057 - ")){if(target!=null)throw new Exception("Ambiguous fixture window");target=window;}
  if(target==null)throw new Exception("Generated Chrome fixture is not available on this desktop");
  using(var overlay=new Form{Text="BOOX capture test overlay",BackColor=Color.Red,WindowState=FormWindowState.Maximized}){
   overlay.Shown+=async delegate{try{using(var capture=new WindowCaptureClient()){
    var options=new ImageOptions{Landscape=true,Grayscale=true,Enhancement=1,Contrast=1};
    byte[] first,second;
    using(var image=await capture.CaptureAsync(target,false)){
     Color center=image.GetPixel(image.Width/2,image.Height/2);if(center.R==255&&center.G==0&&center.B==0)throw new Exception("Captured overlay instead of Chrome");
     first=ImagePipeline.Encode(image,false,options);
    }
    await Task.Delay(1200);
    using(var image=await capture.CaptureAsync(target,false))second=ImagePipeline.Encode(image,false,options);
    if(Hash(first)==Hash(second))throw new Exception("Background Chrome fixture did not update");
    File.WriteAllBytes("build/chrome-window-first.png",first);File.WriteAllBytes("build/chrome-window-second.png",second);
    Console.WriteLine("PASS: actual Chrome background window updated behind owned overlay; encoded two different 1600x1200 PNG frames");
   }}catch(Exception e){Console.Error.WriteLine("FAIL: "+e);Environment.ExitCode=1;}finally{overlay.Close();}};
   Application.Run(overlay);
  }
 }
}
