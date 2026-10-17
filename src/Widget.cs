using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexLimits {
    public static class Theme {
        public static readonly Color Surface=Color.FromArgb(44,44,44), Text=Color.FromArgb(247,247,247), Muted=Color.FromArgb(191,191,191),
            Green=Color.FromArgb(189,231,204), Amber=Color.FromArgb(255,210,151), Red=Color.FromArgb(255,164,164);
        public static void TextAt(Graphics g,string text,float size,Color color,Rectangle bounds,bool bold) {
            g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using(var font=new Font("Segoe UI",size, bold ? FontStyle.Bold : FontStyle.Regular,GraphicsUnit.Pixel))
            using(var brush=new SolidBrush(color))
            using(var format=new StringFormat(StringFormat.GenericTypographic) { LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap })
                g.DrawString(text,font,brush,bounds,format);
        }
        public static Color QuotaColor(double? percent) { return !percent.HasValue ? Muted : percent<=10 ? Red : percent<=25 ? Amber : Green; }
        public static void InlineText(Graphics g,string text,float size,Rectangle bounds) {
            using(var font=new Font("Segoe UI",size,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var brush=new SolidBrush(Text))
            using(var format=new StringFormat(StringFormat.GenericTypographic) {LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})
                g.DrawString(text,font,brush,bounds,format);
        }
        public static GraphicsPath Rounded(Rectangle rect,int radius) {
            var path=new GraphicsPath();int diameter=radius*2;
            path.AddArc(rect.Left,rect.Top,diameter,diameter,180,90);path.AddArc(rect.Right-diameter,rect.Top,diameter,diameter,270,90);
            path.AddArc(rect.Right-diameter,rect.Bottom-diameter,diameter,diameter,0,90);path.AddArc(rect.Left,rect.Bottom-diameter,diameter,diameter,90,90);path.CloseFigure();return path;
        }
    }
    public class WidgetSettings {
        public int OffsetX,OffsetY;
        public static WidgetSettings Load() {
            try { return Json.Serializer.Deserialize<WidgetSettings>(File.ReadAllText(Path.Combine(Program.DataDirectory,"settings.json"))); }
            catch { return new WidgetSettings(); }
        }
        public void Save() { File.WriteAllText(Path.Combine(Program.DataDirectory,"settings.json"),Json.Serializer.Serialize(this)); }
    }
    public sealed class Widget : Form {
        readonly CodexClient client=new CodexClient();
        readonly Timer clock=new Timer { Interval=250 };
        readonly ToolTip tooltip=new ToolTip { InitialDelay=250, ReshowDelay=100,AutoPopDelay=15000 };
        readonly WidgetSettings settings;
        readonly bool preview;
        NotifyIcon tray; ContextMenuStrip menu; DetailsForm details;
        Anchor anchor; bool finding,refreshing,closing,dragging,paused;
        Point dragStart,dragOrigin; bool moved;
        DateTime nextAnchor=DateTime.MinValue,nextRefresh=DateTime.MinValue;
        public Snapshot Data;
        public DateTimeOffset? Updated { get; set; }
        public string LastError;
        public bool InitialDetails;
        DateTime nextStatus=DateTime.MinValue;
        public double ScaleFactor=1;
        public bool LayeredOk;
        int shownCount,hiddenCount,paintCount,focusReturns;
        string paintKey;
        IntPtr owner;
        bool BadgeVisible {get{return IsHandleCreated&&Native.IsWindowVisible(Handle);}}
        public bool Stale { get { return LastError!=null || (Updated.HasValue && DateTimeOffset.UtcNow-Updated.Value>TimeSpan.FromMinutes(3)); } }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle |= 0x08000000|0x80|(preview?0:0x80000); return p; } }
        protected override void WndProc(ref Message m) { if (m.Msg==0x21) { m.Result=new IntPtr(3); return; } base.WndProc(ref m); }
        protected override void OnActivated(EventArgs e) {
            base.OnActivated(e);
            if(!preview&&IsHandleCreated&&!closing)BeginInvoke((Action)RestoreHostFocus);
        }
        void RestoreHostFocus() {
            // A collapsed indicator must never retain keyboard focus after a framework activation.
            if(anchor!=null&&anchor.Window!=IntPtr.Zero&&Native.GetForegroundWindow()==Handle&&!Native.IsIconic(anchor.Window))
                if(Native.SetForegroundWindow(anchor.Window))focusReturns++;
        }
        public Widget(bool previewMode) {
            preview=previewMode; settings=WidgetSettings.Load();
            FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true; StartPosition=FormStartPosition.Manual;
            AutoScaleMode=AutoScaleMode.None; Size=new Size(280,26); BackColor=Color.Black;
            DoubleBuffered=true; Cursor=Cursors.Hand; Text="Codex Limits"; AccessibleName="Лимиты Codex"; AccessibleRole=AccessibleRole.PushButton;
            if (preview) return;
            menu=new ContextMenuStrip();
            menu.Items.Add("Подробнее",null,delegate { ToggleDetails(); });
            menu.Items.Add("Обновить сейчас",null,delegate { RefreshLimits(); });
            menu.Items.Add("Вернуть положение",null,delegate { settings.OffsetX=0;settings.OffsetY=0;settings.Save();nextAnchor=DateTime.MinValue; });
            var hide=menu.Items.Add("Скрыть индикатор",null,delegate { paused=!paused; });
            menu.Opening += delegate { hide.Text=paused?"Показать индикатор":"Скрыть индикатор"; };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Открыть папку приложения",null,delegate { System.Diagnostics.Process.Start(AppDomain.CurrentDomain.BaseDirectory); });
            menu.Items.Add("Выход",null,delegate { Close(); });
            ContextMenuStrip=menu;
            tray=new NotifyIcon { Icon=SystemIcons.Information, Text="Codex Limits — загрузка", Visible=true,ContextMenuStrip=menu };
            tray.DoubleClick += delegate { ToggleDetails(); };
            clock.Tick += Tick; clock.Start();
            MouseDown += delegate(object sender,MouseEventArgs e) { if(e.Button!=MouseButtons.Left)return;dragging=true;moved=false;dragStart=Cursor.Position;dragOrigin=Location;Capture=true; };
            MouseMove += delegate {
                if(!dragging)return;
                int dx=Cursor.Position.X-dragStart.X,dy=Cursor.Position.Y-dragStart.Y;
                if(Math.Abs(dx)+Math.Abs(dy)>5) { moved=true;Location=new Point(dragOrigin.X+dx,dragOrigin.Y+dy); }
            };
            MouseUp += delegate(object sender,MouseEventArgs e) {
                if(!dragging)return; dragging=false;Capture=false;
                if(moved) { settings.OffsetX+=Location.X-dragOrigin.X;settings.OffsetY+=Location.Y-dragOrigin.Y;settings.Save(); }
                else ToggleDetails();
            };
            FormClosing += delegate { closing=true;clock.Stop();if(tray!=null)tray.Dispose();if(details!=null)details.Close();client.Dispose(); };
            FormClosed += delegate { Application.ExitThread(); };
        }
        async void Tick(object sender,EventArgs e) {
            if(closing)return;
            if(Program.QuitRequest!=null&&Program.QuitRequest.WaitOne(0)) {Close();return;}
            if(Program.ShowRequest!=null&&Program.ShowRequest.WaitOne(0))ToggleDetails();
            if(DateTime.UtcNow>=nextRefresh && !refreshing) RefreshLimits();
            if(DateTime.UtcNow>=nextAnchor && !finding) {
                finding=true; nextAnchor=DateTime.UtcNow.AddSeconds(1);
                try { var found=await Task.Run(()=>Native.Locate()); if(!closing)anchor=found; }
                catch(Exception) { anchor=new Anchor {Status="Строка ввода недоступна"}; }
                finally { finding=false; }
            }
            else if(!finding&&anchor!=null&&anchor.ModelElement!=null) {
                finding=true;try {var previous=anchor;anchor=await Task.Run(()=>Native.Refresh(previous));}catch(Exception){anchor=new Anchor();}finally{finding=false;}
            }
            if(closing || dragging)return;
            bool ownPopup=details!=null&&!details.IsDisposed&&details.Visible;
            bool ownMenu=menu!=null&&menu.Visible;
            IntPtr foreground=Native.GetAncestor(Native.GetForegroundWindow(),2);
            bool active=anchor!=null&&Placement.HostContext(foreground.ToInt64(),anchor.Window.ToInt64(),Handle.ToInt64(),ownPopup,ownMenu);
            if(DateTime.UtcNow>=nextStatus) {
                nextStatus=DateTime.UtcNow.AddSeconds(10);
                try { File.WriteAllText(Path.Combine(Program.DataDirectory,"status.json"),Json.Serializer.Serialize(new {
                    hasData=Data!=null,remaining=Data==null||Data.MainWindow==null?null:Data.MainWindow.Remaining,updatedAt=Updated,stale=Stale,
                    error=LastError,hostActive=active,visible=BadgeVisible,exact=anchor!=null&&anchor.Exact,anchor=anchor==null?null:anchor.Status,bounds=Bounds,
                    transparent=true,rendered=LayeredOk,scale=ScaleFactor,dictation=anchor!=null&&anchor.VoiceActive,
                    shown=shownCount,hidden=hiddenCount,paints=paintCount,focusReturns=focusReturns,window=Handle.ToInt64(),font="Segoe UI Bold"
                })); }catch(IOException) { }
            }
            if(paused || anchor==null || anchor.Bounds.IsEmpty || Native.IsIconic(anchor.Window) || !active) { HideBadge();if(anchor!=null&&anchor.VoiceActive&&details!=null)details.Hide(); return; }
            var target=anchor.Bounds;
            Native.RECT current;
            if(Native.GetWindowRect(anchor.Window,out current)) target.Offset(current.Left-anchor.HostBounds.Left,current.Top-anchor.HostBounds.Top);
            target.Offset(settings.OffsetX,settings.OffsetY);
            bool scaleChanged=Math.Abs(ScaleFactor-anchor.Scale)>.01;
            bool boundsChanged=Bounds!=target;
            ScaleFactor=anchor.Scale;
            if(boundsChanged)Bounds=target;
            if(owner!=anchor.Window){Native.SetWindowLongPtr(Handle,-8,anchor.Window);owner=anchor.Window;}
            if(!BadgeVisible) {
                // Native visibility avoids Form.Show activation and its focus feedback loop.
                if(Native.SetWindowPos(Handle,new IntPtr(-1),target.X,target.Y,target.Width,target.Height,0x10|0x40|0x200))shownCount++;
            }
            RepaintBadge();
            RestoreHostFocus();
            if(details!=null&&!details.IsDisposed&&details.Visible&&(scaleChanged||boundsChanged)) {details.RefreshView();PositionDetails();}
        }
        void HideBadge() {
            if(BadgeVisible&&Native.SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x1|0x2|0x4|0x10|0x80))hiddenCount++;
        }
        public async void RefreshLimits() {
            if(refreshing || closing || preview)return;
            refreshing=true;nextRefresh=DateTime.UtcNow.AddMinutes(1);
            try { var snapshot=await Task.Run(()=>client.Read()); if(closing)return;Data=snapshot;Updated=DateTimeOffset.UtcNow;LastError=null; }
            catch(Exception e) { if(closing)return; LastError=e is FileNotFoundException?e.Message:"Не удалось обновить лимиты. Проверьте подключение и вход в Codex."; }
            finally { refreshing=false; }
            if(closing)return;
            var quota=Data==null?null:Data.MainWindow;
            string tip=(quota==null?"Лимиты недоступны":quota.Label+": "+quota.Percent+" осталось")+"\n"+(LastError??"Нажмите для подробностей. Перетаскивайте для настройки положения.");
            tooltip.SetToolTip(this,tip);
            tray.Text=("Codex Limits — "+(quota==null?"нет данных":quota.Percent+" осталось")+(Stale?" (старые данные)":""));
            if(details!=null&&!details.IsDisposed)details.RefreshView();
            if(InitialDetails) { InitialDetails=false;ToggleDetails(); }
            RepaintBadge();
        }
        public void ToggleDetails() {
            if(details!=null&&!details.IsDisposed&&details.Visible) { details.Hide(); return; }
            if(details==null||details.IsDisposed)details=new DetailsForm(this);
            details.RefreshView();
            PositionDetails();details.Show(); details.Activate();
        }
        void PositionDetails() {
            Rectangle screen=Screen.FromPoint(BadgeVisible?Location:Cursor.Position).WorkingArea;
            Point origin=BadgeVisible?new Point(Left,Top-details.Height-8):new Point(Cursor.Position.X-details.Width,Cursor.Position.Y-details.Height);
            details.Location=new Point(Math.Max(screen.Left,Math.Min(screen.Right-details.Width,origin.X)),Math.Max(screen.Top,Math.Min(screen.Bottom-details.Height,origin.Y)));
        }
        protected override void OnPaintBackground(PaintEventArgs e) {if(preview)base.OnPaintBackground(e);}
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);if(preview)DrawBadge(e.Graphics);else RepaintBadge();
        }
        public Bitmap RenderBadge() {
            var bitmap=new Bitmap(Math.Max(1,Width),Math.Max(1,Height),System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using(var g=Graphics.FromImage(bitmap)) {g.Clear(Color.Transparent);DrawBadge(g);}return bitmap;
        }
        void RepaintBadge() {
            if(preview){Invalidate();return;}
            if(!IsHandleCreated||closing)return;
            var quota=Data==null?null:Data.MainWindow;
            string nextKey=Width+"|"+Height+"|"+ScaleFactor+"|"+BadgeText()+"|"+Stale+"|"+(quota==null?null:quota.Remaining);
            if(LayeredOk&&paintKey==nextKey)return;
            using(var bitmap=RenderBadge())LayeredOk=LayeredWindow.Present(Handle,bitmap,Location);
            if(LayeredOk){paintKey=nextKey;paintCount++;}
        }
        string BadgeText() {
            var quota=Data==null?null:Data.MainWindow;double s=ScaleFactor;
            string text=Data==null?(LastError==null?"Лимиты · загрузка…":"Лимиты · нет связи"):quota==null?"Лимиты · нет данных":
                (Stale?"~":"")+quota.Label+" "+quota.Percent+"  ·  "+(Width>240*s?"сброс ":"")+quota.Countdown(DateTimeOffset.UtcNow);
            if(Data!=null&&(Data.UsageAllowed==false||(Data.Main!=null&&Data.Main.Blocked)))text="Лимит доступа достигнут";
            return text;
        }
        void DrawBadge(Graphics g) {
            g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            double s=ScaleFactor;var quota=Data==null?null:Data.MainWindow;
            Color accent=Stale?Theme.Amber:Theme.QuotaColor(quota==null?null:quota.Remaining);
            int d=(int)(12*s),x=(int)(9*s),y=(Height-d)/2;
            using(var pen=new Pen(Color.FromArgb(130,130,130),(float)(2*s)))g.DrawEllipse(pen,x,y,d,d);
            if(quota!=null&&quota.Remaining.HasValue)using(var pen=new Pen(accent,(float)(2*s)))g.DrawArc(pen,x,y,d,d,-90,(float)(3.6*quota.Remaining.Value));
            Theme.InlineText(g,BadgeText(),(float)(12*s),new Rectangle(x+d+(int)(8*s),0,Width-x-d-(int)(14*s),Height));
        }
    }
    sealed class DetailCanvas : Control {
        public readonly Widget Widget;
        public DetailCanvas(Widget widget) { Widget=widget;DoubleBuffered=true;BackColor=Theme.Surface;AccessibleRole=AccessibleRole.StaticText; }
        public int ContentHeight { get {
            int h=68;var data=Widget.Data;
            if(data!=null) { foreach(var pool in data.Pools)h+=28+Math.Max(1,pool.Windows.Count)*93;h+=88; }
            else h+=76;
            if(Widget.LastError!=null)h+=45;
            return (int)(h*Widget.ScaleFactor);
        } }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);Render(e.Graphics);
        }
        public void Render(Graphics g) {
            double s=Widget.ScaleFactor;
            g.ScaleTransform((float)s,(float)s);int w=(int)(Width/s), y=18;
            Theme.TextAt(g,"Лимиты Codex",18,Theme.Text,new Rectangle(20,y,w-40,27),true);y+=29;
            Theme.TextAt(g,"Общие для всех чатов аккаунта",11,Theme.Muted,new Rectangle(20,y,w-40,18),false);y+=31;
            var data=Widget.Data;
            if(data==null) { Theme.TextAt(g,Widget.LastError==null?"Получаем актуальные данные…":"Не удалось получить лимиты",12,Theme.Amber,new Rectangle(20,y,w-40,25),false);y+=76; }
            else {
                foreach(var pool in data.Pools) {
                    Theme.TextAt(g,pool.Name??(pool.Id=="codex"?"Codex":pool.Id??"Лимит"),11,Theme.Muted,new Rectangle(20,y,w-40,20),true);y+=27;
                    if(pool.Windows.Count==0) { Theme.TextAt(g,"Окна лимитов не предоставлены",12,Theme.Muted,new Rectangle(20,y,w-40,28),false);y+=93; }
                    foreach(var quota in pool.Windows) {
                        Color color=Theme.QuotaColor(quota.Remaining);
                        Theme.TextAt(g,quota.Label,14,Theme.Text,new Rectangle(20,y,w-155,26),true);
                        Theme.TextAt(g,quota.Percent+" осталось",14,color,new Rectangle(w-147,y,127,26),true);y+=32;
                        using(var brush=new SolidBrush(Color.FromArgb(77,77,77)))g.FillRectangle(brush,20,y,w-40,5);
                        if(quota.Remaining.HasValue)using(var brush=new SolidBrush(color))g.FillRectangle(brush,20,y,(float)((w-40)*quota.Remaining.Value/100),5);y+=13;
                        Theme.TextAt(g,"Сброс: "+quota.ResetMoscow,11,Theme.Muted,new Rectangle(20,y,w-40,18),false);y+=19;
                        Theme.TextAt(g,"Через "+quota.Countdown(DateTimeOffset.UtcNow),11,Theme.Muted,new Rectangle(20,y,w-40,18),false);y+=29;
                    }
                }
                using(var pen=new Pen(Color.FromArgb(75,75,75)))g.DrawLine(pen,20,y,w-20,y);y+=13;
                var main=data.Main;string balance="Не предоставлено";
                if(main!=null) {
                    decimal number;
                    if(main.UnlimitedCredits)balance="Без ограничений";
                    else if(decimal.TryParse(main.CreditBalance,NumberStyles.Number,CultureInfo.InvariantCulture,out number))balance=number.ToString("N2",CultureInfo.GetCultureInfo("ru-RU"));
                }
                Theme.TextAt(g,"Кредиты",12,Theme.Muted,new Rectangle(20,y,120,22),false);
                Theme.TextAt(g,balance,12,Theme.Text,new Rectangle(150,y,w-170,22),true);y+=26;
                Theme.TextAt(g,"Полных сбросов",12,Theme.Muted,new Rectangle(20,y,140,22),false);
                Theme.TextAt(g,data.ResetsAvailable.HasValue?data.ResetsAvailable.Value.ToString():"—",12,Theme.Text,new Rectangle(170,y,w-190,22),true);y+=35;
            }
            if(Widget.LastError!=null) { Theme.TextAt(g,"Не обновлено · показаны последние данные",10,Theme.Amber,new Rectangle(20,y,w-40,22),false);y+=22; Theme.TextAt(g,"Проверьте подключение и вход в Codex",10,Theme.Muted,new Rectangle(20,y,w-40,18),false); }
        }
    }
    public sealed class DetailsForm : Form {
        readonly Widget widget;readonly Panel scroll,footer;readonly DetailCanvas canvas;readonly Label updated;readonly Button refresh,close;
        public DetailsForm(Widget widget) {
            this.widget=widget;FormBorderStyle=FormBorderStyle.None;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;TopMost=true;
            StartPosition=FormStartPosition.Manual;BackColor=Theme.Surface;ForeColor=Theme.Text;Text="Лимиты Codex";AutoScaleMode=AutoScaleMode.None;
            scroll=new Panel { AutoScroll=true,Dock=DockStyle.Fill,BackColor=Theme.Surface };
            canvas=new DetailCanvas(widget);scroll.Controls.Add(canvas);
            footer=new Panel { Dock=DockStyle.Bottom,Height=(int)(62*widget.ScaleFactor),BackColor=Theme.Surface };
            updated=new Label { AutoSize=false,ForeColor=Theme.Muted,TextAlign=ContentAlignment.MiddleLeft };
            refresh=new Button { Text="Обновить",FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(62,62,62),ForeColor=Theme.Text,Cursor=Cursors.Hand,AccessibleName="Обновить лимиты" };
            refresh.FlatAppearance.BorderColor=Color.FromArgb(98,98,98);refresh.Click+=delegate {widget.RefreshLimits();};
            footer.Controls.Add(updated);footer.Controls.Add(refresh);Controls.Add(scroll);Controls.Add(footer);
            close=new Button { Text="×",FlatStyle=FlatStyle.Flat,ForeColor=Theme.Muted,BackColor=Theme.Surface,AccessibleName="Закрыть подробности",TabIndex=1 };
            close.FlatAppearance.BorderSize=0;close.Click+=delegate {Hide();};Controls.Add(close);close.BringToFront();
            KeyPreview=true;KeyDown+=delegate(object sender,KeyEventArgs e) {if(e.KeyCode==Keys.Escape)Hide();};
            Deactivate+=delegate { if(!Program.Rendering&&!Program.KeepDetails)Hide(); };
            RefreshView();
        }
        public void RefreshView() {
            double s=widget.ScaleFactor;var area=Screen.FromPoint(Location).WorkingArea;int width=Math.Min((int)(374*s),area.Width-32),footerHeight=(int)(62*s);
            int available=area.Height-(int)(100*s);
            int height=Math.Min((int)(490*s),Math.Min(available,canvas.ContentHeight+footerHeight));
            ClientSize=new Size(width,Math.Max((int)(230*s),height));
            using(var outline=Theme.Rounded(ClientRectangle,(int)(12*s))) {var oldRegion=Region;Region=new Region(outline);if(oldRegion!=null)oldRegion.Dispose();}
            canvas.Size=new Size(width-SystemInformation.VerticalScrollBarWidth,canvas.ContentHeight);
            footer.Height=footerHeight;
            close.Bounds=new Rectangle(width-(int)(38*s),(int)(12*s),(int)(28*s),(int)(28*s));
            close.Font=new Font("Segoe UI",(float)(15*s),FontStyle.Regular,GraphicsUnit.Pixel);
            updated.Bounds=new Rectangle((int)(20*s),0,Math.Max(50,width-(int)(150*s)),footerHeight);
            if(updated.Font.Size!=(float)(10*s))updated.Font=new Font("Segoe UI",(float)(10*s),FontStyle.Regular,GraphicsUnit.Pixel);
            updated.Text=widget.Updated.HasValue?(widget.Stale?"Последние данные ":"Обновлено ")+widget.Updated.Value.ToOffset(TimeSpan.FromHours(3)).ToString("HH:mm:ss")+" МСК\nАвтообновление: 60 секунд":"Ожидаем данные Codex";
            refresh.Bounds=new Rectangle(width-(int)(112*s),(int)(15*s),(int)(92*s),(int)(31*s));
            if(refresh.Font.Size!=(float)(11*s))refresh.Font=new Font("Segoe UI",(float)(11*s),FontStyle.Regular,GraphicsUnit.Pixel);
            canvas.AccessibleName="Лимиты Codex. "+(widget.Data!=null&&widget.Data.MainWindow!=null?widget.Data.MainWindow.Percent+" осталось, сброс "+widget.Data.MainWindow.ResetMoscow+", кредитов "+(widget.Data.Main==null?"неизвестно":widget.Data.Main.CreditBalance)+", полных сбросов "+widget.Data.ResetsAvailable:"Нет данных");
            canvas.Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);using(var pen=new Pen(Color.FromArgb(79,79,79)))e.Graphics.DrawRectangle(pen,0,0,ClientSize.Width-1,ClientSize.Height-1);
        }
        public Bitmap RenderPreview() {
            var bitmap=new Bitmap(ClientSize.Width,ClientSize.Height);
            using(var g=Graphics.FromImage(bitmap)) {
                g.Clear(Color.Transparent);
                using(var outline=Theme.Rounded(new Rectangle(0,0,ClientSize.Width-1,ClientSize.Height-1),(int)(12*widget.ScaleFactor))) {
                    g.SmoothingMode=SmoothingMode.AntiAlias;using(var fill=new SolidBrush(Theme.Surface))g.FillPath(fill,outline);g.SetClip(outline);
                }
                var state=g.Save();canvas.Render(g);g.Restore(state);
                double s=widget.ScaleFactor;g.ScaleTransform((float)s,(float)s);int width=(int)(ClientSize.Width/s),y=(int)(ClientSize.Height/s)-62;
                using(var font=new Font("Segoe UI",10,FontStyle.Regular,GraphicsUnit.Pixel))using(var brush=new SolidBrush(Theme.Muted))
                    g.DrawString(updated.Text,font,brush,new Rectangle(20,y+15,width-150,42));
                using(var brush=new SolidBrush(refresh.BackColor))g.FillRectangle(brush,width-112,y+15,92,31);
                Theme.TextAt(g,"Обновить",11,Theme.Text,new Rectangle(width-102,y+15,82,31),false);
                Theme.TextAt(g,"×",18,Theme.Muted,new Rectangle(width-37,12,28,28),false);
                g.ResetTransform();
                g.ResetClip();
            }
            return bitmap;
        }
    }
}
