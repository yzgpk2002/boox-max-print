using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Collections.Generic;
class BuildImageIcons {
    static GraphicsPath Round(float x,float y,float w,float h,float r) {
        var p=new GraphicsPath();p.AddArc(x,y,r,r,180,90);p.AddArc(x+w-r,y,r,r,270,90);p.AddArc(x+w-r,y+h-r,r,r,0,90);p.AddArc(x,y+h-r,r,r,90,90);p.CloseFigure();return p;
    }
    static Bitmap Draw(int size,bool tray) {
        var b=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(b))using(var ink=new SolidBrush(Color.FromArgb(24,44,64)))using(var paper=new SolidBrush(Color.FromArgb(250,248,233)))using(var teal=new SolidBrush(Color.FromArgb(34,177,157)))using(var amber=new SolidBrush(Color.FromArgb(244,174,53))) {
            g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(size/256f,size/256f);
            using(var shape=Round(24,12,208,232,38))g.FillPath(ink,shape);
            using(var shape=Round(38,34,180,188,22))g.FillPath(paper,shape);
            // Two owl brows make the reader recognizable even at tray sizes.
            g.FillPolygon(ink,new[]{new Point(48,57),new Point(109,83),new Point(52,111)});
            g.FillPolygon(ink,new[]{new Point(208,57),new Point(147,83),new Point(204,111)});
            g.FillEllipse(teal,46,74,84,90);g.FillEllipse(teal,126,74,84,90);
            g.FillEllipse(paper,57,84,62,65);g.FillEllipse(paper,137,84,62,65);
            g.FillEllipse(ink,77,101,29,34);g.FillEllipse(ink,150,101,29,34);
            if(size>=32){g.FillEllipse(Brushes.White,82,104,9,10);g.FillEllipse(Brushes.White,155,104,9,10);}
            g.FillPolygon(amber,new[]{new Point(111,151),new Point(145,151),new Point(128,177)});
            using(var pen=new Pen(Color.FromArgb(24,44,64),tray?10:7)) {
                pen.StartCap=pen.EndCap=LineCap.Round;
                g.DrawLine(pen,72,190,184,190);if(!tray)g.DrawLine(pen,86,204,170,204);
            }
            g.FillEllipse(teal,120,228,16,8);
        }
        return b;
    }
    static void SaveIcon(string path,bool tray) {
        int[] sizes=tray?new[]{16,20,24,32,48}:new[]{16,20,24,32,48,64,128,256};var images=new List<byte[]>();
        foreach(int size in sizes)using(var b=Draw(size,tray))using(var ms=new MemoryStream()){b.Save(ms,ImageFormat.Png);images.Add(ms.ToArray());}
        using(var writer=new BinaryWriter(File.Create(path))) {
            writer.Write((short)0);writer.Write((short)1);writer.Write((short)sizes.Length);int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((short)0);writer.Write((short)1);writer.Write((short)32);writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length;}
            foreach(var data in images)writer.Write(data);
        }
    }
    static void Main(string[] args) {
        Directory.CreateDirectory(args[0]);SaveIcon(Path.Combine(args[0],"boox-images.ico"),false);SaveIcon(Path.Combine(args[0],"boox-tray.ico"),true);
        using(var b=Draw(256,false))b.Save(Path.Combine(args[0],"boox-images.png"),ImageFormat.Png);
        using(var b=Draw(32,true))b.Save(Path.Combine(args[0],"boox-tray.png"),ImageFormat.Png);
    }
}
