using System;
using System.IO;
using System.Drawing;
using BooxImages;
class ImageSettingsTests {
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Main(){try{Run();}catch(Exception e){Console.Error.WriteLine("FAIL: "+e.GetType().Name+": "+e.Message);Environment.ExitCode=1;}}
    static void Run(){
        string root=Path.GetFullPath("build/settings-tests");Directory.CreateDirectory(root);string path=Path.Combine(root,"settings.json");
        File.WriteAllText(path,"{\"Direction\":1,\"Enhancement\":2,\"ShowCursor\":true}");
        var migrated=ImageSettings.Load(path);Check(migrated.FullScreen&&migrated.IntervalSeconds==.5&&!migrated.AutoScroll,"Old settings gain safe defaults");
        foreach(double interval in new[]{.1,.2,.5,1.0,2.0}){new ImageSettings{IntervalSeconds=interval}.Save(path);Check(ImageSettings.Load(path).IntervalSeconds==interval,"Allowed interval restored "+interval);}
        foreach(double interval in new[]{5.0,10.0,-1.0,0.0}){new ImageSettings{IntervalSeconds=interval}.Save(path);Check(ImageSettings.Load(path).IntervalSeconds==.5,"Removed interval migrates to 0.5");}
        var settings=new ImageSettings{Direction=1,Enhancement=3,Grayscale=false,Contrast=2,ShowCursor=true,IntervalSeconds=.2,FullScreen=false,RegionX=-800,RegionY=20,RegionWidth=600,RegionHeight=900,AutoScroll=true,ScrollSeconds=1.7,ScrollLines=9,ScrollMode=1,FullRefreshEvery=7};
        settings.Save(path);var loaded=ImageSettings.Load(path);
        Check(loaded.Direction==1&&loaded.Enhancement==3&&!loaded.Grayscale&&loaded.Contrast==2&&loaded.ShowCursor,"Display settings persisted");
        Check(loaded.IntervalSeconds==.2&&!loaded.FullScreen&&loaded.RegionX==-800&&loaded.RegionY==20&&loaded.RegionWidth==600&&loaded.RegionHeight==900,"Source and interval persisted");
        Check(loaded.AutoScroll&&loaded.ScrollSeconds==1.7&&loaded.ScrollLines==9&&loaded.ScrollMode==1&&loaded.FullRefreshEvery==7,"Reading settings persisted");
        Rectangle region;Check(loaded.TryRestoreRegion(new Rectangle(-1000,0,2920,1080),out region)&&region.X==-800,"Negative-monitor coordinates restore");
        Check(!loaded.TryRestoreRegion(new Rectangle(0,0,1920,1080),out region),"Disconnected monitor selection rejected");
        loaded.RegionX=int.MaxValue;loaded.RegionWidth=int.MaxValue;Check(!loaded.TryRestoreRegion(new Rectangle(0,0,1920,1080),out region),"Overflowing region rejected");
        File.WriteAllText(path,"{\"ScrollSeconds\":-5,\"ScrollLines\":0,\"ScrollMode\":8,\"FullRefreshEvery\":1001}");
        loaded=ImageSettings.Load(path);Check(loaded.ScrollSeconds==5&&loaded.ScrollLines==3&&loaded.ScrollMode==0&&loaded.FullRefreshEvery==0,"Invalid reading settings recover");
        Check(AutoScroller.WheelDelta(3,3)==-120&&AutoScroller.WheelDelta(1,3)==-40&&AutoScroller.WheelDelta(9,3)==-360,"Wheel scaling follows Windows line count");
        bool rejected=false;try{AutoScroller.WheelDelta(3,-1);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Page-configured wheel cannot masquerade as exact lines");
        var a=new IntPtr(11);var b=new IntPtr(12);
        Check(AutoScroller.TargetIsCurrent(a,2,a,2,false),"Matching foreground allowed");
        Check(!AutoScroller.TargetIsCurrent(a,2,b,2,false),"Other foreground rejected");
        Check(!AutoScroller.TargetIsCurrent(a,2,a,3,false),"Reused handle/process rejected");
        Check(!AutoScroller.TargetIsCurrent(a,2,a,2,true),"Switching away then back remains disarmed");
        Check(!AutoScroller.TargetIsCurrent(IntPtr.Zero,2,IntPtr.Zero,2,false),"Unarmed input rejected");
        Console.WriteLine("PASS: all settings, legacy defaults, interval migration, monitor validation, scroll scaling and foreground safety policy; no input was injected");
    }
}
