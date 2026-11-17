using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;
using System.Windows.Automation;

namespace BooxImages {
    // Only targets the explicitly armed foreground window. Never changes focus or cursor.
    public sealed class AutoScroller:IDisposable {
        [StructLayout(LayoutKind.Sequential)] struct GuiThreadInfo {public int Size,Flags;public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret;public Rectangle CaretRect;}
        [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}
        delegate void WinEventDelegate(IntPtr hook,uint evt,IntPtr hwnd,int objectId,int childId,uint thread,uint time);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder name,int count);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
        [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread,ref GuiThreadInfo info);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out IntPtr result);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEventDelegate callback,uint process,uint thread,uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        IntPtr target,hook;uint processId;bool focusChanged,browserTarget;
        readonly WinEventDelegate callback;
        public string Diagnostic {get;private set;}
        public AutoScroller(){callback=OnForegroundChanged;}
        public bool Armed {get{return target!=IntPtr.Zero&&!focusChanged;}}
        public bool TargetIsForeground {get{uint pid;GetWindowThreadProcessId(target,out pid);return TargetIsCurrent(target,processId,GetForegroundWindow(),pid,focusChanged);}}
        void OnForegroundChanged(IntPtr eventHook,uint evt,IntPtr hwnd,int objectId,int childId,uint thread,uint time){if(target!=IntPtr.Zero&&hwnd!=target)focusChanged=true;}
        public static IntPtr CurrentWindow {get{return GetForegroundWindow();}}
        public bool Arm(out string reason){return Arm(IntPtr.Zero,out reason);}
        public bool Arm(IntPtr expectedWindow,out string reason){
            Stop();IntPtr foreground=GetForegroundWindow();uint process;GetWindowThreadProcessId(foreground,out process);
            if(expectedWindow!=IntPtr.Zero&&foreground!=expectedWindow){reason="\u7b49\u5f85\u8fde\u63a5\u65f6\u7a97\u53e3\u5df2\u6539\u53d8\uff0c\u8bf7\u56de\u5230\u9605\u8bfb\u6b63\u6587\u6309 Ctrl+Alt+R\u3002";return false;}
            var className=new StringBuilder(128);GetClassName(foreground,className,className.Capacity);string windowClass=className.ToString();
            if(foreground==IntPtr.Zero||foreground==GetShellWindow()||!IsWindowVisible(foreground)||process==(uint)Process.GetCurrentProcess().Id||windowClass=="Shell_TrayWnd"||windowClass=="Shell_SecondaryTrayWnd"||windowClass=="Progman"||windowClass=="WorkerW"){reason="\u8bf7\u5148\u6253\u5f00\u8981\u9605\u8bfb\u7684\u7f51\u9875/\u6587\u6863\u7a97\u53e3\uff0c\u518d\u5f00\u59cb\u81ea\u52a8\u3002";return false;}
            target=foreground;processId=process;focusChanged=false;Diagnostic="target="+windowClass;
            browserTarget=windowClass.StartsWith("Chrome_WidgetWin_",StringComparison.Ordinal);
            if(!BrowserFocusReady(out reason)){Stop();return false;}
            hook=SetWinEventHook(3,3,IntPtr.Zero,callback,0,0,0);
            if(hook==IntPtr.Zero){Stop();reason="\u65e0\u6cd5\u76d1\u6d4b\u524d\u53f0\u7a97\u53e3\uff0c\u81ea\u52a8\u7ffb\u9875\u672a\u542f\u52a8\u3002";return false;}
            reason="";return true;
        }
        public static bool TargetIsCurrent(IntPtr target,uint expectedProcess,IntPtr foreground,uint currentProcess,bool switched){return target!=IntPtr.Zero&&!switched&&target==foreground&&expectedProcess==currentProcess;}
        public static bool EditableControl(int controlType){return controlType==ControlType.Edit.Id||controlType==ControlType.ComboBox.Id;}
        bool BrowserFocusReady(out string reason){
            reason="";if(!browserTarget)return true;
            try {var focused=AutomationElement.FocusedElement;if(focused==null)throw new InvalidOperationException();
                if(EditableControl(focused.Current.ControlType.Id)){reason="\u7126\u70b9\u5728\u5730\u5740\u680f/\u7f16\u8f91\u6846\uff0c\u8bf7\u70b9\u51fb\u7f51\u9875\u6b63\u6587\u540e\u6309 Ctrl+Alt+R\u3002";return false;}
                return true;
            }catch(Exception){reason="\u6682\u65f6\u65e0\u6cd5\u786e\u8ba4\u7f51\u9875\u7126\u70b9\uff0c\u8bf7\u70b9\u51fb\u6b63\u6587\u540e\u91cd\u8bd5\u3002";return false;}
        }
        public static int WheelDelta(int lines,int systemLines){if(lines<1||lines>100)throw new ArgumentOutOfRangeException("lines");if(systemLines<=0)throw new InvalidOperationException("System wheel must be configured to scroll lines");return -Math.Max(1,(int)Math.Round(lines*120.0/systemLines));}
        public bool Step(bool page,int lines,out string reason){
            uint process;uint thread=GetWindowThreadProcessId(target,out process);
            if(!IsWindow(target)||!TargetIsCurrent(target,processId,GetForegroundWindow(),process,focusChanged)) {Stop();reason="\u5df2\u5207\u6362\u7a97\u53e3\uff0c\u81ea\u52a8\u7ffb\u9875\u5df2\u505c\u6b62\uff1b\u70b9\u7ee7\u7eed\u91cd\u65b0\u9009\u5b9a\u76ee\u6807\u3002";return false;}
            if((GetAsyncKeyState(0x10)&0x8000)!=0||(GetAsyncKeyState(0x11)&0x8000)!=0||(GetAsyncKeyState(0x12)&0x8000)!=0){Stop();reason="\u68c0\u6d4b\u5230 Shift/Ctrl/Alt\uff0c\u81ea\u52a8\u7ffb\u9875\u5df2\u505c\u6b62\u3002";return false;}
            var info=new GuiThreadInfo {Size=Marshal.SizeOf(typeof(GuiThreadInfo))};
            if(!GetGUIThreadInfo(thread,ref info)||info.Focus==IntPtr.Zero||GetAncestor(info.Focus,2)!=target){Stop();reason="\u65e0\u6cd5\u786e\u5b9a\u9605\u8bfb\u7a97\u53e3\u7684\u7126\u70b9\uff0c\u81ea\u52a8\u7ffb\u9875\u5df2\u505c\u6b62\u3002";return false;}
            var focusClass=new StringBuilder(128);GetClassName(info.Focus,focusClass,focusClass.Capacity);Diagnostic="focus="+focusClass+", mode="+(page?"page":"lines");
            if(!BrowserFocusReady(out reason)){Stop();return false;}
            if(focusChanged||GetForegroundWindow()!=target){Stop();reason="\u7a97\u53e3\u5df2\u5207\u6362\uff0c\u81ea\u52a8\u7ffb\u9875\u5df2\u505c\u6b62\u3002";return false;}
            IntPtr result;bool ok;
            if(page) {
                // Targeted messages cannot spill into another app if foreground changes mid-call.
                ok=SendMessageTimeout(info.Focus,0x100,new IntPtr(0x22),new IntPtr(0x01510001),2,150,out result)!=IntPtr.Zero;
                if(ok)ok=SendMessageTimeout(info.Focus,0x101,new IntPtr(0x22),new IntPtr(unchecked((int)0xC1510001)),2,150,out result)!=IntPtr.Zero;
            }else {
                int systemLines=SystemInformation.MouseWheelScrollLines;
                if(systemLines<=0){Stop();reason="\u7cfb\u7edf\u6eda\u8f6e\u8bbe\u7f6e\u4e3a\u6574\u9875/\u7981\u7528\uff0c\u8bf7\u9009\u7ffb\u9875\u6a21\u5f0f\u6216\u5c06\u7cfb\u7edf\u6eda\u8f6e\u6539\u4e3a\u6309\u884c\u6eda\u52a8\u3002";return false;}
                NativeRect rect;if(!GetWindowRect(info.Focus,out rect)){reason="\u65e0\u6cd5\u8bfb\u53d6\u76ee\u6807\u7a97\u53e3";Stop();return false;}
                int x=(rect.Left+rect.Right)/2,y=(rect.Top+rect.Bottom)/2,point=(x&65535)|((y&65535)<<16);
                int delta=WheelDelta(lines,systemLines);
                ok=SendMessageTimeout(info.Focus,0x20A,new IntPtr(unchecked(delta<<16)),new IntPtr(point),2,150,out result)!=IntPtr.Zero;
            }
            if(!ok){Stop();reason="\u76ee\u6807\u7a97\u53e3\u672a\u63a5\u53d7\u6eda\u52a8\uff0c\u81ea\u52a8\u7ffb\u9875\u5df2\u505c\u6b62\u3002";return false;}
            reason="";return true;
        }
        public void Stop(){target=IntPtr.Zero;processId=0;focusChanged=false;if(hook!=IntPtr.Zero){UnhookWinEvent(hook);hook=IntPtr.Zero;}}
        public void Dispose(){Stop();}
    }
}
