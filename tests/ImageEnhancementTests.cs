using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using BooxImages;

class ImageEnhancementTests {
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static Bitmap Sample(){
        var image=new Bitmap(13,7,PixelFormat.Format24bppRgb);
        using(var g=Graphics.FromImage(image))g.Clear(Color.White);
        // Thin blurred vertical stroke surrounded by white; odd width tests row padding.
        for(int y=0;y<7;y++){image.SetPixel(5,y,Color.FromArgb(220,220,220));image.SetPixel(6,y,Color.FromArgb(140,140,140));image.SetPixel(7,y,Color.FromArgb(220,220,220));}
        return image;
    }
    static void Main(string[] args){
        using(var original=Sample())using(var clear=Sample())using(var deep=Sample())using(var binary=Sample()){
            ImageEnhancement.Apply(original,0);ImageEnhancement.Apply(clear,1);ImageEnhancement.Apply(deep,2);ImageEnhancement.Apply(binary,3);
            Check(original.GetPixel(6,3).R==140,"Original mode must preserve pixels");
            Check(clear.GetPixel(6,3).R<140,"Clear mode must darken a blurred stroke");
            Check(deep.GetPixel(5,3).R<clear.GetPixel(5,3).R,"Deep mode must strengthen the stroke edge");
            for(int y=0;y<7;y++)for(int x=0;x<13;x++){
                Color p=binary.GetPixel(x,y);Check((p.R==0||p.R==255)&&p.R==p.G&&p.G==p.B,"Binary mode contains gray");
                if(x<4||x>8)Check(clear.GetPixel(x,y).R==255&&deep.GetPixel(x,y).R==255,"White background changed");
            }
        }
        using(var pixel=new Bitmap(1,1,PixelFormat.Format24bppRgb)){pixel.SetPixel(0,0,Color.FromArgb(80,80,80));ImageEnhancement.Apply(pixel,1);Check(pixel.GetPixel(0,0).R==80,"Single-pixel bounds failed");}
        string folder=args[0];Directory.CreateDirectory(folder);
        using(var image=new Bitmap(1200,1600,PixelFormat.Format24bppRgb)){
            // Match the sender's Graphics/LockBits lifetime, and generate visual examples.
            using(var g=Graphics.FromImage(image)){
                g.Clear(Color.White);g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
                using(var title=new Font("Microsoft YaHei",30))g.DrawString("BOOX TEXT ENHANCEMENT",title,Brushes.Black,50,45);
                for(int size=9;size<=21;size+=3)using(var font=new Font("Microsoft YaHei",size)){
                    g.DrawString("\u5c0f\u6587\u5b57\u6e05\u6670\u5ea6\uff1a\u7535\u5b50\u7eb8\u9605\u8bfb ABC abc 0123456789  /  "+size+" pt",font,Brushes.Gray,50,150+(size-9)*40);
                }
                g.Flush();image.Save(Path.Combine(folder,"text-original.png"),ImageFormat.Png);
                var timer=Stopwatch.StartNew();ImageEnhancement.Apply(image,1);timer.Stop();
                image.Save(Path.Combine(folder,"text-clear.png"),ImageFormat.Png);
                Console.WriteLine("1200x1600 clear enhancement: "+timer.ElapsedMilliseconds+" ms");
            }
        }
        Console.WriteLine("PASS: stroke clarity, stronger edges, background preservation, binary output, odd stride, 1px bounds, active Graphics lifetime");
    }
}
