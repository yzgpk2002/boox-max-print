using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace BooxImages {
    // Never switch desktops or wake/dismiss the screen saver to obtain an image.
    public static class DesktopGuard {
        [DllImport("user32.dll")] static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
        [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder name,int size,out int needed);
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action,uint param,out bool value,uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetProcessWindowStation();
        [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint thread);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        delegate bool EnumWindow(IntPtr window,IntPtr param);
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct ProcessEntry {
            public uint Size,Usage,ProcessId;public UIntPtr Heap;public uint Module,Threads,ParentProcessId;public int Priority;public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string FileName;
        }
        [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint process);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32First(IntPtr snapshot,ref ProcessEntry entry);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32Next(IntPtr snapshot,ref ProcessEntry entry);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback,IntPtr param);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
        // .scr launched by preview/shortcut can cover Default without setting SPI_GETSCREENSAVERRUNNING.
        // Inspect actual visible windows; a dormant LockApp process alone is not a lock signal.
        static int VisibleProtection(bool includeSaver){
            var processes=new Dictionary<uint,int>();IntPtr snapshot=CreateToolhelp32Snapshot(2,0);
            if(snapshot==new IntPtr(-1))return -1;
            try{var entry=new ProcessEntry{Size=(uint)Marshal.SizeOf(typeof(ProcessEntry))};if(!Process32First(snapshot,ref entry))return -1;
                do{string file=entry.FileName;if(includeSaver&&file.EndsWith(".scr",StringComparison.OrdinalIgnoreCase))processes[entry.ProcessId]=1;
                    else if(file.Equals("LockApp.exe",StringComparison.OrdinalIgnoreCase)||file.Equals("LogonUI.exe",StringComparison.OrdinalIgnoreCase))processes[entry.ProcessId]=2;
                }while(Process32Next(snapshot,ref entry));
            }finally{CloseHandle(snapshot);}
            int protection=0;
            bool enumerated=EnumWindows(delegate(IntPtr window,IntPtr unused){
                if(!IsWindowVisible(window))return true;
                uint pid;GetWindowThreadProcessId(window,out pid);int kind;processes.TryGetValue(pid,out kind);
                if(kind==0&&includeSaver){var name=new StringBuilder(128);GetClassName(window,name,name.Capacity);if(name.ToString().Equals("WindowsScreenSaverClass",StringComparison.OrdinalIgnoreCase))kind=1;}
                if(kind==0)return true;int cloaked;if(DwmGetWindowAttribute(window,14,out cloaked,4)==0&&cloaked!=0)return true;
                protection=Math.Max(protection,kind);
                return true;
            },IntPtr.Zero);
            return enumerated?protection:-1;
        }
        public static string InstanceScope(){
            int needed;var station=new StringBuilder(256);var desktop=new StringBuilder(256);
            if(!GetUserObjectInformation(GetProcessWindowStation(),2,station,512,out needed)||!GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()),2,desktop,512,out needed))throw new InvalidOperationException("无法确定当前桌面，未启动发送器。");
            using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(station+"\\"+desktop))).Replace("-","").Substring(0,24);
        }
        public static bool Allows(bool windowMode,bool readableDefault,bool saverKnown,bool saverRunning){return readableDefault&&(windowMode||(saverKnown&&!saverRunning));}
        public static bool CanCapture(bool windowMode){return BlockReason(windowMode).Length==0;}
        public static string BlockReason(bool windowMode){
            IntPtr desktop=OpenInputDesktop(0,false,1);if(desktop==IntPtr.Zero)return "input-desktop-unavailable";
            try{int needed;var name=new StringBuilder(256);bool normal=GetUserObjectInformation(desktop,2,name,name.Capacity*2,out needed)&&name.ToString()=="Default";
                if(!normal)return "non-default-desktop";
                int visible=VisibleProtection(!windowMode);if(visible<0)return "window-inspection-unavailable";if(visible==2)return "visible-lock-window";
                if(windowMode)return "";
                if(visible==1)return "visible-screensaver-window";
                bool saver;bool known=SystemParametersInfo(0x72,0,out saver,0);return !known?"screensaver-state-unavailable":saver?"system-screensaver-running":"";
            }finally{CloseDesktop(desktop);}
        }
    }
    public sealed class WindowTarget {
        public long Handle;public int ProcessId;public long Started;public string Title;
        public override string ToString(){return Title;}
    }
    public static class WindowSource {
        delegate bool EnumWindow(IntPtr window,IntPtr param);
        [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback,IntPtr param);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int size);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int size);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
        public static List<WindowTarget> List(){
            var targets=new List<WindowTarget>();int self=Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr h,IntPtr p){try{uint pid;GetWindowThreadProcessId(h,out pid);if(pid==self||!IsWindowVisible(h)||IsIconic(h))return true;
                var title=new StringBuilder(512);GetWindowText(h,title,title.Capacity);var cls=new StringBuilder(128);GetClassName(h,cls,cls.Capacity);
                if(title.Length==0||cls.ToString()=="Progman"||cls.ToString()=="WorkerW"||cls.ToString().StartsWith("Shell_"))return true;
                using(var process=Process.GetProcessById((int)pid))targets.Add(new WindowTarget{Handle=h.ToInt64(),ProcessId=(int)pid,Started=process.StartTime.ToUniversalTime().Ticks,Title=title.ToString()});
            }catch(ArgumentException){}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}return true;},IntPtr.Zero);return targets;
        }
        public static void Validate(WindowTarget target){
            if(target==null)throw new InvalidOperationException("请先选择窗口。");
            IntPtr h=new IntPtr(target.Handle);uint pid;GetWindowThreadProcessId(h,out pid);
            if(pid!=target.ProcessId||!IsWindowVisible(h)||IsIconic(h))throw new InvalidOperationException("选定窗口已关闭、隐藏或最小化；保留最后画面，请恢复或重新选择窗口。");
            using(var process=Process.GetProcessById((int)pid))if(process.StartTime.ToUniversalTime().Ticks!=target.Started)throw new InvalidOperationException("原窗口已结束，请重新选择窗口。");
        }
        public static Bitmap Capture(WindowTarget target,bool cursor,bool redrawBoundWindow=false){
            Validate(target);if(!redrawBoundWindow && !DesktopGuard.CanCapture(true))throw new InvalidOperationException("当前桌面不可捕获。");
            IntPtr window=new IntPtr(target.Handle);Rect r;if(!GetWindowRect(window,out r))throw new InvalidOperationException("无法读取窗口大小。");
            int width=r.Right-r.Left,height=r.Bottom-r.Top;if(width<20||height<20||width>12000||height>12000||(long)width*height>40000000)throw new InvalidOperationException("窗口大小不支持。");
            var bitmap=new Bitmap(width,height,PixelFormat.Format24bppRgb);
            try{
                using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Magenta);IntPtr dc=g.GetHdc();bool ok;try{ok=PrintWindow(window,dc,2);}finally{g.ReleaseHdc(dc);}if(!ok)throw new InvalidOperationException("此窗口不支持后台捕获。");if(cursor)ScreenCapture.DrawCursor(g,new Point(r.Left,r.Top));}
                Validate(target);if(!redrawBoundWindow && !DesktopGuard.CanCapture(true))throw new InvalidOperationException("桌面已切换，丢弃本帧。");
                // Reject a completely unpainted or black result (common with unsupported GPU windows).
                bool allBlack=true,unpainted=true;for(int y=1;y<12;y++)for(int x=1;x<12;x++){Color c=bitmap.GetPixel(width*x/12,height*y/12);allBlack&=c.R==0&&c.G==0&&c.B==0;unpainted&=c.R==255&&c.G==0&&c.B==255;}
                if(allBlack||unpainted)throw new InvalidOperationException("窗口返回空白画面；保留最后画面，请恢复窗口或使用整屏。");return bitmap;
            }catch{bitmap.Dispose();throw;}
        }
    }
    // PrintWindow can block inside another application. A separate owned process bounds that wait.
    public sealed class WindowCaptureClient:IDisposable {
        sealed class WindowNotReady : InvalidOperationException {
            public WindowNotReady(string message):base(message){}
        }
        readonly object gate=new object();Process child;bool disposed;DateTime retryAfter;
        readonly string executable;
        public WindowCaptureClient():this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BooxWindowCapture.exe")){}
        internal WindowCaptureClient(string executablePath){executable=executablePath;}
        public async Task<Bitmap> CaptureAsync(WindowTarget target,bool cursor,bool locked=false){
            WindowSource.Validate(target);Process current;
            lock(gate){if(disposed)throw new ObjectDisposedException("WindowCaptureClient");if(DateTime.UtcNow<retryAfter)throw new InvalidOperationException("窗口捕获暂缓重试，保留最后画面。");
                if(child==null||child.HasExited){if(child!=null)child.Dispose();using(var parent=Process.GetCurrentProcess()){
                    var start=new ProcessStartInfo(executable,parent.Id+" "+parent.StartTime.ToUniversalTime().Ticks){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true};child=Process.Start(start);
                }}current=child;
            }
            var read=Task.Run(delegate{
                current.StandardInput.WriteLine(target.Handle+" "+target.ProcessId+" "+target.Started+" "+(cursor&&!locked?1:0)+" "+(locked?1:0));current.StandardInput.Flush();
                var reader=new BinaryReader(current.StandardOutput.BaseStream);int length=reader.ReadInt32();if(length<0)throw new WindowNotReady(reader.ReadString());if(length<1||length>64000000)throw new IOException("Invalid capture size");byte[] bytes=reader.ReadBytes(length);if(bytes.Length!=length)throw new EndOfStreamException();return bytes;
            });
            try{
                if(await Task.WhenAny(read,Task.Delay(1800))!=read)throw new TimeoutException("窗口响应超时，保留最后画面；稍后自动重试。");
                byte[] bytes=await read;using(var stream=new MemoryStream(bytes))using(var image=Image.FromStream(stream))return new Bitmap(image);
            }catch(WindowNotReady){
                // A complete error reply leaves the helper and its pre-lock binding usable.
                retryAfter=DateTime.UtcNow.AddSeconds(1);
                throw;
            }catch{
                lock(gate){if(child==current){StopChild();retryAfter=DateTime.UtcNow.AddSeconds(3);}}
                ObserveFailure(read);throw;
            }
        }
        static void ObserveFailure(Task task){task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
        void StopChild(){if(child==null)return;try{if(!child.HasExited)child.Kill();}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}child.Dispose();child=null;}
        public void Dispose(){lock(gate){disposed=true;StopChild();}}
    }
    public static class ScreenCapture {
        [StructLayout(LayoutKind.Sequential)] struct CursorInfo {public int Size,Flags;public IntPtr Cursor;public Point Position;}
        [StructLayout(LayoutKind.Sequential)] struct IconInfo {public bool IsIcon;public int HotspotX,HotspotY;public IntPtr Mask,Color;}
        [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CursorInfo info);
        [DllImport("user32.dll")] static extern IntPtr CopyIcon(IntPtr icon);
        [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon,out IconInfo info);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr dc,int x,int y,IntPtr icon,int width,int height,int step,IntPtr brush,int flags);
        // Check the desktop pixels before overlaying the cursor or adjusting contrast.
        internal static bool IsSolidBlack(Bitmap image) {
            if(image.GetPixel(image.Width/2,image.Height/2).ToArgb()!=Color.Black.ToArgb())return false;
            var data=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
            try {
                byte[] row=new byte[image.Width*3];
                for(int y=0;y<image.Height;y++){
                    Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),row,0,row.Length);
                    foreach(byte channel in row)if(channel!=0)return false;
                }
                return true;
            }finally{image.UnlockBits(data);}
        }
        public static Bitmap Capture(Rectangle region,bool showCursor) {
            var image=new Bitmap(region.Width,region.Height,PixelFormat.Format24bppRgb);
            try {
                using(var g=Graphics.FromImage(image)) {
                    g.CopyFromScreen(region.Location,Point.Empty,region.Size);
                }
                if(IsSolidBlack(image)){image.Dispose();return null;}
                if(showCursor)using(var g=Graphics.FromImage(image))DrawCursor(g,region.Location);
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
