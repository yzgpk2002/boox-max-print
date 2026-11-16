using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using BooxImages;
class ImagePipelineBenchmark {
    static void Main(string[] args) {
        Directory.CreateDirectory(args[0]);
        using(var source=Image.FromFile("build/enhancement-verification/text-original.png")) {
            for(int mode=0;mode<4;mode++)for(int round=0;round<4;round++) {
                var sw=Stopwatch.StartNew();
                using(var target=new Bitmap(1200,1600,PixelFormat.Format24bppRgb)) {
                    using(var g=Graphics.FromImage(target))using(var attributes=new ImageAttributes()) {
                        var matrix=new ColorMatrix(new float[][]{new float[]{.299f,.299f,.299f,0,0},new float[]{.587f,.587f,.587f,0,0},new float[]{.114f,.114f,.114f,0,0},new float[]{0,0,0,1,0},new float[]{0,0,0,0,1}});
                        attributes.SetColorMatrix(matrix);g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                        g.DrawImage(source,new Rectangle(0,0,1200,1600),0,0,source.Width,source.Height,GraphicsUnit.Pixel,attributes);
                    }
                    long resize=sw.ElapsedMilliseconds;ImageEnhancementReference.Apply(target,mode);long enhance=sw.ElapsedMilliseconds;
                    using(var ms=new MemoryStream()) {
                        target.Save(ms,ImageFormat.Png);long encode=sw.ElapsedMilliseconds;
                        ms.Position=0;using(var decoded=Image.FromStream(ms))using(var clone=new Bitmap(decoded)){}
                        if(round>0)Console.WriteLine("mode={0} resize={1} enhance={2} png={3} preview={4} total={5} bytes={6}",mode,resize,enhance-resize,encode-enhance,sw.ElapsedMilliseconds-encode,sw.ElapsedMilliseconds,ms.Length);
                        if(round==3)File.WriteAllBytes(Path.Combine(args[0],"mode-"+mode+".png"),ms.ToArray());
                    }
                }
            }
        }
    }
}
