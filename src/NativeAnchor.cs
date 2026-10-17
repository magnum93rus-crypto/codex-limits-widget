using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace CodexLimits {
    public class Anchor {
        public IntPtr Window;
        public Rectangle Bounds, HostBounds;
        public double Scale=1;
        public bool Exact;
        public string Status;
        public int Elements,Permissions,Models;
        public bool VoiceActive,TextReference;
        public Rectangle LeftReference,RightReference;
        public AutomationElement PermissionElement,ModelElement;
        public Anchor Copy(){return (Anchor)MemberwiseClone();}
    }
    public static class Native {
        class Reference {public AutomationElement Element;public Rectangle Bounds;public bool Text;}
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; public Rectangle Rectangle { get { return Rectangle.FromLTRB(Left,Top,Right,Bottom); } } }
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window,out RECT rect);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window,uint flags);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
        [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr window,int index,IntPtr value);
        public static List<IntPtr> HostWindows() {
            var windows = new List<IntPtr>();
            IntPtr active=GetAncestor(GetForegroundWindow(),2);uint pid;
            GetWindowThreadProcessId(active,out pid);
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
        static Rectangle ToRectangle(System.Windows.Rect rect) {
            if (rect.IsEmpty || double.IsInfinity(rect.Width)) return Rectangle.Empty;
            return Rectangle.FromLTRB((int)rect.Left,(int)rect.Top,(int)rect.Right,(int)rect.Bottom);
        }
        static bool IsPermission(string n) {
            return n!=null&&n.Length<100&&(n.IndexOf("Полный доступ",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("Full access",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("Default permissions",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("Разрешения",StringComparison.OrdinalIgnoreCase)>=0);
        }
        static bool IsModel(string n) {
            return n!=null&&n.Length<160&&(n.StartsWith("GPT-",StringComparison.OrdinalIgnoreCase)||n.StartsWith("Выбрать модель",StringComparison.OrdinalIgnoreCase)||n.StartsWith("Select model",StringComparison.OrdinalIgnoreCase));
        }
        public static Anchor Refresh(Anchor previous) {
            var anchor=previous.Copy();anchor.Bounds=Rectangle.Empty;
            try {
                if(anchor.ModelElement==null||anchor.PermissionElement==null){anchor.Status="refresh: missing references";return anchor;}
                var left=anchor.PermissionElement.Current;var right=anchor.ModelElement.Current;
                if(left.IsOffscreen||right.IsOffscreen){anchor.Status="refresh: offscreen";return anchor;}
                if(!IsPermission(left.Name)||!IsModel(right.Name)){anchor.Status="refresh: changed names";return anchor;}
                anchor.LeftReference=ToRectangle(left.BoundingRectangle);anchor.RightReference=ToRectangle(right.BoundingRectangle);
                anchor.Scale=Placement.InferScale(anchor.RightReference.Height,anchor.TextReference,GetDpiForWindow(anchor.Window)/96d);
                anchor.Bounds=Placement.VisibleBadge(anchor.LeftReference,anchor.RightReference,anchor.Scale,anchor.VoiceActive);
                RECT rect;if(GetWindowRect(anchor.Window,out rect))anchor.HostBounds=rect.Rectangle;
            }catch(ElementNotAvailableException) {anchor.Status="refresh: element unavailable";}catch(COMException e) {anchor.Status="refresh: COM "+e.ErrorCode;}catch(InvalidOperationException e) {anchor.Status="refresh: "+e.Message;}
            return anchor;
        }
        public static Anchor Locate() {
            var handles = HostWindows(); IntPtr active = GetAncestor(GetForegroundWindow(),2);
            handles.Sort((a,b) => (a==active ? -1 : b==active ? 1 : 0));
            foreach (var handle in handles) {
                RECT rect; if (!GetWindowRect(handle,out rect)) continue;
                double scale=GetDpiForWindow(handle)/96d; if (scale<=0) scale=1;
                var anchor = new Anchor { Window=handle,HostBounds=rect.Rectangle,Scale=scale,Status="Обычная строка ввода скрыта" };
                try {
                    var root = AutomationElement.FromHandle(handle);
                    AutomationElementCollection buttons;
                    var cache=new CacheRequest();
                    cache.Add(AutomationElement.NameProperty);cache.Add(AutomationElement.IsOffscreenProperty);cache.Add(AutomationElement.BoundingRectangleProperty);cache.Add(AutomationElement.ControlTypeProperty);
                    using(cache.Activate()) buttons = root.FindAll(TreeScope.Descendants,new OrCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Text),
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ComboBox)));
                    anchor.Elements=buttons.Count;
                    var permissions = new List<Reference>(); var models = new List<Reference>();
                    foreach (AutomationElement button in buttons) {
                        var current=button.Cached; if (current.IsOffscreen) continue;
                        var bounds=ToRectangle(current.BoundingRectangle);
                        if (bounds.IsEmpty || bounds.Top < rect.Top+(rect.Bottom-rect.Top)/2) continue;
                        string n=current.Name ?? "";
                        if(current.ControlType==ControlType.Button&&Placement.DictationControl(n))anchor.VoiceActive=true;
                        if(IsPermission(n))permissions.Add(new Reference {Element=button,Bounds=bounds,Text=current.ControlType==ControlType.Text});
                        if(IsModel(n))models.Add(new Reference {Element=button,Bounds=bounds,Text=current.ControlType==ControlType.Text});
                    }
                    anchor.Permissions=permissions.Count;anchor.Models=models.Count;
                    if(anchor.VoiceActive){anchor.Status="Диктовка — индикатор скрыт";return anchor;}
                    permissions.Sort((a,b)=>a.Text==b.Text?b.Bounds.Top.CompareTo(a.Bounds.Top):a.Text?-1:1);
                    models.Sort((a,b)=>a.Text==b.Text?b.Bounds.Top.CompareTo(a.Bounds.Top):a.Text?-1:1);
                    foreach (var left in permissions) foreach (var right in models) {
                        double effectiveScale=Placement.InferScale(right.Bounds.Height,right.Text,scale);
                        var bounds=Placement.VisibleBadge(left.Bounds,right.Bounds,effectiveScale,anchor.VoiceActive);
                        if (!bounds.IsEmpty) {
                            anchor.Bounds=bounds;anchor.Scale=effectiveScale;anchor.Exact=true;anchor.Status="Между доступом и моделью";
                            anchor.PermissionElement=left.Element;anchor.ModelElement=right.Element;anchor.LeftReference=left.Bounds;anchor.RightReference=right.Bounds;anchor.TextReference=right.Text;return anchor;
                        }
                    }
                    if (permissions.Count>0 && models.Count>0) { anchor.Status="В строке недостаточно места"; return anchor; }
                } catch (ElementNotAvailableException) { }
                  catch (COMException) { }
                  catch (InvalidOperationException) { }
                // Dictation replaces the normal footer. Never float over a replacement bar.
                return anchor;
            }
            return new Anchor { Status="Окно Codex не найдено" };
        }
    }
}
