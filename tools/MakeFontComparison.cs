using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using BooxImages;
class MakeFontComparison {
    static void Text(Graphics g,string text,string face,float size,FontStyle style,RectangleF box){using(var f=new Font(face,size,style,GraphicsUnit.Pixel))g.DrawString(text,f,Brushes.Black,box);}
    static void Main(){
        const string ui="Microsoft YaHei UI";
        using(var image=new Bitmap(1200,1600,PixelFormat.Format24bppRgb)) {
            using(var g=Graphics.FromImage(image)) {
                g.Clear(Color.White);g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
                Text(g,"\u7070\u5ea6\u5b57\u4f53\u5bf9\u7167",ui,44,FontStyle.Bold,new RectangleF(54,44,1090,70));
                Text(g,"\u540c\u4e00\u9875\u5bf9\u6bd4\u5b57\u4f53\u4e0e\u5b57\u91cd\uff1b\u4e0a\u534a\u9875\u4e0d\u9510\u5316\uff0c\u4fdd\u7559\u7070\u5ea6\u8fb9\u7f18\u3002",ui,23,FontStyle.Regular,new RectangleF(54,115,1090,55));
                string[] faces={ui,"Noto Sans SC","Noto Sans SC Medium",ui};
                for(int i=0;i<4;i++) {
                    int y=190+i*245;string label=(i+1)+". "+faces[i]+(i==3?" Bold":"");
                    Text(g,label,ui,26,FontStyle.Bold,new RectangleF(54,y,1090,45));
                    int row=0;foreach(int size in new[]{20,24,28,32}) {
                        Text(g,size+"px",ui,18,FontStyle.Regular,new RectangleF(54,y+49+row*43,82,42));
                        Text(g,"\u58a8\u6c34\u5c4f\u9605\u8bfb\uff1a\u8fb9\u7f18\u5e73\u6ed1\uff0c\u7b14\u753b\u6e05\u695a ABC 0123456789",faces[i],size,i==3?FontStyle.Bold:FontStyle.Regular,new RectangleF(145,y+44+row*43,1000,46));row++;
                    }
                    g.DrawLine(Pens.LightGray,54,y+229,1146,y+229);
                }
                Text(g,"\u540c\u4e00\u5b57\u4f53\u7684\u56db\u79cd\u589e\u5f3a\uff08Noto Sans SC Medium 28px\uff09",ui,25,FontStyle.Bold,new RectangleF(54,1190,1090,50));
            }
            for(int mode=0;mode<4;mode++)using(var sample=new Bitmap(262,220,PixelFormat.Format24bppRgb)) {
                using(var g=Graphics.FromImage(sample)){g.Clear(Color.White);g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;Text(g,"\u7b14\u753b\u8fb9\u7f18\n\u5e73\u6ed1\u9605\u8bfb\nAa 012345","Noto Sans SC Medium",28,FontStyle.Regular,new RectangleF(12,5,244,190));}
                ImageEnhancement.Apply(sample,mode,true);
                using(var g=Graphics.FromImage(image)){g.DrawImageUnscaled(sample,54+mode*278,1285);string[] labels={"\u539f\u56fe","\u6587\u5b57\u6e05\u6670","\u6587\u5b57\u52a0\u6df1","\u9ed1\u767d\u9ad8\u5bf9\u6bd4"};Text(g,labels[mode],ui,23,FontStyle.Regular,new RectangleF(54+mode*278,1247,270,38));}
            }
            using(var g=Graphics.FromImage(image))Text(g,"\u5148\u770b 24\u201332px \u7684\u8212\u9002\u5ea6\uff1b\u6700\u53f3\u5217\u7eaf\u9ed1\u767d\u6709\u610f\u4fdd\u7559\u4f5c\u4e3a\u952f\u9f7f\u5bf9\u7167\u3002",ui,22,FontStyle.Regular,new RectangleF(54,1520,1090,60));
            Directory.CreateDirectory("build/font-comparison");
            File.WriteAllBytes("build/font-comparison/fonts.png",ImagePipeline.Encode(image,false,new ImageOptions {Enhancement=0}));
        }
    }
}
