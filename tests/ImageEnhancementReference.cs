using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BooxImages {
    // Runs after resize so thin text is enhanced at the actual transmitted size.
    public static class ImageEnhancementReference {
        public static void Apply(Bitmap image,int mode) {
            if(mode==0)return;
            if(mode<0||mode>3)throw new ArgumentOutOfRangeException("mode");
            int width=image.Width,height=image.Height;
            var bounds=new Rectangle(0,0,width,height);
            BitmapData data=image.LockBits(bounds,ImageLockMode.ReadWrite,PixelFormat.Format24bppRgb);
            try {
                int stride=Math.Abs(data.Stride),length=stride*height;
                byte[] input=new byte[length],output=new byte[length];
                for(int y=0;y<height;y++)Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),input,y*stride,stride);
                Buffer.BlockCopy(input,0,output,0,length);
                for(int y=0;y<height;y++) {
                    int above=Math.Max(0,y-1)*stride,below=Math.Min(height-1,y+1)*stride,row=y*stride;
                    for(int x=0;x<width;x++) {
                        int left=Math.Max(0,x-1)*3,right=Math.Min(width-1,x+1)*3,pixel=row+x*3;
                        for(int c=0;c<3;c++) {
                            int center=input[pixel+c],north=input[above+x*3+c],south=input[below+x*3+c],west=input[row+left+c],east=input[row+right+c];
                            // Weighted unsharp mask; stronger mode also deepens nearby dark strokes.
                            int blur=(4*center+north+south+west+east+4)/8;
                            int sharp=center+(center-blur)*(mode==1?2:3);
                            if(mode>=2) {
                                int darkest=Math.Min(center,Math.Min(Math.Min(north,south),Math.Min(west,east)));
                                if(darkest<160)sharp-=(center-darkest)/5;
                            }
                            output[pixel+c]=(byte)Math.Max(0,Math.Min(255,sharp));
                        }
                        if(mode==3) {
                            int luma=(114*output[pixel]+587*output[pixel+1]+299*output[pixel+2])/1000;
                            byte value=(byte)(luma<190?0:255);output[pixel]=output[pixel+1]=output[pixel+2]=value;
                        }
                    }
                }
                for(int y=0;y<height;y++)Marshal.Copy(output,y*stride,IntPtr.Add(data.Scan0,y*data.Stride),stride);
            }finally {image.UnlockBits(data);}
        }
    }
}
