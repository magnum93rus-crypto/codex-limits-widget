using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CodexLimits {
    public class Anchor {
        public IntPtr Window;
        public Rectangle Bounds, HostBounds;
        public double Scale=1;
        public bool Exact;
        public string Status;
        public int Elements,Permissions,Models;
        public bool VoiceActive,TextReference;
        public bool NeedsRecheck,ConfirmedHidden;
        public Rectangle LeftReference,RightReference;
        public Anchor Copy(){return (Anchor)MemberwiseClone();}
    }
    public class AnchorStability {
        Anchor lastValid;
        DateTimeOffset lastConfirmed;
        public Anchor Resolve(Anchor observed,DateTimeOffset now){
            if(observed.VoiceActive||observed.ConfirmedHidden||observed.Window==IntPtr.Zero){lastValid=null;return observed;}
            if(!observed.Bounds.IsEmpty&&!observed.NeedsRecheck){lastValid=observed.Copy();lastConfirmed=now;return observed;}
            if(lastValid!=null&&lastValid.Window==observed.Window&&now-lastConfirmed<TimeSpan.FromSeconds(1)){
                var retained=lastValid.Copy();retained.NeedsRecheck=true;retained.Status="Повторный поиск элементов интерфейса";return retained;
            }
            return observed;
        }
    }
    public static class Native {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; public Rectangle Rectangle { get { return Rectangle.FromLTRB(Left,Top,Right,Bottom); } } }
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window,out RECT rect);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window,uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window,uint command);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
        [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr window,int index,IntPtr value);
        public static List<IntPtr> HostWindows(IntPtr known=default(IntPtr)) {
            var windows = new List<IntPtr>();
            IntPtr active=GetAncestor(GetForegroundWindow(),2);uint pid;
            GetWindowThreadProcessId(active,out pid);
            if(known!=IntPtr.Zero&&IsWindow(known)&&IsWindowVisible(known)&&!IsIconic(known)) {
                uint hostPid;GetWindowThreadProcessId(known,out hostPid);
                if(active!=known&&pid==hostPid&&IsWindowVisible(active)&&!IsIconic(active))windows.Add(active);
                windows.Add(known);return windows;
            }
            try {using(var process=Process.GetProcessById((int)pid)) {
                if((process.ProcessName=="ChatGPT"||process.ProcessName.Equals("Codex",StringComparison.OrdinalIgnoreCase))&&IsWindowVisible(active)&&!IsIconic(active))windows.Add(active);
            }}catch(ArgumentException) { }
            foreach (string name in new[] {"ChatGPT","Codex"}) foreach (var p in Process.GetProcessesByName(name)) {
                using (p) try {
                    IntPtr handle = p.MainWindowHandle;
                    if (handle != IntPtr.Zero && IsWindowVisible(handle) && !IsIconic(handle) && !windows.Contains(handle)) windows.Add(handle);
                } catch (InvalidOperationException) { }
            }
            return windows;
        }
        internal static bool IsPermission(string n) {
            return n!=null&&n.Length<100&&(n.IndexOf("Полный доступ",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("Full access",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("Default permissions",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("Разрешения",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("permissions",StringComparison.OrdinalIgnoreCase)>=0);
        }
        internal static bool IsModel(string n) {
            return n!=null&&n.Length<160&&(n.StartsWith("GPT-",StringComparison.OrdinalIgnoreCase)||n.StartsWith("Выбрать модель",StringComparison.OrdinalIgnoreCase)||n.StartsWith("Select model",StringComparison.OrdinalIgnoreCase));
        }
        [ThreadStatic] static FooterReader reader;
        public static void ReleaseAutomation() {if(reader!=null){reader.Dispose();reader=null;}}
        public static Anchor Refresh(Anchor previous,Func<Anchor> reacquire=null) {return reacquire!=null?reacquire():Locate(previous);}
        public static Anchor Locate() {return Locate(null);}
        public static Anchor Locate(Anchor previous) {
            var handles=HostWindows(previous==null?IntPtr.Zero:previous.Window);
            IntPtr active=GetAncestor(GetForegroundWindow(),2);
            handles.Sort((a,b)=>(a==active?-1:b==active?1:0));
            foreach(var handle in handles) {
                RECT rect;if(!GetWindowRect(handle,out rect))continue;
                double scale=GetDpiForWindow(handle)/96d;if(scale<=0)scale=1;
                if(reader==null)reader=new FooterReader();
                for(int attempt=0;attempt<2;attempt++) {
                    try {
                        var result=reader.Read(handle,rect.Rectangle,scale,attempt>0||(previous!=null&&previous.NeedsRecheck));
                        if(!result.NeedsRecheck||attempt==1)return result;
                    }catch(COMException){reader.ResetFooter();}
                }
                return new Anchor {Window=handle,NeedsRecheck=true,Status="Повторный поиск строки ввода"};
            }
            return new Anchor {Status="Окно Codex не найдено"};
        }
    }
}
