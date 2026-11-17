using System;
using System.IO;
using System.Drawing;
using System.Web.Script.Serialization;

namespace BooxImages {
    public sealed class ImageSettings {
        public int Direction {get;set;}
        public int Enhancement {get;set;}
        public bool Grayscale {get;set;}
        public int Contrast {get;set;}
        public bool ShowCursor {get;set;}
        public double IntervalSeconds {get;set;}
        public bool FullScreen {get;set;}
        public int RegionX {get;set;}
        public int RegionY {get;set;}
        public int RegionWidth {get;set;}
        public int RegionHeight {get;set;}
        public bool AutoScroll {get;set;}
        public double ScrollSeconds {get;set;}
        public int ScrollLines {get;set;}
        public int ScrollMode {get;set;}
        public int FullRefreshEvery {get;set;}
        public ImageSettings(){Enhancement=1;Grayscale=true;IntervalSeconds=.5;FullScreen=true;ScrollSeconds=5;ScrollLines=3;}
        public static double NormalizeInterval(double value){return value==.1||value==.2||value==.5||value==1||value==2?value:.5;}
        public bool TryRestoreRegion(Rectangle desktop,out Rectangle region){
            region=Rectangle.Empty;
            if(FullScreen||RegionWidth<20||RegionHeight<20)return false;
            long right=(long)RegionX+RegionWidth,bottom=(long)RegionY+RegionHeight;
            if(RegionX<desktop.Left||RegionY<desktop.Top||right>desktop.Right||bottom>desktop.Bottom)return false;
            region=new Rectangle(RegionX,RegionY,RegionWidth,RegionHeight);return true;
        }
        public static ImageSettings Load(string path) {
            try {
                var settings=new JavaScriptSerializer().Deserialize<ImageSettings>(File.ReadAllText(path));
                if(settings==null)return new ImageSettings();
                if(settings.Direction<0||settings.Direction>1)settings.Direction=0;
                if(settings.Enhancement<0||settings.Enhancement>3)settings.Enhancement=1;
                if(settings.Contrast<0||settings.Contrast>2)settings.Contrast=0;
                settings.IntervalSeconds=NormalizeInterval(settings.IntervalSeconds);
                if(double.IsNaN(settings.ScrollSeconds)||double.IsInfinity(settings.ScrollSeconds)||settings.ScrollSeconds<.1||settings.ScrollSeconds>3600)settings.ScrollSeconds=5;
                if(settings.ScrollLines<1||settings.ScrollLines>100)settings.ScrollLines=3;
                if(settings.ScrollMode<0||settings.ScrollMode>1)settings.ScrollMode=0;
                if(settings.FullRefreshEvery<0||settings.FullRefreshEvery>1000)settings.FullRefreshEvery=0;
                return settings;
            }catch(IOException){return new ImageSettings();}catch(UnauthorizedAccessException){return new ImageSettings();}catch(ArgumentException){return new ImageSettings();}catch(InvalidOperationException){return new ImageSettings();}
        }
        public void Save(string path) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp=path+".tmp";
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this));
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }
    }
}
