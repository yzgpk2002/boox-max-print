using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace BooxImages {
    public sealed class ImageOptions {
        public bool Landscape,Grayscale=true,ShowCursor;
        public int Enhancement=1;
        public float Contrast=1;
    }
    public static class ImagePipeline {
        public static byte[] Encode(Image source,bool file,ImageOptions options) {
            double targetWidth=options.Landscape?1600:1200,targetHeight=options.Landscape?1200:1600;
            double scale=file?Math.Min(1,targetWidth/source.Width):Math.Min(1,Math.Min(targetWidth/source.Width,targetHeight/source.Height));
            scale=Math.Min(scale,Math.Sqrt(4000000.0/((double)source.Width*source.Height)));
            int width=Math.Max(1,(int)(source.Width*scale)),height=Math.Max(1,(int)(source.Height*scale));
            using(var target=new Bitmap(width,height,PixelFormat.Format24bppRgb)) {
                using(var g=Graphics.FromImage(target))using(var attributes=new ImageAttributes()) {
                    g.Clear(Color.White);g.InterpolationMode=InterpolationMode.HighQualityBicubic;float c=options.Contrast,bias=(1-c)/2;
                    var matrix=new ColorMatrix();if(options.Grayscale){matrix.Matrix00=.299f*c;matrix.Matrix01=.299f*c;matrix.Matrix02=.299f*c;matrix.Matrix10=.587f*c;matrix.Matrix11=.587f*c;matrix.Matrix12=.587f*c;matrix.Matrix20=.114f*c;matrix.Matrix21=.114f*c;matrix.Matrix22=.114f*c;}else{matrix.Matrix00=matrix.Matrix11=matrix.Matrix22=c;}
                    matrix.Matrix40=matrix.Matrix41=matrix.Matrix42=bias;attributes.SetColorMatrix(matrix);g.DrawImage(source,new Rectangle(0,0,width,height),0,0,source.Width,source.Height,GraphicsUnit.Pixel,attributes);
                }
                ImageEnhancement.Apply(target,options.Enhancement,options.Grayscale);
                using(var buffer=new MemoryStream()) {
                    // Lossless indexed PNG: the same gray values, fewer bytes and less decoding work.
                    if(options.Grayscale||options.Enhancement==3)using(var indexed=ToGrayscalePalette(target))indexed.Save(buffer,ImageFormat.Png);
                    else target.Save(buffer,ImageFormat.Png);
                    if(buffer.Length>8*1024*1024)throw new Exception("Image exceeds 8 MB");return buffer.ToArray();
                }
            }
        }
        static unsafe Bitmap ToGrayscalePalette(Bitmap source) {
            int width=source.Width,height=source.Height;var target=new Bitmap(width,height,PixelFormat.Format8bppIndexed);
            var palette=target.Palette;for(int i=0;i<256;i++)palette.Entries[i]=Color.FromArgb(i,i,i);target.Palette=palette;
            var bounds=new Rectangle(0,0,width,height);
            var input=source.LockBits(bounds,ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
            try {
                var output=target.LockBits(bounds,ImageLockMode.WriteOnly,PixelFormat.Format8bppIndexed);
                try {for(int y=0;y<height;y++) {byte* src=(byte*)input.Scan0+y*input.Stride,dst=(byte*)output.Scan0+y*output.Stride;for(int x=0;x<width;x++)dst[x]=src[x*3];}}
                finally{target.UnlockBits(output);}
            }catch{target.Dispose();throw;}finally{source.UnlockBits(input);}
            return target;
        }
    }
}
