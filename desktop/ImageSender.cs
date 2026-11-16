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

[assembly:System.Reflection.AssemblyVersion("0.4.2.0")]
[assembly:System.Reflection.AssemblyFileVersion("0.4.2.0")]

namespace BooxImages {
    static class Program {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread] static void Main(string[] args) {
            SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            bool created;using(var mutex=new System.Threading.Mutex(true,"Local\\BooxMaxImageSender",out created)) {
                if(!created){MessageBox.Show("BOOX \u56fe\u7247\u53d1\u9001\u5668\u5df2\u5728\u8fd0\u884c\uff0c\u8bf7\u4ece\u6258\u76d8\u6253\u5f00\u3002");return;}
                try {Application.Run(new SenderForm());}catch(Exception e){MessageBox.Show(e.Message,"BOOX Max Images");}
            }
        }
    }
    sealed class Frame {
        internal byte[] Bytes;internal string Hash,Session;internal long Sequence;internal int Generation;
    }
    public sealed class SenderForm:Form {
        readonly HttpClient http=new HttpClient(new HttpClientHandler { UseProxy=false });
        readonly JavaScriptSerializer json=new JavaScriptSerializer();
        readonly Timer captureTimer=new Timer(),pollTimer=new Timer();
        readonly ComboBox interval=new ComboBox(),contrast=new ComboBox(),direction=new ComboBox(),enhancement=new ComboBox();
        readonly CheckBox gray=new CheckBox(),showCursor=new CheckBox();
        readonly string settingsPath;
        readonly Icon appIcon,trayIcon;
        readonly Label status=new Label(),regionLabel=new Label();
        readonly PictureBox preview=new PictureBox();
        readonly NotifyIcon tray=new NotifyIcon();
        Rectangle region;bool hasRegion,automatic,busy,polling,controlling,closing,wasAutomatic,fullScreen=true,lastWasCapture;
        Task sendingTask=Task.FromResult(0),captureTask=Task.FromResult(0);
        bool capturing;
        byte[] previewBytes;
        string session="",ackHash="",baseUrl;long sequence;int generation;Frame pending,last;DateTime retryAfter=DateTime.MinValue;
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
        public SenderForm():this(null){}
        internal SenderForm(string root) {
            Text="BOOX Max \u56fe\u7247\u53d1\u9001";ClientSize=new Size(880,650);MinimumSize=new Size(880,620);Font=new Font("Microsoft YaHei",10);StartPosition=FormStartPosition.CenterScreen;
            if(root==null)root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));
            settingsPath=Path.Combine(root,".local","image-settings.json");var saved=ImageSettings.Load(settingsPath);
            var assembly=System.Reflection.Assembly.GetExecutingAssembly();
            using(var stream=assembly.GetManifestResourceStream("BooxImages.AppIcon"))appIcon=new Icon(stream);
            using(var stream=assembly.GetManifestResourceStream("BooxImages.TrayIcon"))trayIcon=new Icon(stream,32,32);
            Icon=appIcon;
            var config=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,".local","image-client.json")));
            baseUrl=(string)config["url"];http.Timeout=TimeSpan.FromSeconds(22);http.DefaultRequestHeaders.Add("X-Boox-Client",(string)config["key"]);
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,Padding=new Padding(15)};
            foreach(int height in new int[]{42,44,44,44,30,38})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,66));Controls.Add(layout);
            var heading=new Label {Text="\u628a\u7535\u8111\u753b\u9762\u6216\u56fe\u7247\u53d1\u9001\u5230 BOOX",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,15,FontStyle.Bold)};layout.Controls.Add(heading);
            var actions=new FlowLayoutPanel {Dock=DockStyle.Fill};layout.Controls.Add(actions);
            Add(actions,"\u9009\u62e9\u56fe\u7247",async delegate {await SendFile();});
            Add(actions,"\u6574\u5c4f",async delegate {if(controlling||closing)return;await Pause();fullScreen=true;UseFullScreen();});
            Add(actions,"\u6846\u9009\u533a\u57df",async delegate {await SelectRegion();});
            Add(actions,"\u53d1\u9001\u4e00\u6b21",async delegate {await Begin(false,false);});
            Add(actions,"\u5f00\u59cb\u81ea\u52a8",async delegate {await Begin(true,false);});
            direction.DropDownStyle=ComboBoxStyle.DropDownList;direction.Width=178;direction.Items.AddRange(new object[]{"\u7ad6\u5c4f 1200\u00d71600","\u6a2a\u5c4f 1600\u00d71200"});direction.SelectedIndex=0;direction.Margin=new Padding(10,5,0,0);
            direction.SelectedIndex=saved.Direction;
            direction.SelectedIndexChanged+=async delegate {SaveSettings();await ChangeOrientation();};
            var options=new FlowLayoutPanel {Dock=DockStyle.Fill};layout.Controls.Add(options);
            options.Controls.Add(new Label {Text="\u68c0\u67e5\u95f4\u9694(\u79d2)",AutoSize=true,Margin=new Padding(3,8,4,0)});
            interval.DropDownStyle=ComboBoxStyle.DropDownList;interval.Width=72;interval.Items.AddRange(new object[]{"0.5","1","2","5","10"});interval.SelectedIndex=0;options.Controls.Add(interval);
            interval.SelectedIndexChanged+=delegate {captureTimer.Interval=(int)(double.Parse((string)interval.SelectedItem,System.Globalization.CultureInfo.InvariantCulture)*1000);};
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
            layout.Controls.Add(new Label {Text="\u5feb\u6377\u952e\uff1aCtrl+Alt+B \u53d1\u9001\u622a\u56fe   |   Ctrl+Alt+P \u6682\u505c / \u7ee7\u7eed",Dock=DockStyle.Fill,ForeColor=Color.DimGray});
            regionLabel.Dock=DockStyle.Fill;layout.Controls.Add(regionLabel);UseFullScreen();
            preview.Dock=DockStyle.Fill;preview.SizeMode=PictureBoxSizeMode.Zoom;preview.BackColor=Color.FromArgb(240,240,240);layout.Controls.Add(preview);
            status.Dock=DockStyle.Fill;status.Text="\u5c31\u7eea\u3002\u70b9\u201c\u5f00\u59cb\u81ea\u52a8\u201d\u5373\u53ef\u53d1\u9001\u4e3b\u663e\u793a\u5668\u6574\u5c4f\u3002";layout.Controls.Add(status);
            tray.Icon=trayIcon;tray.Text="BOOX Max \u56fe\u7247";tray.Visible=true;tray.DoubleClick+=delegate{ShowWindow();};
            var menu=new ContextMenuStrip();menu.Items.Add("\u6253\u5f00\u63a7\u5236\u7a97\u53e3",null,delegate{ShowWindow();});menu.Items.Add("\u53d1\u9001\u622a\u56fe",null,async delegate{await Begin(false,false);});menu.Items.Add("\u6682\u505c",null,async delegate{await Pause(true);});menu.Items.Add("\u7ee7\u7eed",null,async delegate{await Resume();});menu.Items.Add("\u9000\u51fa",null,delegate{Close();});menu.Items.Add(new ToolStripSeparator());menu.Items.Add(new ToolStripMenuItem("Ctrl+Alt+B  \u53d1\u9001\u622a\u56fe"){Enabled=false});menu.Items.Add(new ToolStripMenuItem("Ctrl+Alt+P  \u6682\u505c / \u7ee7\u7eed"){Enabled=false});tray.ContextMenuStrip=menu;
            captureTimer.Interval=500;captureTimer.Tick+=async delegate{await CaptureTick();};pollTimer.Interval=2000;pollTimer.Tick+=async delegate{await Poll();};pollTimer.Start();
            Shown+=delegate {bool a=RegisterHotKey(Handle,1,0x4003,0x42),b=RegisterHotKey(Handle,2,0x4003,0x50);if(!a||!b)SetStatus("\u90e8\u5206\u5feb\u6377\u952e\u88ab\u5360\u7528\uff0c\u8bf7\u4f7f\u7528\u6309\u94ae\u3002");};
            Resize+=delegate{if(WindowState==FormWindowState.Minimized)Hide();};
        }
        void Add(Control row,string text,EventHandler click){var b=new Button {Text=text,AutoSize=true,Height=34,Margin=new Padding(3)};b.Click+=click;row.Controls.Add(b);}
        void ShowWindow(){Show();WindowState=FormWindowState.Normal;UpdatePreview();Activate();}
        void UpdatePreview(){if(previewBytes==null)return;using(var stream=new MemoryStream(previewBytes))using(var image=Image.FromStream(stream)){var old=preview.Image;preview.Image=new Bitmap(image);if(old!=null)old.Dispose();}previewBytes=null;}
        void SaveSettings(){try{new ImageSettings {Direction=direction.SelectedIndex,Enhancement=enhancement.SelectedIndex,Grayscale=gray.Checked,Contrast=contrast.SelectedIndex,ShowCursor=showCursor.Checked}.Save(settingsPath);}catch(Exception e){SetStatus("\u8bbe\u7f6e\u4fdd\u5b58\u5931\u8d25: "+e.Message);}}
        ImageOptions SnapshotOptions(){return new ImageOptions {Landscape=direction.SelectedIndex==1,Grayscale=gray.Checked,Enhancement=enhancement.SelectedIndex,Contrast=float.Parse((string)contrast.SelectedItem,System.Globalization.CultureInfo.InvariantCulture),ShowCursor=showCursor.Checked};}
        void SetStatus(string text){if(!closing)status.Text=text;}
        protected override void WndProc(ref Message m){
            if(m.Msg==0x312){HandleHotkey(m.WParam.ToInt32());return;}base.WndProc(ref m);
        }
        async void HandleHotkey(int id){if(id==1)await Begin(false,false);else if(id==2){if(automatic)await Pause(true);else await Resume();}}
        async Task<Dictionary<string,object>> Request(string endpoint,HttpContent content=null){
            using(var response=content==null?await http.GetAsync(baseUrl+endpoint):await http.PostAsync(baseUrl+endpoint,content)){
                string text=await response.Content.ReadAsStringAsync();var result=json.Deserialize<Dictionary<string,object>>(text);
                if(!response.IsSuccessStatusCode)throw new InvalidOperationException(result.ContainsKey("error")?Convert.ToString(result["error"]):text);return result;
            }
        }
        string Orientation {get{return direction.SelectedIndex==1?"landscape":"portrait";}}
        void UseFullScreen(){
            region=Screen.PrimaryScreen.Bounds;hasRegion=true;
            regionLabel.Text=String.Format("\u6574\u5c4f\uff08\u4e3b\u663e\u793a\u5668\uff09: {0}\u00d7{1}  |  \u53ef\u76f4\u63a5\u5f00\u59cb\uff0c\u6846\u9009\u4e3a\u53ef\u9009\u529f\u80fd",region.Width,region.Height);
        }
        async Task ChangeDisplaySettings(){
            if(controlling||closing)return;controlling=true;bool restart=automatic;captureTimer.Stop();pending=null;generation++;ackHash="";
            try {if(restart){Hide();await Task.Delay(200);await captureTask;await CaptureTick();}else SetStatus("\u663e\u793a\u8bbe\u7f6e\u5df2\u4fdd\u5b58\uff0c\u4e0b\u6b21\u53d1\u9001\u65f6\u751f\u6548\u3002");}
            finally{controlling=false;if(restart)captureTimer.Start();}
        }
        Task<Dictionary<string,object>> Control(string action){return Request("/control",new StringContent(json.Serialize(new {action=action,session=session,orientation=Orientation}),System.Text.Encoding.UTF8,"application/json"));}
        async Task ChangeOrientation(){
            if(controlling||closing)return;ackHash="";pending=null;generation++;if(session.Length==0)return;
            bool restart=automatic;controlling=true;captureTimer.Stop();
            try {await Control("orientation");SetStatus(direction.Text+"\uff1a\u5df2\u540c\u6b65\u5230 BOOX");if(restart){Hide();await Task.Delay(200);await captureTask;await CaptureTick();}}
            catch(Exception e){SetStatus(e.Message);}finally{controlling=false;if(restart)captureTimer.Start();}
        }
        async Task SelectRegion(){
            if(controlling||closing)return;
            await Pause();Hide();await Task.Delay(200);
            using(var picker=new RegionPicker()){if(picker.ShowDialog()==DialogResult.OK){region=picker.Selection;hasRegion=true;fullScreen=false;regionLabel.Text=String.Format("\u6846\u9009\u533a\u57df: {0} \u00d7 {1}  ({2}, {3})",region.Width,region.Height,region.X,region.Y);}}
            ShowWindow();
        }
        async Task Begin(bool auto,bool resume){
            if(controlling||closing)return;if(fullScreen||!hasRegion)UseFullScreen();
            controlling=true;automatic=false;captureTimer.Stop();pending=null;generation++;
            try {
                bool newSession=!resume||session.Length==0;
                if(newSession){session=Guid.NewGuid().ToString("N");sequence=0;}
                await Control(newSession?"start":"resume");ackHash="";retryAfter=DateTime.MinValue;automatic=auto;wasAutomatic=auto;
                Hide();await Task.Delay(200);await captureTask;await CaptureTick();if(auto){captureTimer.Start();tray.ShowBalloonTip(2000,"BOOX Max","\u6b63\u5728\u53d1\u9001\u9009\u5b9a\u533a\u57df\uff0c\u53cc\u51fb\u6258\u76d8\u56fe\u6807\u53ef\u7ba1\u7406\u3002",ToolTipIcon.Info);}else ShowWindow();
            }catch(Exception e){SetStatus(e.Message);ShowWindow();}finally{controlling=false;}
        }
        async Task SendFile(){
            if(controlling||closing)return;using(var dialog=new OpenFileDialog {Filter="Images|*.png;*.jpg;*.jpeg;*.bmp",Title="\u9009\u62e9\u8981\u53d1\u9001\u7684\u56fe\u7247"}){
                if(dialog.ShowDialog()!=DialogResult.OK)return;controlling=true;automatic=false;wasAutomatic=false;captureTimer.Stop();pending=null;generation++;
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
            if(closing||!hasRegion||session.Length==0)return;
            capturing=true;int captureGeneration=generation;string captureSession=session;
            try {
                if(fullScreen)UseFullScreen();Rectangle bounds=region;ImageOptions options=SnapshotOptions();lastWasCapture=true;
                byte[] bytes=await Task.Run(delegate {using(var source=ScreenCapture.Capture(bounds,options.ShowCursor))return ImagePipeline.Encode(source,false,options);});
                // A pause, new session or setting change invalidates work already in progress.
                if(!closing&&captureGeneration==generation&&captureSession==session)Queue(bytes);
            }catch(Exception e){if(captureGeneration==generation)SetStatus(e.Message);}
            finally{capturing=false;}
        }
        void Queue(byte[] bytes){
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            if(hash==ackHash&&!busy){pending=null;return;}
            if(pending!=null&&pending.Hash==hash)return;
            pending=new Frame {Bytes=bytes,Hash=hash,Session=session,Sequence=++sequence,Generation=generation};last=pending;
            StartPump();
        }
        void StartPump(){if(!busy)sendingTask=PumpAsync();}
        async Task PumpAsync(){
            if(busy||closing||pending==null||DateTime.UtcNow<retryAfter)return;busy=true;
            try {
                while(pending!=null&&!closing){
                    Frame frame=pending;pending=null;if(frame.Generation!=generation)continue;if(frame.Hash==ackHash)continue;
                    try {using(var body=new ByteArrayContent(frame.Bytes)){body.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");body.Headers.Add("X-Image-Session",frame.Session);body.Headers.Add("X-Image-Sequence",frame.Sequence.ToString());await Request("/frame",body);}
                        if(frame.Generation!=generation)continue;ackHash=frame.Hash;retryAfter=DateTime.MinValue;
                        previewBytes=frame.Bytes;if(Visible&&WindowState!=FormWindowState.Minimized)UpdatePreview();
                        SetStatus(String.Format("\u5df2\u9001\u8fbe BOOX\uff1a\u753b\u9762 {0}\uff0c{1:N0} KB\uff0c{2:HH:mm:ss}\n\u95f4\u9694\u662f\u68c0\u67e5\u9891\u7387\uff1b\u5b9e\u9645\u663e\u793a\u53d6\u51b3\u4e8e\u7f51\u7edc\u4e0e\u58a8\u6c34\u5c4f\u3002",frame.Sequence,frame.Bytes.Length/1024.0,DateTime.Now));
                    }catch(Exception e){if(frame.Generation==generation){SetStatus("\u7b49\u5f85\u8fde\u63a5/\u7ee7\u7eed: "+e.Message);if(pending==null)pending=frame;retryAfter=DateTime.UtcNow.AddSeconds(3);}break;}
                }
            }finally{busy=false;}
        }
        async Task Pause(bool cleanFinalFrame=false){
            if(controlling||closing)return;controlling=true;
            bool captureFinal=cleanFinalFrame&&lastWasCapture&&session.Length>0;
            wasAutomatic=automatic||wasAutomatic;automatic=false;captureTimer.Stop();pending=null;generation++;
            try {
                if(cleanFinalFrame){WindowState=FormWindowState.Minimized;Hide();await Task.Delay(200);}
                if(session.Length==0)return;
                if(captureFinal){await captureTask;await sendingTask;retryAfter=DateTime.MinValue;ackHash="";await CaptureTick();await sendingTask;}
                pending=null;generation++;await Control("pause");SetStatus("\u5df2\u6536\u8d77\u7a97\u53e3\u5e76\u6682\u505c\uff0cBOOX \u4fdd\u7559\u6700\u540e\u753b\u9762\u3002");
            }catch(Exception e){SetStatus(e.Message);}finally{controlling=false;}
        }
        async Task Resume(){
            if(controlling||closing)return;if(wasAutomatic||last==null){await Begin(true,true);return;}
            controlling=true;generation++;pending=null;
            try {await Control("resume");ackHash="";retryAfter=DateTime.MinValue;Queue(last.Bytes);}catch(Exception e){SetStatus(e.Message);}finally{controlling=false;}
        }
        async Task Poll(){
            if(polling||controlling||closing)return;polling=true;int observedGeneration=generation;
            try {
                var remote=await Request("/state");if(observedGeneration!=generation||controlling||closing)return;string remoteSession=Convert.ToString(remote["session"]);
                // Reconcile a receipt lost after BOOX accepted a frame.
                if(remoteSession==session&&!busy)ackHash=Convert.ToString(remote["sha256"]);
                if(automatic&&session.Length>0&&remoteSession.Length==0){await Control("start");ackHash="";retryAfter=DateTime.MinValue;}
                else if(session.Length>0&&remoteSession==session&&Convert.ToBoolean(remote["paused"])&&(automatic||pending!=null||capturing)){
                    wasAutomatic=automatic||wasAutomatic;automatic=false;captureTimer.Stop();pending=null;generation++;SetStatus("BOOX \u5df2\u6682\u505c\uff08\u9605\u8bfb/\u9000\u51fa/\u6253\u5370\uff09\uff0c\u70b9\u201c\u7ee7\u7eed\u201d\u6062\u590d\u3002");
                }else if(automatic&&remoteSession.Length>0&&remoteSession!=session){automatic=false;captureTimer.Stop();pending=null;generation++;SetStatus("\u53e6\u4e00\u4e2a\u56fe\u7247\u4f1a\u8bdd\u5df2\u542f\u52a8\uff0c\u672c\u6b21\u53d1\u9001\u6682\u505c\u3002");}
                if(pending!=null)StartPump();
            }catch(Exception e){if(session.Length==0||automatic)SetStatus("BOOX Wi-Fi: "+e.Message);}finally{polling=false;}
        }
        protected override async void OnFormClosing(FormClosingEventArgs e){
            if(closing){base.OnFormClosing(e);return;}
            e.Cancel=true;SaveSettings();closing=true;captureTimer.Stop();pollTimer.Stop();pending=null;generation++;UnregisterHotKey(Handle,1);UnregisterHotKey(Handle,2);tray.Visible=false;tray.Dispose();
            if(session.Length>0){try{var pauseRequest=Control("pause");await Task.WhenAny(pauseRequest,Task.Delay(2000));if(pauseRequest.IsFaulted){var ignored=pauseRequest.Exception;}}catch{}}
            Close();
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
