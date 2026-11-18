using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;

[assembly:System.Reflection.AssemblyVersion("0.5.8.0")]
[assembly:System.Reflection.AssemblyFileVersion("0.5.8.0")]

namespace BooxImages {
    static class Program {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
        internal static readonly string InstanceName="Local\\BooxMaxImageSender."+DesktopGuard.InstanceScope();
        internal static readonly string ActivationName=InstanceName+".Activate";
        internal static bool ActivateExisting(string name) {
            for(int attempt=0;attempt<20;attempt++)try {
                using(var request=System.Threading.EventWaitHandle.OpenExisting(name))
                using(var ack=System.Threading.EventWaitHandle.OpenExisting(name+".Ack")) {
                    string executable=System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                    foreach(var other in System.Diagnostics.Process.GetProcessesByName(System.Diagnostics.Process.GetCurrentProcess().ProcessName))using(other)try{if(other.Id!=System.Diagnostics.Process.GetCurrentProcess().Id&&other.MainModule.FileName==executable)AllowSetForegroundWindow(other.Id);}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
                    ack.Reset();request.Set();return ack.WaitOne(4000);
                }
            }catch(System.Threading.WaitHandleCannotBeOpenedException){System.Threading.Thread.Sleep(100);}
            return false;
        }
        [STAThread] static void Main(string[] args) {
            SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            bool created;using(var mutex=new System.Threading.Mutex(true,InstanceName,out created)) {
                bool owns=created;
                if(!created){
                    if(ActivateExisting(ActivationName))return;
                    // The previous instance may have been exiting while this launch requested activation.
                    try{owns=mutex.WaitOne(500);}catch(System.Threading.AbandonedMutexException){owns=true;}
                    if(!owns){MessageBox.Show("\u539f\u53d1\u9001\u5668\u672a\u54cd\u5e94\u6253\u5f00\u8bf7\u6c42\u3002\u8bf7\u5728\u4efb\u52a1\u7ba1\u7406\u5668\u7ed3\u675f BooxImages \u540e\u91cd\u65b0\u6253\u5f00\u3002","BOOX Max Images");return;}
                }
                try {using(var form=new SenderForm())using(var activation=new WindowActivation(form,ActivationName)){form.EnforceProcessExit=true;Application.Run(form);form.LogLifecycle("message loop ended");}}catch(Exception e){MessageBox.Show(e.Message,"BOOX Max Images");}
                finally{if(owns)mutex.ReleaseMutex();}
            }
            // Native/COM libraries may retain foreground threads after the WinForms loop ends.
            Environment.Exit(0);
        }
    }
    // Named events work even when the main window and notification icon are hidden.
    sealed class WindowActivation:IDisposable {
        readonly System.Threading.EventWaitHandle request,ack;
        readonly System.Threading.RegisteredWaitHandle registration;
        volatile bool disposed;
        internal WindowActivation(SenderForm form,string name) {
            request=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.AutoReset,name);
            ack=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.AutoReset,name+".Ack");
            IntPtr handle=form.Handle;
            registration=System.Threading.ThreadPool.RegisterWaitForSingleObject(request,delegate(object state,bool timedOut){
                if(disposed)return;
                try{form.BeginInvoke((Action)delegate{if(disposed||form.IsDisposed)return;if(form.RecoverWindow()&&!disposed)ack.Set();});}catch(InvalidOperationException){}
            },null,System.Threading.Timeout.Infinite,false);
        }
        public void Dispose(){disposed=true;registration.Unregister(null);request.Dispose();ack.Dispose();}
    }
    sealed class Frame {
        internal byte[] Bytes;internal string Hash,Session;internal long Sequence;internal int Generation,CaptureKind;
    }
    public sealed class SenderForm:Form {
        readonly HttpClient http;
        readonly JavaScriptSerializer json=new JavaScriptSerializer();
        readonly Timer captureTimer=new Timer(),pollTimer=new Timer(),scrollTimer=new Timer();
        readonly ComboBox interval=new ComboBox(),contrast=new ComboBox(),direction=new ComboBox(),enhancement=new ComboBox(),scrollMode=new ComboBox();
        readonly CheckBox gray=new CheckBox(),showCursor=new CheckBox(),autoScroll=new CheckBox();
        readonly NumericUpDown scrollSeconds=new NumericUpDown(),scrollLines=new NumericUpDown(),fullRefreshEvery=new NumericUpDown();
        readonly AutoScroller scroller=new AutoScroller();
        DateTime nextScroll;
        bool ready,syncingRefresh,scrollBusy,closeReady;
        internal bool EnforceProcessExit;
        System.Threading.Timer exitDeadline;
        readonly WindowCaptureClient windowCapture=new WindowCaptureClient();
        WindowTarget windowTarget;bool captureWindow;
        string captureHold="";
        string scrollStatus="",transferStatus="";long scrollSteps;
        readonly string settingsPath;
        readonly Icon appIcon,trayIcon;
        readonly Label status=new Label(),regionLabel=new Label();
        readonly Label networkLabel=new Label();
        readonly Button pairButton=new Button();
        bool networkPolling,pairPending;DateTime nextNetwork;
        readonly PictureBox preview=new PictureBox();
        readonly NotifyIcon tray=new NotifyIcon();
        Rectangle region;bool hasRegion,automatic,busy,polling,controlling,closing,wasAutomatic,fullScreen=true,lastWasCapture;
        Task sendingTask=Task.FromResult(0),captureTask=Task.FromResult(0);
        bool capturing;
        byte[] previewBytes;
        string session="",ackHash="",baseUrl;long sequence;int generation;Frame pending,last;DateTime retryAfter=DateTime.MinValue;
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
        static readonly int taskbarCreated=RegisterWindowMessage("TaskbarCreated");
        public SenderForm():this(null){}
        internal SenderForm(string root):this(root,null){}
        internal SenderForm(string root,HttpMessageHandler handler) {
            http=new HttpClient(handler??new HttpClientHandler {UseProxy=false});
            Text="BOOX Max \u56fe\u7247\u53d1\u9001";ClientSize=new Size(940,740);MinimumSize=new Size(940,700);Font=new Font("Microsoft YaHei",10);StartPosition=FormStartPosition.CenterScreen;
            if(root==null)root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));
            settingsPath=Path.Combine(root,".local","image-settings.json");var saved=ImageSettings.Load(settingsPath);
            LogLifecycle("starting");
            var assembly=System.Reflection.Assembly.GetExecutingAssembly();
            using(var stream=assembly.GetManifestResourceStream("BooxImages.AppIcon"))appIcon=new Icon(stream);
            using(var stream=assembly.GetManifestResourceStream("BooxImages.TrayIcon"))trayIcon=new Icon(stream,32,32);
            Icon=appIcon;
            var config=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,".local","image-client.json")));
            baseUrl=(string)config["url"];http.Timeout=TimeSpan.FromSeconds(22);http.DefaultRequestHeaders.Add("X-Boox-Client",(string)config["key"]);
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=11,Padding=new Padding(15)};
            foreach(int height in new int[]{42,44,44,44,44,44,30,38})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,66));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,62));Controls.Add(layout);
            var heading=new Label {Text="\u628a\u7535\u8111\u753b\u9762\u6216\u56fe\u7247\u53d1\u9001\u5230 BOOX",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,15,FontStyle.Bold)};layout.Controls.Add(heading);
            var actions=new FlowLayoutPanel {Dock=DockStyle.Fill};layout.Controls.Add(actions);
            Add(actions,"\u9009\u62e9\u56fe\u7247",async delegate {await SendFile();});
            Add(actions,"\u6574\u5c4f",async delegate {if(controlling||closing||pairPending)return;await Pause();captureWindow=false;fullScreen=true;UseFullScreen();SaveSettings();});
            Add(actions,"\u6846\u9009\u533a\u57df",async delegate {await SelectRegion();});
            Add(actions,"选择窗口",async delegate {await SelectWindow();});
            Add(actions,"\u53d1\u9001\u4e00\u6b21",async delegate {await Begin(false,false);});
            Add(actions,"\u5f00\u59cb\u81ea\u52a8",async delegate {await Begin(true,false);});
            Add(actions,"BOOX \u56de\u684c\u9762",async delegate {await ReturnHome();});
            direction.DropDownStyle=ComboBoxStyle.DropDownList;direction.Width=178;direction.Items.AddRange(new object[]{"\u7ad6\u5c4f 1200\u00d71600","\u6a2a\u5c4f 1600\u00d71200"});direction.SelectedIndex=0;direction.Margin=new Padding(10,5,0,0);
            direction.SelectedIndex=saved.Direction;
            direction.SelectedIndexChanged+=async delegate {SaveSettings();await ChangeOrientation();};
            var options=new FlowLayoutPanel {Dock=DockStyle.Fill};layout.Controls.Add(options);
            options.Controls.Add(new Label {Text="\u68c0\u67e5\u95f4\u9694(\u79d2)",AutoSize=true,Margin=new Padding(3,8,4,0)});
            interval.DropDownStyle=ComboBoxStyle.DropDownList;interval.Width=72;interval.Items.AddRange(new object[]{"0.1","0.2","0.5","1","2"});interval.SelectedItem=saved.IntervalSeconds.ToString("0.0#",System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');options.Controls.Add(interval);
            interval.SelectedIndexChanged+=delegate {captureTimer.Interval=SelectedIntervalMs;SaveSettings();};
            options.Controls.Add(direction);Add(options,"\u6682\u505c",async delegate {await Pause(true);});Add(options,"\u7ee7\u7eed",async delegate {await Resume();});
            var displayOptions=new FlowLayoutPanel {Dock=DockStyle.Fill};layout.Controls.Add(displayOptions);
            displayOptions.Controls.Add(new Label {Text="\u663e\u793a\u589e\u5f3a",AutoSize=true,Margin=new Padding(3,8,4,0)});
            enhancement.DropDownStyle=ComboBoxStyle.DropDownList;enhancement.Width=180;enhancement.Items.AddRange(new object[]{"\u539f\u56fe","\u6587\u5b57\u6e05\u6670","\u6587\u5b57\u52a0\u6df1","\u9ed1\u767d\u9ad8\u5bf9\u6bd4"});enhancement.SelectedIndex=1;displayOptions.Controls.Add(enhancement);
            gray.Text="\u7070\u5ea6";gray.Checked=true;gray.AutoSize=true;gray.Margin=new Padding(14,7,4,0);displayOptions.Controls.Add(gray);
            displayOptions.Controls.Add(new Label {Text="\u5bf9\u6bd4\u5ea6",AutoSize=true,Margin=new Padding(12,8,4,0)});
            contrast.DropDownStyle=ComboBoxStyle.DropDownList;contrast.Width=72;contrast.Items.AddRange(new object[]{"1.0","1.25","1.5"});contrast.SelectedIndex=0;displayOptions.Controls.Add(contrast);
            showCursor.Text="\u663e\u793a\u5149\u6807";showCursor.AutoSize=true;showCursor.Margin=new Padding(14,7,4,0);displayOptions.Controls.Add(showCursor);
            enhancement.SelectedIndex=saved.Enhancement;gray.Checked=saved.Grayscale;contrast.SelectedIndex=saved.Contrast;showCursor.Checked=saved.ShowCursor;
            enhancement.SelectedIndexChanged+=async delegate {SaveSettings();await ChangeDisplaySettings();};gray.CheckedChanged+=async delegate {SaveSettings();await ChangeDisplaySettings();};contrast.SelectedIndexChanged+=async delegate {SaveSettings();await ChangeDisplaySettings();};showCursor.CheckedChanged+=async delegate {SaveSettings();await ChangeDisplaySettings();};
            var readingOptions=new FlowLayoutPanel {Dock=DockStyle.Fill};layout.Controls.Add(readingOptions);
            autoScroll.Text="\u81ea\u52a8\u7ffb\u9875";autoScroll.AutoSize=true;autoScroll.Margin=new Padding(3,7,8,0);autoScroll.Checked=saved.AutoScroll;readingOptions.Controls.Add(autoScroll);
            readingOptions.Controls.Add(new Label{Text="\u6bcf",AutoSize=true,Margin=new Padding(3,8,0,0)});
            scrollSeconds.Minimum=.1m;scrollSeconds.Maximum=3600;scrollSeconds.DecimalPlaces=1;scrollSeconds.Increment=.1m;scrollSeconds.Width=72;scrollSeconds.Value=(decimal)saved.ScrollSeconds;readingOptions.Controls.Add(scrollSeconds);
            readingOptions.Controls.Add(new Label{Text="\u79d2",AutoSize=true,Margin=new Padding(3,8,4,0)});
            scrollMode.DropDownStyle=ComboBoxStyle.DropDownList;scrollMode.Width=110;scrollMode.Items.AddRange(new object[]{"\u6eda\u52a8\u6307\u5b9a\u884c","\u5411\u4e0b\u7ffb\u4e00\u9875"});scrollMode.SelectedIndex=saved.ScrollMode;readingOptions.Controls.Add(scrollMode);
            scrollLines.Minimum=1;scrollLines.Maximum=100;scrollLines.Width=62;scrollLines.Value=saved.ScrollLines;scrollLines.Enabled=saved.ScrollMode==0;readingOptions.Controls.Add(scrollLines);
            readingOptions.Controls.Add(new Label{Text="\u884c\uff08\u6309\u7cfb\u7edf\u6eda\u8f6e\u6362\u7b97\uff09",AutoSize=true,Margin=new Padding(3,8,0,0)});
            autoScroll.CheckedChanged+=delegate{SaveSettings();StopScrolling();SetScrollStatus(autoScroll.Checked?"\u5df2\u542f\u7528\uff1b\u5728\u9605\u8bfb\u6b63\u6587\u6309 Ctrl+Alt+R \u542f\u52a8\u3002":"\u672a\u542f\u7528\u3002");};
            scrollSeconds.ValueChanged+=delegate{SaveSettings();nextScroll=DateTime.UtcNow.AddSeconds((double)scrollSeconds.Value);};
            scrollLines.ValueChanged+=delegate{SaveSettings();};scrollMode.SelectedIndexChanged+=delegate{scrollLines.Enabled=scrollMode.SelectedIndex==0;SaveSettings();};
            var refreshOptions=new FlowLayoutPanel {Dock=DockStyle.Fill};layout.Controls.Add(refreshOptions);
            refreshOptions.Controls.Add(new Label{Text="\u6bcf\u9001\u8fbe",AutoSize=true,Margin=new Padding(3,8,4,0)});
            fullRefreshEvery.Minimum=0;fullRefreshEvery.Maximum=1000;fullRefreshEvery.Width=70;fullRefreshEvery.Value=saved.FullRefreshEvery;refreshOptions.Controls.Add(fullRefreshEvery);
            refreshOptions.Controls.Add(new Label{Text="\u6b21\u753b\u9762\u5168\u5c40\u5237\u65b0\uff080=\u5173\u95ed\uff09",AutoSize=true,Margin=new Padding(3,8,4,0)});
            Add(refreshOptions,"\u7acb\u5373\u5168\u5237",async delegate{await ManualRefresh();});
            refreshOptions.Controls.Add(new Label{Text="\u5f00\u59cb\u540e 3 \u79d2\u5185\u70b9\u51fb\u9605\u8bfb\u6b63\u6587\uff1b\u9501\u5b9a\u540e\u5207\u6362\u7a97\u53e3\u505c\u6b62",AutoSize=true,ForeColor=Color.DimGray,Margin=new Padding(12,8,0,0)});
            fullRefreshEvery.ValueChanged+=async delegate{SaveSettings();await SyncRefreshSettings();};
            layout.Controls.Add(new Label {Text="Ctrl+Alt+B \u622a\u56fe  |  Ctrl+Alt+P \u6682\u505c/\u7ee7\u7eed  |  Ctrl+Alt+R \u4ece\u5f53\u524d\u7f51\u9875\u5f00\u59cb/\u505c\u6b62\u7ffb\u9875",Dock=DockStyle.Fill,ForeColor=Color.DimGray});
            regionLabel.Dock=DockStyle.Fill;layout.Controls.Add(regionLabel);
            Rectangle restoredRegion;if(saved.TryRestoreRegion(SystemInformation.VirtualScreen,out restoredRegion)){region=restoredRegion;hasRegion=true;fullScreen=false;UpdateRegionLabel();}else{fullScreen=true;UseFullScreen();}
            if(saved.CaptureWindow){captureWindow=true;fullScreen=false;windowTarget=new WindowTarget{Handle=saved.WindowHandle,ProcessId=saved.WindowProcess,Started=saved.WindowStarted};regionLabel.Text="指定窗口（已记住）；原窗口关闭后请重新选择，不会自动改为整屏。";}
            preview.Dock=DockStyle.Fill;preview.SizeMode=PictureBoxSizeMode.Zoom;preview.BackColor=Color.FromArgb(240,240,240);layout.Controls.Add(preview);
            status.Dock=DockStyle.Fill;status.Text=captureWindow?"就绪。指定窗口可在普通屏保遮挡时继续同步；请勿最小化目标窗口。":"就绪。整屏/选区遇屏保保留最后画面；屏保期间持续更新请选择窗口。";layout.Controls.Add(status);
            var connection=new Panel{Dock=DockStyle.Fill};networkLabel.Dock=DockStyle.Fill;networkLabel.AutoEllipsis=true;networkLabel.Text="\u6b63\u5728\u68c0\u6d4b\u7535\u8111\u4e0e BOOX \u7684 Wi-Fi...";connection.Controls.Add(networkLabel);
            pairButton.Text="\u91cd\u65b0\u914d\u5bf9 (USB)";pairButton.Dock=DockStyle.Right;pairButton.Width=150;pairButton.Click+=async delegate{await RePair();};connection.Controls.Add(pairButton);layout.Controls.Add(connection);
            tray.Icon=trayIcon;tray.Text="BOOX Max \u56fe\u7247";tray.Visible=true;tray.DoubleClick+=delegate{ShowWindow();};
            var menu=new ContextMenuStrip();menu.Items.Add("\u6253\u5f00\u63a7\u5236\u7a97\u53e3",null,delegate{ShowWindow();});menu.Items.Add("\u53d1\u9001\u622a\u56fe",null,async delegate{await Begin(false,false);});menu.Items.Add("\u6682\u505c",null,async delegate{await Pause(true);});menu.Items.Add("\u7ee7\u7eed",null,async delegate{await Resume();});menu.Items.Add("\u9000\u51fa",null,delegate{RequestExit("tray click");});menu.Items.Add(new ToolStripSeparator());menu.Items.Add(new ToolStripMenuItem("Ctrl+Alt+B  \u53d1\u9001\u622a\u56fe"){Enabled=false});menu.Items.Add(new ToolStripMenuItem("Ctrl+Alt+P  \u6682\u505c / \u7ee7\u7eed"){Enabled=false});tray.ContextMenuStrip=menu;
            captureTimer.Interval=SelectedIntervalMs;captureTimer.Tick+=async delegate{await CaptureTick();};pollTimer.Interval=2000;pollTimer.Tick+=async delegate{await Poll();await RefreshNetwork();};pollTimer.Start();
            scrollTimer.Interval=100;scrollTimer.Tick+=async delegate{await ScrollTick();};ready=true;
            menu.Items.Add(new ToolStripMenuItem("Ctrl+Alt+R  \u5f53\u524d\u7f51\u9875\u7ffb\u9875"){Enabled=false});
            menu.Items.Insert(4,new ToolStripMenuItem("BOOX \u56de\u684c\u9762",null,async delegate{await ReturnHome();}));
            Shown+=delegate {bool a=RegisterHotKey(Handle,1,0x4003,0x42),b=RegisterHotKey(Handle,2,0x4003,0x50),c=RegisterHotKey(Handle,3,0x4003,0x52);if(!a||!b||!c)SetStatus("\u90e8\u5206\u5feb\u6377\u952e\u88ab\u5360\u7528\uff0c\u8bf7\u4f7f\u7528\u6309\u94ae\u3002");};
            Resize+=delegate{if(WindowState==FormWindowState.Minimized)Hide();};
            Shown+=async delegate{await RefreshNetwork();};
        }
        async Task RefreshNetwork(){
            if(closing||controlling||networkPolling||DateTime.UtcNow<nextNetwork)return;int observedGeneration=generation;networkPolling=true;nextNetwork=DateTime.UtcNow.AddSeconds(5);
            try{
                var data=await Request("/network");if(closing||controlling||observedGeneration!=generation)return;
                var computer=(Dictionary<string,object>)data["computer"];var boox=(Dictionary<string,object>)data["boox"];var pair=(Dictionary<string,object>)data["pairing"];
                var names=computer["ssids"] as System.Collections.IEnumerable;var ssids=new List<string>();if(names!=null)foreach(object name in names)ssids.Add(Convert.ToString(name));
                string pc=ssids.Count>0?String.Join(" / ",ssids):Convert.ToString(computer["status"])=="unavailable"?"\u65e0\u6cd5\u8bfb\u53d6\uff08\u7cfb\u7edf\u6743\u9650\uff09":"\u672a\u8fde\u63a5 Wi-Fi\uff08\u53ef\u4f7f\u7528\u6709\u7ebf\u7f51\u7edc\uff09";
                bool online=Convert.ToBoolean(boox["online"]);string remote=Convert.ToString(boox["ssid"]);
                if(remote.Length==0)remote=online?"\u540d\u79f0\u6682\u4e0d\u53ef\u7528":"\u672a\u83b7\u53d6";else if(!online)remote+="\uff08\u4e0a\u6b21\uff09";
                networkLabel.Text="\u7535\u8111 Wi-Fi: "+pc+"\nBOOX Wi-Fi: "+remote+"  |  "+(online?"\u5df2\u8fde\u63a5":"\u8fde\u63a5\u672a\u6210\u529f\uff0c\u6b63\u5728\u91cd\u8bd5");
                bool running=Convert.ToBoolean(pair["running"]);pairButton.Enabled=!running&&!pairPending;
                if(pairPending&&!running){pairPending=false;pairButton.Enabled=true;SetStatus(Convert.ToString(pair["status"])=="success"?"\u914d\u5bf9\u6210\u529f\uff0c\u70b9\u5f00\u59cb\u81ea\u52a8\u7ee7\u7eed\u53d1\u9001\u3002":"\u914d\u5bf9\u672a\u5b8c\u6210: "+Convert.ToString(pair["message"]));}
            }catch(Exception e){if(!closing)networkLabel.Text="\u8fde\u63a5\u72b6\u6001\u6682\u4e0d\u53ef\u7528: "+e.Message;}
            finally{networkPolling=false;}
        }
        async Task RePair(){
            if(controlling||closing||pairPending)return;controlling=true;pairButton.Enabled=false;
            automatic=false;captureTimer.Stop();StopScrolling();pending=null;generation++;
            try{
                SetStatus("\u8bf7\u4fdd\u6301 BOOX \u7684 USB \u8c03\u8bd5\u8fde\u63a5\uff0c\u6b63\u5728\u914d\u5bf9...\uff08\u4e0d\u4f1a\u4fee\u6539 Wi-Fi \u8bbe\u7f6e\uff09");
                await Task.WhenAny(sendingTask,Task.Delay(2000));
                await Request("/pair",new StringContent("{}",System.Text.Encoding.UTF8,"application/json"));
                pairPending=true;session="";last=null;ackHash="";nextNetwork=DateTime.MinValue;
            }catch(Exception e){SetStatus("\u914d\u5bf9\u5931\u8d25: "+e.Message);pairButton.Enabled=true;}
            finally{controlling=false;}
        }
        void Add(Control row,string text,EventHandler click){var b=new Button {Text=text,AutoSize=true,Height=34,Margin=new Padding(3)};b.Click+=click;row.Controls.Add(b);}
        internal void LogLifecycle(string stage){try{string folder=Path.Combine(Path.GetDirectoryName(settingsPath),"..","logs");Directory.CreateDirectory(folder);File.AppendAllText(Path.Combine(folder,"sender.log"),DateTime.Now.ToString("o")+" pid="+System.Diagnostics.Process.GetCurrentProcess().Id+" "+stage+Environment.NewLine);}catch(IOException){}catch(UnauthorizedAccessException){}}
        void ShowWindow(){if(scroller.Armed)SetScrollStatus("\u63a7\u5236\u7a97\u53e3\u5df2\u6253\u5f00\uff0c\u7ffb\u9875\u505c\u6b62\uff1b\u70b9\u7ee7\u7eed\u91cd\u65b0\u9009\u5b9a\u3002");StopScrolling();Show();WindowState=FormWindowState.Normal;UpdatePreview();Activate();}
        void RestoreTray(){if(closing)return;tray.Visible=false;tray.Icon=trayIcon;tray.Visible=true;}
        internal bool RecoverWindow(){if(closing)return false;RestoreTray();ShowWindow();if(!SystemInformation.VirtualScreen.IntersectsWith(Bounds)){Location=Screen.PrimaryScreen.WorkingArea.Location;}BringToFront();return Visible&&WindowState==FormWindowState.Normal;}
        void UpdatePreview(){if(previewBytes==null)return;using(var stream=new MemoryStream(previewBytes))using(var image=Image.FromStream(stream)){var old=preview.Image;preview.Image=new Bitmap(image);if(old!=null)old.Dispose();}previewBytes=null;}
        int SelectedIntervalMs {get{return (int)(double.Parse((string)interval.SelectedItem,System.Globalization.CultureInfo.InvariantCulture)*1000);}}
        void SaveSettings(){if(!ready)return;try{new ImageSettings {Direction=direction.SelectedIndex,Enhancement=enhancement.SelectedIndex,Grayscale=gray.Checked,Contrast=contrast.SelectedIndex,ShowCursor=showCursor.Checked,IntervalSeconds=SelectedIntervalMs/1000.0,FullScreen=fullScreen,CaptureWindow=captureWindow,WindowHandle=windowTarget==null?0:windowTarget.Handle,WindowProcess=windowTarget==null?0:windowTarget.ProcessId,WindowStarted=windowTarget==null?0:windowTarget.Started,RegionX=region.X,RegionY=region.Y,RegionWidth=region.Width,RegionHeight=region.Height,AutoScroll=autoScroll.Checked,ScrollSeconds=(double)scrollSeconds.Value,ScrollLines=(int)scrollLines.Value,ScrollMode=scrollMode.SelectedIndex,FullRefreshEvery=(int)fullRefreshEvery.Value}.Save(settingsPath);}catch(Exception e){SetStatus("\u8bbe\u7f6e\u4fdd\u5b58\u5931\u8d25: "+e.Message);}}
        void StopScrolling(){scrollTimer.Stop();scroller.Stop();}
        void ArmScrolling(){ArmScrolling(IntPtr.Zero);}
        void ArmScrolling(IntPtr expected){StopScrolling();if(!automatic||!autoScroll.Checked||closing)return;string reason;if(!scroller.Arm(expected,out reason)){SetScrollStatus(reason);tray.ShowBalloonTip(3000,"BOOX",reason,ToolTipIcon.Warning);return;}scrollSteps=0;SetScrollStatus("\u5df2\u9501\u5b9a\u9605\u8bfb\u7a97\u53e3\uff1b\u7b49\u5f85\u56fe\u7247\u56de\u6267\u540e\u7ffb\u9875\u3002");nextScroll=DateTime.UtcNow.AddSeconds((double)scrollSeconds.Value);scrollTimer.Start();}
        async Task ScrollTick(){
            if(scrollBusy)return;scrollBusy=true;int scrollGeneration=generation;string scrollSession=session;
            try {
            if(!automatic||controlling||closing){if(!automatic||closing)StopScrolling();return;}
            if(!DesktopGuard.CanCapture(false)){StopScrolling();SetScrollStatus("屏保或锁屏期间停止翻页；窗口图片同步独立运行。");return;}
            if(!scroller.Armed){StopScrolling();SetScrollStatus("\u5df2\u5207\u6362\u7a97\u53e3\uff0c\u81ea\u52a8\u7ffb\u9875\u5df2\u505c\u6b62\uff1b\u70b9\u7ee7\u7eed\u91cd\u65b0\u9009\u5b9a\u9605\u8bfb\u7a97\u53e3\u3002");return;}
            if(DateTime.UtcNow<nextScroll||DateTime.UtcNow<retryAfter||ackHash.Length==0)return;
            string reason;if(!scroller.Step(scrollMode.SelectedIndex==1,(int)scrollLines.Value,out reason)){StopScrolling();SetScrollStatus(reason);return;}
            scrollSteps++;SetScrollStatus("\u5df2\u53d1\u9001 "+scrollSteps+" \u6b21"+(scrollMode.SelectedIndex==1?"\u6574\u9875\u7ffb\u52a8":"\u6309\u884c\u6eda\u52a8")+"\uff1b\u5207\u6362\u7a97\u53e3\u540e\u505c\u6b62\u3002",scrollSteps==1||scrollSteps%50==0);
            nextScroll=DateTime.UtcNow.AddSeconds((double)scrollSeconds.Value);
            await Task.Delay(70);await captureTask;if(automatic&&!controlling&&!closing&&scroller.Armed&&scrollGeneration==generation&&scrollSession==session)await CaptureTick();
            }finally{scrollBusy=false;}
        }
        async Task SyncRefreshSettings(){
            if(!ready||controlling||closing||session.Length==0||syncingRefresh)return;
            syncingRefresh=true;
            try{int sent;do{sent=(int)fullRefreshEvery.Value;var reply=await Control("settings");if(sent>0&&reply.ContainsKey("fullRefreshSupported")&&!Convert.ToBoolean(reply["fullRefreshSupported"]))SetStatus("BOOX \u5f53\u524d\u56fa\u4ef6\u672a\u63d0\u4f9b\u53ef\u7528\u5168\u5237\u63a5\u53e3\u3002");}while(!closing&&session.Length>0&&sent!=(int)fullRefreshEvery.Value);}
            catch(Exception e){SetStatus(e.Message);}finally{syncingRefresh=false;}
        }
        async Task ManualRefresh(){
            if(controlling||closing||pairPending)return;if(session.Length==0){SetStatus("\u8bf7\u5148\u53d1\u9001\u4e00\u5f20\u56fe\u7247\uff0c\u518d\u5168\u5c40\u5237\u65b0\u3002");return;}
            controlling=true;StopScrolling();try{var reply=await Control("refresh");SetStatus(reply.ContainsKey("fullRefreshSupported")&&!Convert.ToBoolean(reply["fullRefreshSupported"])?"BOOX \u5f53\u524d\u4e0d\u652f\u6301\u5168\u5c40\u5237\u65b0":"\u5df2\u8bf7\u6c42 BOOX \u5168\u5c40\u5237\u65b0\u3002");}catch(Exception e){SetStatus(e.Message);}finally{controlling=false;}
        }
        ImageOptions SnapshotOptions(){return new ImageOptions {Landscape=direction.SelectedIndex==1,Grayscale=gray.Checked,Enhancement=enhancement.SelectedIndex,Contrast=float.Parse((string)contrast.SelectedItem,System.Globalization.CultureInfo.InvariantCulture),ShowCursor=showCursor.Checked};}
        void SetStatus(string text){if(!closing){transferStatus=text;status.Text=text+(scrollStatus.Length==0?"":"\n\u81ea\u52a8\u7ffb\u9875: "+scrollStatus);}}
        void SetScrollStatus(string text,bool record=true){scrollStatus=text;SetStatus(transferStatus);if(!record)return;try{string folder=Path.Combine(Path.GetDirectoryName(settingsPath),"..","logs");Directory.CreateDirectory(folder);File.AppendAllText(Path.Combine(folder,"scroll.log"),DateTime.Now.ToString("o")+" "+text+" "+scroller.Diagnostic+Environment.NewLine);}catch(IOException){}catch(UnauthorizedAccessException){}}
        protected override void WndProc(ref Message m){
            if(taskbarCreated!=0&&m.Msg==taskbarCreated){base.WndProc(ref m);if(ready&&!closing)BeginInvoke((Action)RestoreTray);return;}
            if(m.Msg==0x312){HandleHotkey(m.WParam.ToInt32());return;}base.WndProc(ref m);
        }
        async void HandleHotkey(int id){if(id==1)await Begin(false,false);else if(id==2){if(automatic)await Pause(true);else await Resume();}else if(id==3)await ReadHere();}
        void StopReadingOnly(){StopScrolling();SetScrollStatus("\u5df2\u6682\u505c\u7ffb\u9875\uff0c\u56fe\u7247\u7ee7\u7eed\u53d1\u9001\uff1bCtrl+Alt+R \u6062\u590d\u7ffb\u9875\u3002");}
        async Task ReadHere(){
            if(controlling||closing||pairPending)return;
            if(automatic&&scroller.TargetIsForeground){StopReadingOnly();return;}
            IntPtr expected=AutoScroller.CurrentWindow;string reason;
            if(!scroller.Arm(expected,out reason)){SetScrollStatus(reason);tray.ShowBalloonTip(3000,"BOOX",reason,ToolTipIcon.Warning);return;}
            scroller.Stop();autoScroll.Checked=true;SaveSettings();
            if(automatic&&session.Length>0){
                // Wait for hotkey modifiers to be released without stopping capture or changing its session.
                controlling=true;try{await Task.Delay(300);if(!closing&&automatic)ArmScrolling(expected);}finally{controlling=false;}
                return;
            }
            await BeginCore(true,true,expected);
        }
        async Task<Dictionary<string,object>> Request(string endpoint,HttpContent content=null){
            using(var response=content==null?await http.GetAsync(baseUrl+endpoint):await http.PostAsync(baseUrl+endpoint,content)){
                string text=await response.Content.ReadAsStringAsync();var result=json.Deserialize<Dictionary<string,object>>(text);
                if(!response.IsSuccessStatusCode)throw new InvalidOperationException(result.ContainsKey("error")?Convert.ToString(result["error"]):text);return result;
            }
        }
        string Orientation {get{return direction.SelectedIndex==1?"landscape":"portrait";}}
        void UseFullScreen(){
            region=Screen.PrimaryScreen.Bounds;hasRegion=true;
            regionLabel.Text=String.Format("整屏（主显示器）: {0}×{1}  |  屏保期间保留最后桌面；持续更新请选择窗口",region.Width,region.Height);
        }
        void UpdateRegionLabel(){regionLabel.Text=String.Format("\u6846\u9009\u533a\u57df: {0} \u00d7 {1}  ({2}, {3})",region.Width,region.Height,region.X,region.Y);}
        async Task ChangeDisplaySettings(){
            if(controlling||closing||pairPending)return;controlling=true;bool restart=automatic;captureTimer.Stop();pending=null;generation++;ackHash="";
            try {if(restart){Hide();await Task.Delay(200);await captureTask;await CaptureTick();}else SetStatus("\u663e\u793a\u8bbe\u7f6e\u5df2\u4fdd\u5b58\uff0c\u4e0b\u6b21\u53d1\u9001\u65f6\u751f\u6548\u3002");}
            finally{controlling=false;if(restart)captureTimer.Start();}
        }
        Task<Dictionary<string,object>> Control(string action){var request=new Dictionary<string,object>{{"action",action},{"session",action=="home"&&session.Length==0?Guid.NewGuid().ToString("N"):session},{"fullRefreshEvery",(int)fullRefreshEvery.Value}};if(action=="start"||action=="resume"||action=="orientation")request["orientation"]=Orientation;return Request("/control",new StringContent(json.Serialize(request),System.Text.Encoding.UTF8,"application/json"));}
        async Task ReturnHome(){
            if(controlling||closing||pairPending)return;controlling=true;
            wasAutomatic=automatic||wasAutomatic;automatic=false;captureTimer.Stop();StopScrolling();pending=null;generation++;
            SetScrollStatus("\u5df2\u505c\u6b62\u7ffb\u9875\u548c\u56fe\u7247\u53d1\u9001\u3002");
            try{await captureTask;await sendingTask;pending=null;generation++;await Control("home");SetStatus("BOOX \u5df2\u8bf7\u6c42\u8fd4\u56de\u684c\u9762\uff1b\u70b9\u7ee7\u7eed\u53ef\u6062\u590d\u56fe\u7247\u3002");}
            catch(Exception e){SetStatus("\u8fd4\u56de\u684c\u9762\u5931\u8d25\uff08\u9700 BOOX \u63a5\u6536\u5668 0.5.4\uff09: "+e.Message);}
            finally{controlling=false;}
        }
        async Task ChangeOrientation(){
            if(controlling||closing||pairPending)return;ackHash="";pending=null;generation++;if(session.Length==0)return;
            bool restart=automatic;controlling=true;captureTimer.Stop();
            try {await Control("orientation");SetStatus(direction.Text+"\uff1a\u5df2\u540c\u6b65\u5230 BOOX");if(restart){Hide();await Task.Delay(200);await captureTask;await CaptureTick();}}
            catch(Exception e){SetStatus(e.Message);}finally{controlling=false;if(restart)captureTimer.Start();}
        }
        async Task SelectRegion(){
            if(controlling||closing||pairPending)return;
            await Pause();Hide();await Task.Delay(200);
            using(var picker=new RegionPicker()){if(picker.ShowDialog()==DialogResult.OK){region=picker.Selection;hasRegion=true;captureWindow=false;fullScreen=false;UpdateRegionLabel();SaveSettings();}}
            ShowWindow();
        }
        async Task SelectWindow(){
            if(controlling||closing||pairPending)return;await Pause();if(closing)return;
            using(var picker=new Form{Text="选择持续同步的窗口",Size=new Size(680,430),StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false}){
                var list=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true};foreach(var target in WindowSource.List())list.Items.Add(target);
                var hint=new Label{Dock=DockStyle.Top,Height=55,Text="选中网页或文档窗口后，点击开始自动。\n普通屏保遮挡时继续取图；最小化、关闭或锁屏时保留最后画面。"};
                var ok=new Button{Text="使用此窗口",Dock=DockStyle.Bottom,Height=40};ok.Click+=delegate{if(list.SelectedItem!=null)picker.DialogResult=DialogResult.OK;};list.DoubleClick+=delegate{if(list.SelectedItem!=null)picker.DialogResult=DialogResult.OK;};
                picker.Controls.Add(list);picker.Controls.Add(hint);picker.Controls.Add(ok);picker.AcceptButton=ok;
                if(picker.ShowDialog(this)==DialogResult.OK){windowTarget=(WindowTarget)list.SelectedItem;captureWindow=true;fullScreen=false;hasRegion=true;regionLabel.Text="指定窗口："+windowTarget.Title;SaveSettings();SetStatus("已选定窗口；点击开始自动。请勿最小化目标窗口。");}
            }
        }
        Task Begin(bool auto,bool resume){return BeginCore(auto,resume,IntPtr.Zero);}
        async Task BeginCore(bool auto,bool resume,IntPtr expected){
            if(controlling||closing||pairPending)return;if(!captureWindow&&(fullScreen||!hasRegion))UseFullScreen();
            if(captureWindow)try{WindowSource.Validate(windowTarget);}catch(Exception e){SetStatus(e.Message);ShowWindow();return;}
            controlling=true;automatic=false;captureTimer.Stop();StopScrolling();pending=null;generation++;
            try {
                bool newSession=!resume||session.Length==0;
                if(newSession){session=Guid.NewGuid().ToString("N");sequence=0;}
                await Control(newSession?"start":"resume");ackHash="";retryAfter=DateTime.MinValue;automatic=auto;wasAutomatic=auto;
                Hide();
                if(auto&&autoScroll.Checked&&expected==IntPtr.Zero){SetScrollStatus("3 \u79d2\u5185\u70b9\u51fb\u8981\u9605\u8bfb\u7684\u7f51\u9875/\u6587\u6863\u6b63\u6587\uff0c\u7136\u540e\u81ea\u52a8\u9501\u5b9a\u3002");tray.ShowBalloonTip(3000,"BOOX",scrollStatus,ToolTipIcon.Info);await Task.Delay(3000);}else await Task.Delay(300);
                if(closing)return;if(auto){if(autoScroll.Checked)ArmScrolling(expected);else SetScrollStatus("\u672a\u542f\u7528\uff1b\u5728\u9605\u8bfb\u6b63\u6587\u6309 Ctrl+Alt+R \u542f\u52a8\u3002");}await captureTask;await CaptureTick();if(auto){captureTimer.Start();if(!autoScroll.Checked)tray.ShowBalloonTip(2000,"BOOX Max","\u6b63\u5728\u53d1\u9001\u9009\u5b9a\u533a\u57df\uff0c\u53cc\u51fb\u6258\u76d8\u56fe\u6807\u53ef\u7ba1\u7406\u3002",ToolTipIcon.Info);}else ShowWindow();
            }catch(Exception e){SetStatus(e.Message);ShowWindow();}finally{controlling=false;}
        }
        async Task SendFile(){
            if(controlling||closing||pairPending)return;using(var dialog=new OpenFileDialog {Filter="Images|*.png;*.jpg;*.jpeg;*.bmp",Title="\u9009\u62e9\u8981\u53d1\u9001\u7684\u56fe\u7247"}){
                if(dialog.ShowDialog()!=DialogResult.OK)return;controlling=true;automatic=false;wasAutomatic=false;captureTimer.Stop();StopScrolling();pending=null;generation++;
                try {string fileName=dialog.FileName;var options=SnapshotOptions();byte[] bytes=await Task.Run(delegate{using(var source=Image.FromFile(fileName)){if((long)source.Width*source.Height>100000000)throw new Exception("Image too large");Orient(source);return ImagePipeline.Encode(source,true,options);}});if(closing)return;lastWasCapture=false;
                    session=Guid.NewGuid().ToString("N");sequence=0;await Control("start");ackHash="";Queue(bytes);
                }catch(Exception e){SetStatus(e.Message);}finally{controlling=false;}
            }
        }
        static void Orient(Image image){if(Array.IndexOf(image.PropertyIdList,0x112)<0)return;int orientation=image.GetPropertyItem(0x112).Value[0];RotateFlipType[] map={RotateFlipType.RotateNoneFlipNone,RotateFlipType.RotateNoneFlipNone,RotateFlipType.RotateNoneFlipX,RotateFlipType.Rotate180FlipNone,RotateFlipType.Rotate180FlipX,RotateFlipType.Rotate90FlipX,RotateFlipType.Rotate90FlipNone,RotateFlipType.Rotate270FlipX,RotateFlipType.Rotate270FlipNone};if(orientation>=1&&orientation<=8)image.RotateFlip(map[orientation]);}
        Task CaptureTick(){
            if(capturing)return captureTask;
            captureTask=CaptureAsync();return captureTask;
        }
        async Task CaptureAsync(){
            if(closing||(!captureWindow&&!hasRegion)||session.Length==0)return;
            capturing=true;int captureGeneration=generation;string captureSession=session;
            try {
                bool windowMode=captureWindow;WindowTarget target=windowTarget;
                string blocked=DesktopGuard.BlockReason(windowMode);if(captureHold!=blocked){captureHold=blocked;LogLifecycle("capture "+(blocked.Length==0?"resumed":"held: "+blocked)+"; source="+(windowMode?"window":"desktop"));}
                if(blocked.Length>0){pending=null;SetStatus(windowMode?"锁屏或桌面不可访问，保留最后画面；解锁后自动恢复。":"屏保或锁屏中，保留最后桌面；持续同步请选择窗口。");return;}
                if(!windowMode&&fullScreen)UseFullScreen();Rectangle bounds=region;ImageOptions options=SnapshotOptions();lastWasCapture=true;
                byte[] bytes;
                if(windowMode){using(var source=await windowCapture.CaptureAsync(target,options.ShowCursor))bytes=await Task.Run(()=>ImagePipeline.Encode(source,false,options));}
                else bytes=await Task.Run(delegate {if(!DesktopGuard.CanCapture(false))return null;using(var source=ScreenCapture.Capture(bounds,options.ShowCursor))return ImagePipeline.Encode(source,false,options);});
                // A pause, new session or setting change invalidates work already in progress.
                if(bytes!=null&&!closing&&captureGeneration==generation&&captureSession==session&&DesktopGuard.CanCapture(windowMode))QueueCapture(bytes,windowMode?2:1);
            }catch(Exception e){if(captureGeneration==generation)SetStatus(e.Message);}
            finally{capturing=false;}
        }
        void Queue(byte[] bytes){
            QueueCapture(bytes,0);
        }
        void QueueCapture(byte[] bytes,int captureKind){
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            if(hash==ackHash&&!busy){pending=null;return;}
            if(pending!=null&&pending.Hash==hash)return;
            pending=new Frame {Bytes=bytes,Hash=hash,Session=session,Sequence=++sequence,Generation=generation,CaptureKind=captureKind};last=pending;
            StartPump();
        }
        void StartPump(){if(!busy)sendingTask=PumpAsync();}
        async Task PumpAsync(){
            if(busy||closing||pending==null||DateTime.UtcNow<retryAfter)return;busy=true;
            try {
                while(pending!=null&&!closing){
                    Frame frame=pending;pending=null;if(frame.Generation!=generation)continue;if(frame.Hash==ackHash)continue;
                    if(frame.CaptureKind!=0&&!DesktopGuard.CanCapture(frame.CaptureKind==2)){SetStatus("屏保或锁屏中，已拦截待发送截图，保留 BOOX 最后画面。");continue;}
                    try {using(var body=new ByteArrayContent(frame.Bytes)){body.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");body.Headers.Add("X-Image-Session",frame.Session);body.Headers.Add("X-Image-Sequence",frame.Sequence.ToString());await Request("/frame",body);}
                        if(frame.Generation!=generation)continue;ackHash=frame.Hash;retryAfter=DateTime.MinValue;
                        previewBytes=frame.Bytes;if(Visible&&WindowState!=FormWindowState.Minimized)UpdatePreview();
                        SetStatus(String.Format("\u5df2\u9001\u8fbe BOOX\uff1a\u753b\u9762 {0}\uff0c{1:N0} KB\uff0c{2:HH:mm:ss}\n\u95f4\u9694\u662f\u68c0\u67e5\u9891\u7387\uff1b\u5b9e\u9645\u663e\u793a\u53d6\u51b3\u4e8e\u7f51\u7edc\u4e0e\u58a8\u6c34\u5c4f\u3002",frame.Sequence,frame.Bytes.Length/1024.0,DateTime.Now));
                    }catch(Exception e){if(frame.Generation==generation){SetStatus("\u7b49\u5f85\u8fde\u63a5/\u7ee7\u7eed: "+e.Message);if(pending==null)pending=frame;retryAfter=DateTime.UtcNow.AddSeconds(3);}break;}
                }
            }finally{busy=false;}
        }
        async Task Pause(bool cleanFinalFrame=false){
            if(controlling||closing||pairPending)return;controlling=true;
            bool captureFinal=cleanFinalFrame&&lastWasCapture&&session.Length>0;
            wasAutomatic=automatic||wasAutomatic;automatic=false;captureTimer.Stop();StopScrolling();pending=null;generation++;
            if(autoScroll.Checked)SetScrollStatus("\u5df2\u6682\u505c\u7ffb\u9875\uff1b\u5728\u9605\u8bfb\u6b63\u6587\u6309 Ctrl+Alt+R \u6062\u590d\u3002");
            try {
                if(cleanFinalFrame){WindowState=FormWindowState.Minimized;Hide();await Task.Delay(200);}
                if(session.Length==0)return;
                if(captureFinal){await captureTask;await sendingTask;retryAfter=DateTime.MinValue;ackHash="";await CaptureTick();await sendingTask;}
                pending=null;generation++;await Control("pause");SetStatus("\u5df2\u6536\u8d77\u7a97\u53e3\u5e76\u6682\u505c\uff0cBOOX \u4fdd\u7559\u6700\u540e\u753b\u9762\u3002");
            }catch(Exception e){SetStatus(e.Message);}finally{controlling=false;}
        }
        async Task Resume(){
            if(controlling||closing||pairPending)return;if(wasAutomatic||last==null){await Begin(true,true);return;}
            controlling=true;generation++;pending=null;
            try {await Control("resume");ackHash="";retryAfter=DateTime.MinValue;QueueCapture(last.Bytes,last.CaptureKind);}catch(Exception e){SetStatus(e.Message);}finally{controlling=false;}
        }
        async Task Poll(){
            if(polling||controlling||closing||pairPending)return;polling=true;int observedGeneration=generation;
            try {
                var remote=await Request("/state");if(observedGeneration!=generation||controlling||closing)return;string remoteSession=Convert.ToString(remote["session"]);
                // Reconcile a receipt lost after BOOX accepted a frame.
                if(remoteSession==session&&!busy)ackHash=Convert.ToString(remote["sha256"]);
                if(automatic&&session.Length>0&&remoteSession.Length==0){await Control("start");ackHash="";retryAfter=DateTime.MinValue;}
                else if(session.Length>0&&remoteSession==session&&Convert.ToBoolean(remote["paused"])&&(automatic||pending!=null||capturing)){
                    wasAutomatic=automatic||wasAutomatic;automatic=false;captureTimer.Stop();StopScrolling();pending=null;generation++;SetStatus("BOOX \u5df2\u6682\u505c\uff08\u9605\u8bfb/\u9000\u51fa/\u6253\u5370\uff09\uff0c\u70b9\u201c\u7ee7\u7eed\u201d\u6062\u590d\u3002");
                }else if(automatic&&remoteSession.Length>0&&remoteSession!=session){automatic=false;captureTimer.Stop();StopScrolling();pending=null;generation++;SetStatus("\u53e6\u4e00\u4e2a\u56fe\u7247\u4f1a\u8bdd\u5df2\u542f\u52a8\uff0c\u672c\u6b21\u53d1\u9001\u6682\u505c\u3002");}
                if(pending!=null)StartPump();
            }catch(Exception e){if(session.Length==0||automatic)SetStatus("BOOX Wi-Fi: "+e.Message);}finally{polling=false;}
        }
        internal void RequestExit(string source){
            if(closing)return;
            if(EnforceProcessExit)exitDeadline=new System.Threading.Timer(delegate{Environment.Exit(0);},null,5000,System.Threading.Timeout.Infinite);
            LogLifecycle("exit requested from "+source+"; session="+(session.Length>0));closing=true;
            SaveSettings();captureTimer.Stop();pollTimer.Stop();StopScrolling();pending=null;generation++;
            // Queue after the tray click/WM_CLOSE has unwound, without closing recursively.
            BeginInvoke((Action)FinishExit);
        }
        async void FinishExit(){
            try{
                UnregisterHotKey(Handle,1);UnregisterHotKey(Handle,2);UnregisterHotKey(Handle,3);tray.Visible=false;tray.Dispose();windowCapture.Dispose();
                if(session.Length>0){try{var pauseRequest=Control("pause");await Task.WhenAny(pauseRequest,Task.Delay(2000));if(pauseRequest.IsFaulted){var ignored=pauseRequest.Exception;}}catch{}}
                LogLifecycle("exit network wait finished");http.CancelPendingRequests();
            }catch(Exception e){LogLifecycle("exit cleanup error: "+e.GetType().Name);}
            finally{closeReady=true;try{Close();}finally{Application.ExitThread();}}
        }
        protected override void OnFormClosing(FormClosingEventArgs e){
            if(closeReady){base.OnFormClosing(e);return;}
            e.Cancel=true;RequestExit("window close");
        }
        protected override void OnFormClosed(FormClosedEventArgs e){
            LogLifecycle("form closed");
            captureTimer.Dispose();pollTimer.Dispose();scrollTimer.Dispose();scroller.Dispose();windowCapture.Dispose();http.Dispose();
            if(!EnforceProcessExit&&exitDeadline!=null)exitDeadline.Dispose();
            base.OnFormClosed(e);
        }
    }
    sealed class RegionPicker:Form {
        Point start,current;bool dragging;public Rectangle Selection;
        public RegionPicker(){FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;Bounds=SystemInformation.VirtualScreen;TopMost=true;BackColor=Color.Black;Opacity=.35;Cursor=Cursors.Cross;DoubleBuffered=true;KeyPreview=true;
            KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Escape){DialogResult=DialogResult.Cancel;Close();}};
            MouseDown+=delegate(object s,MouseEventArgs e){start=current=e.Location;dragging=true;};
            MouseMove+=delegate(object s,MouseEventArgs e){if(dragging){current=e.Location;Invalidate();}};
            MouseUp+=delegate(object s,MouseEventArgs e){current=e.Location;Rectangle r=Box();if(r.Width<20||r.Height<20)return;Selection=new Rectangle(r.X+Left,r.Y+Top,r.Width,r.Height);DialogResult=DialogResult.OK;Close();};}
        Rectangle Box(){return new Rectangle(Math.Min(start.X,current.X),Math.Min(start.Y,current.Y),Math.Abs(start.X-current.X),Math.Abs(start.Y-current.Y));}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var p=new Pen(Color.White,3))e.Graphics.DrawRectangle(p,Box());e.Graphics.DrawString("\u62d6\u52a8\u6846\u9009\u533a\u57df  |  Esc \u53d6\u6d88",new Font("Microsoft YaHei",22),Brushes.White,30,30);}
    }
}
