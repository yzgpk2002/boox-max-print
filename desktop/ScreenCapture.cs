using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BooxImages {
    public static class ScreenCapture {
        [StructLayout(LayoutKind.Sequential)] struct CursorInfo {public int Size,Flags;public IntPtr Cursor;public Point Position;}
        [StructLayout(LayoutKind.Sequential)] struct IconInfo {public bool IsIcon;public int HotspotX,HotspotY;public IntPtr Mask,Color;}
        [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CursorInfo info);
        [DllImport("user32.dll")] static extern IntPtr CopyIcon(IntPtr icon);
        [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon,out IconInfo info);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr dc,int x,int y,IntPtr icon,int width,int height,int step,IntPtr brush,int flags);
        public static Bitmap Capture(Rectangle region,bool showCursor) {
            var image=new Bitmap(region.Width,region.Height,PixelFormat.Format24bppRgb);
            try {
                using(var g=Graphics.FromImage(image)) {
                    g.CopyFromScreen(region.Location,Point.Empty,region.Size);
                    if(showCursor)DrawCursor(g,region.Location);
                }
                return image;
            }catch{image.Dispose();throw;}
        }
        public static void DrawCursor(Graphics graphics,Point origin) {
            var info=new CursorInfo {Size=Marshal.SizeOf(typeof(CursorInfo))};
            if(!GetCursorInfo(ref info)||(info.Flags&1)==0)return;
            IntPtr icon=CopyIcon(info.Cursor);if(icon==IntPtr.Zero)return;
            try {
                IconInfo details;if(!GetIconInfo(icon,out details))return;
                try {
                    IntPtr dc=graphics.GetHdc();
                    try {DrawIconEx(dc,info.Position.X-origin.X-details.HotspotX,info.Position.Y-origin.Y-details.HotspotY,icon,0,0,0,IntPtr.Zero,3);}
                    finally{graphics.ReleaseHdc(dc);}
                }finally{if(details.Mask!=IntPtr.Zero)DeleteObject(details.Mask);if(details.Color!=IntPtr.Zero)DeleteObject(details.Color);}
            }finally{DestroyIcon(icon);}
        }
    }
}
