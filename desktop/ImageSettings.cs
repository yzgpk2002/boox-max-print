using System;
using System.IO;
using System.Web.Script.Serialization;

namespace BooxImages {
    public sealed class ImageSettings {
        public int Direction {get;set;}
        public int Enhancement {get;set;}
        public bool Grayscale {get;set;}
        public int Contrast {get;set;}
        public bool ShowCursor {get;set;}
        public ImageSettings(){Enhancement=1;Grayscale=true;}
        public static ImageSettings Load(string path) {
            try {
                var settings=new JavaScriptSerializer().Deserialize<ImageSettings>(File.ReadAllText(path));
                if(settings==null)return new ImageSettings();
                if(settings.Direction<0||settings.Direction>1)settings.Direction=0;
                if(settings.Enhancement<0||settings.Enhancement>3)settings.Enhancement=1;
                if(settings.Contrast<0||settings.Contrast>2)settings.Contrast=0;
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
