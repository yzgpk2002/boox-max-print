using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using BooxImages;

class ImagePipelineTests {
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static byte[] Pixels(Bitmap b){var data=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);try{var bytes=new byte[b.Width*b.Height*3];for(int y=0;y<b.Height;y++)Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),bytes,y*b.Width*3,b.Width*3);return bytes;}finally{b.UnlockBits(data);}}
    static void Equal(Bitmap a,Bitmap b,string label){var aa=Pixels(a);var bb=Pixels(b);Check(aa.Length==bb.Length,label+" size");for(int i=0;i<aa.Length;i++)Check(aa[i]==bb[i],label+" pixel "+i);}
    static void Main(string[] args){
        string folder=args[0];Directory.CreateDirectory(folder);
        // Compare against the previous serial implementation, including edge/padding cases.
        var random=new Random(42);
        foreach(bool gray in new[]{false,true})foreach(int width in new[]{1,13,101})using(var input=new Bitmap(width,71,PixelFormat.Format24bppRgb)) {
            for(int y=0;y<input.Height;y++)for(int x=0;x<width;x++){int v=random.Next(256);input.SetPixel(x,y,gray?Color.FromArgb(v,v,v):Color.FromArgb(v,random.Next(256),random.Next(256)));}
            for(int mode=0;mode<4;mode++)using(var expected=(Bitmap)input.Clone())using(var actual=(Bitmap)input.Clone()) {
                ImageEnhancementReference.Apply(expected,mode);ImageEnhancement.Apply(actual,mode,gray);Equal(expected,actual,"optimized mode "+mode+" gray "+gray);
            }
        }
        using(var source=Image.FromFile("build/enhancement-verification/text-original.png")) {
            for(int mode=0;mode<4;mode++)for(int round=0;round<4;round++) {
                var timer=Stopwatch.StartNew();byte[] bytes=ImagePipeline.Encode(source,false,new ImageOptions{Enhancement=mode});timer.Stop();
                if(round>0)Console.WriteLine("mode={0} total={1} bytes={2}",mode,timer.ElapsedMilliseconds,bytes.Length);
                if(round==3) {
                    File.WriteAllBytes(Path.Combine(folder,"mode-"+mode+".png"),bytes);
                    using(var old=new Bitmap("build/baseline-frames/mode-"+mode+".png"))using(var stream=new MemoryStream(bytes))using(var result=new Bitmap(stream))Equal(old,result,"lossless indexed PNG mode "+mode);
                }
            }
        }
        using(var wide=new Bitmap(1920,1080))foreach(bool landscape in new[]{false,true})using(var ms=new MemoryStream(ImagePipeline.Encode(wide,false,new ImageOptions{Landscape=landscape})))using(var result=new Bitmap(ms)){Check(result.Width==(landscape?1600:1200),"Orientation fit width");Check(result.Height==(landscape?900:675),"Orientation fit height");}
        string path=Path.Combine(folder,"settings.json");
        File.WriteAllText(path,"not-json");Check(ImageSettings.Load(path).Enhancement==1,"Corrupt preferences fallback");
        File.WriteAllText(path,"{\"Direction\":55,\"Enhancement\":-1,\"Contrast\":9}");var invalid=ImageSettings.Load(path);Check(invalid.Direction==0&&invalid.Enhancement==1&&invalid.Contrast==0,"Validate saved ranges");
        new ImageSettings{Direction=1,Enhancement=3,Contrast=2,Grayscale=false,ShowCursor=true}.Save(path);
        var saved=ImageSettings.Load(path);Check(saved.Direction==1&&saved.Enhancement==3&&saved.Contrast==2&&!saved.Grayscale&&saved.ShowCursor,"Preference round-trip");
        new ImageSettings{Direction=0,Enhancement=2,ShowCursor=false}.Save(path);Check(ImageSettings.Load(path).Enhancement==2&&!File.Exists(path+".tmp"),"Atomic preference replacement");
        Console.WriteLine("PASS: all enhancement pixels match old implementation; indexed PNG is lossless; both orientations; preferences recovery and persistence");
    }
}
