using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BooxImages {
    // Runs after resize so thin text is enhanced at the actual transmitted size.
    public static class ImageEnhancement {
        public static unsafe void Apply(Bitmap image,int mode,bool grayscale=false) {
            if(mode==0)return;
            if(mode<0||mode>3)throw new ArgumentOutOfRangeException("mode");
            int width=image.Width,height=image.Height;
            var bounds=new Rectangle(0,0,width,height);
            BitmapData data=image.LockBits(bounds,ImageLockMode.ReadWrite,PixelFormat.Format24bppRgb);
            try {
                int stride=Math.Abs(data.Stride),length=stride*height;
                byte[] input=new byte[length];
                for(int y=0;y<height;y++)Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),input,y*stride,stride);
                Parallel.For(0,height,new ParallelOptions {MaxDegreeOfParallelism=Math.Min(4,Environment.ProcessorCount)},delegate(int y) {
                  fixed(byte* pixels=input) {
                    byte* output=(byte*)data.Scan0+y*data.Stride;
                    int above=Math.Max(0,y-1)*stride,below=Math.Min(height-1,y+1)*stride,row=y*stride;
                    for(int x=0;x<width;x++) {
                        int left=Math.Max(0,x-1)*3,right=Math.Min(width-1,x+1)*3,pixel=row+x*3;
                        for(int c=0;c<(grayscale?1:3);c++) {
                            int center=pixels[pixel+c],north=pixels[above+x*3+c],south=pixels[below+x*3+c],west=pixels[row+left+c],east=pixels[row+right+c];
                            // Weighted unsharp mask; stronger mode also deepens nearby dark strokes.
                            int blur=(4*center+north+south+west+east+4)/8;
                            int sharp=center+(center-blur)*(mode==1?2:3);
                            if(mode>=2) {
                                int darkest=Math.Min(center,Math.Min(Math.Min(north,south),Math.Min(west,east)));
                                if(darkest<160)sharp-=(center-darkest)/5;
                            }
                            output[x*3+c]=(byte)Math.Max(0,Math.Min(255,sharp));
                        }
                        int offset=x*3;
                        if(grayscale)output[offset+1]=output[offset+2]=output[offset];
                        if(mode==3) {
                            int luma=(114*output[offset]+587*output[offset+1]+299*output[offset+2])/1000;
                            byte value=(byte)(luma<190?0:255);output[offset]=output[offset+1]=output[offset+2]=value;
                        }
                    }
                  }
                });
            }finally {image.UnlockBits(data);}
        }
    }
}
