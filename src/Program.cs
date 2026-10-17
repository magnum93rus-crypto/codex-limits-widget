using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace CodexLimits {
    static class Program {
        public static string DataDirectory;
        public static bool Rendering;
        public static bool KeepDetails;
        public static EventWaitHandle ShowRequest,QuitRequest;
        static string Option(string[] args,string name) { int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null; }
        [STAThread] static void Main(string[] args) {
            string taskWork=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","work","codex-limits-widget"));
            DataDirectory=Option(args,"--data-dir")??(Directory.Exists(taskWork)?Path.Combine(taskWork,"runtime"):Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"work"));
            Directory.CreateDirectory(DataDirectory);
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }catch(EntryPointNotFoundException) { }
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            string probe=Option(args,"--probe"),diagnose=Option(args,"--diagnose"),render=Option(args,"--render");
            try {
                if(probe!=null) {
                    using(var client=new CodexClient()) {
                        var data=client.Read();
                        File.WriteAllText(probe,Json.Serializer.Serialize(new { success=true, remaining=data.MainWindow==null?null:data.MainWindow.Remaining,
                            windows=data.Main==null?null:data.Main.Windows, credits=data.Main==null?null:data.Main.CreditBalance, resets=data.ResetsAvailable }));
                    }
                    return;
                }
                if(diagnose!=null) {
                    var anchor=Native.Locate();
                    File.WriteAllText(diagnose,Json.Serializer.Serialize(new { exact=anchor.Exact,status=anchor.Status,window=anchor.Window.ToInt64(),bounds=anchor.Bounds,scale=anchor.Scale,elements=anchor.Elements,permissions=anchor.Permissions,models=anchor.Models,leftReference=anchor.LeftReference,rightReference=anchor.RightReference,textReference=anchor.TextReference,dictation=anchor.VoiceActive }));return;
                }
                if(render!=null) {
                    Rendering=true;
                    using(var widget=new Widget(true)) {
                        double scale;string scaleOption=Option(args,"--scale");
                        if(scaleOption!=null&&double.TryParse(scaleOption,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out scale)) {
                            widget.ScaleFactor=Math.Max(.5,Math.Min(3.5,scale));widget.Size=new Size((int)(280*widget.ScaleFactor),(int)(26*widget.ScaleFactor));
                        }
                        string fixture=Option(args,"--fixture");
                        if(fixture!=null)widget.Data=Snapshot.Parse(File.ReadAllText(fixture));
                        widget.Updated=DateTimeOffset.UtcNow;
                        if(args.Contains("--stale"))widget.LastError="Проверка устаревших данных";
                        widget.CreateControl();
                        using(var popup=new DetailsForm(widget))using(var bitmap=new Bitmap((int)(414*widget.ScaleFactor),popup.Height+(int)(90*widget.ScaleFactor)))using(var g=Graphics.FromImage(bitmap)) {
                            g.Clear(Color.FromArgb(29,29,29));
                            using(var badge=widget.RenderBadge()) {badge.Save(Path.ChangeExtension(render,"badge.png"),System.Drawing.Imaging.ImageFormat.Png);g.DrawImage(badge,(int)(20*widget.ScaleFactor),(int)(18*widget.ScaleFactor));}
                            using(var detail=popup.RenderPreview()) {g.DrawImage(detail,(int)(20*widget.ScaleFactor),(int)(62*widget.ScaleFactor));}
                            bitmap.Save(render,System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    return;
                }
                bool created;
                using(var mutex=new Mutex(true,"Local\\CodexLimitsWidget",out created))
                using(var show=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\CodexLimitsWidgetShow"))
                using(var quit=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\CodexLimitsWidgetQuit")) {
                    ShowRequest=show;QuitRequest=quit;
                    if(args.Contains("--quit")){quit.Set();return;}
                    if(!created){show.Set();return;}
                    KeepDetails=args.Contains("--keep-details");
                    using(var widget=new Widget(false)) {
                        widget.InitialDetails=args.Contains("--details");
                        IntPtr handle=widget.Handle;
                        Application.Run();
                    }
                }
            } catch(Exception e) {
                string destination=probe??diagnose??Path.Combine(DataDirectory,"error.json");
                File.WriteAllText(destination,Json.Serializer.Serialize(new { success=false,error=e.Message }));
                Environment.ExitCode=1;
            }
        }
    }
}
