using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using NativeUIA;

namespace CodexLimits {
    // Each returned COM reference has one owner and one explicit release, on this MTA only.
    sealed class FooterReader : IDisposable {
        const int Name=30005,ControlType=30003,Bounds=30001;
        const int Button=50000,Text=50020,ComboBox=50003,Edit=50004;
        readonly IUIAutomation automation;
        readonly IUIAutomationTreeWalker walker;
        readonly IUIAutomationCacheRequest cache;
        readonly IUIAutomationCondition controls,permissions,edit;
        IUIAutomationElement footer;
        IntPtr window;
        struct Reference {public Rectangle Bounds;public bool Text;}
        static void Release(object value) {if(value!=null)Marshal.ReleaseComObject(value);}
        static Rectangle Rect(tagRECT r) {return Rectangle.FromLTRB(r.left,r.top,r.right,r.bottom);}
        public FooterReader() {
            automation=(IUIAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E")));
            walker=automation.ControlViewWalker;
            cache=automation.CreateCacheRequest();
            cache.AddProperty(Name);cache.AddProperty(ControlType);cache.AddProperty(Bounds);
            cache.AutomationElementMode=AutomationElementMode.AutomationElementMode_None;
            controls=Or(new[]{automation.CreatePropertyCondition(ControlType,Button),automation.CreatePropertyCondition(ControlType,Text),automation.CreatePropertyCondition(ControlType,ComboBox)});
            var names=new List<IUIAutomationCondition>();
            foreach(string name in new[]{"Изменить разрешения","Change permissions","Permissions","Разрешения","Full access","Полный доступ","Default permissions"})names.Add(automation.CreatePropertyCondition(Name,name));
            var permissionNames=Or(names.ToArray());var button=automation.CreatePropertyCondition(ControlType,Button);
            try {permissions=automation.CreateAndCondition(button,permissionNames);}finally {Release(button);Release(permissionNames);}
            edit=automation.CreatePropertyCondition(ControlType,Edit);
        }
        IUIAutomationCondition Or(IUIAutomationCondition[] conditions) {
            try {return automation.CreateOrConditionFromArray(conditions);}
            finally {foreach(var condition in conditions)Release(condition);}
        }
        public void ResetFooter() {Release(footer);footer=null;window=IntPtr.Zero;}
        IUIAutomationElement FindFooter(IntPtr handle,Rectangle host) {
            var root=automation.ElementFromHandle(handle);IUIAutomationElement current=null;
            try {
                current=root.FindFirst(TreeScope.TreeScope_Descendants,permissions);
                if(current==null)return null;
                var bounds=Rect(current.CurrentBoundingRectangle);
                if(bounds.IsEmpty||bounds.Top<host.Top+host.Height/2)return null;
                for(int i=0;i<3;i++) {
                    var parent=walker.GetParentElement(current);Release(current);current=parent;if(current==null)return null;
                    var area=Rect(current.CurrentBoundingRectangle);if(area.Height>host.Height/2)return null;
                    var input=current.FindFirst(TreeScope.TreeScope_Descendants,edit);
                    if(input!=null){Release(input);var result=current;current=null;return result;}
                }
                return null;
            }finally {Release(current);Release(root);}
        }
        public Anchor Read(IntPtr handle,Rectangle host,double dpi,bool reset) {
            if(reset||handle!=window)ResetFooter();
            if(footer!=null){var area=Rect(footer.CurrentBoundingRectangle);if(area.IsEmpty||area.Top<host.Top+host.Height/2)ResetFooter();}
            if(footer==null){footer=FindFooter(handle,host);window=handle;}
            var anchor=new Anchor {Window=handle,HostBounds=host,Scale=dpi,NeedsRecheck=true,Status="Строка ввода недоступна"};
            if(footer==null)return anchor;
            var items=footer.FindAllBuildCache(TreeScope.TreeScope_Descendants,controls,cache);
            var lefts=new List<Reference>();var rights=new List<Reference>();
            try {
                anchor.Elements=items.Length;
                for(int i=0;i<items.Length;i++) {
                    var item=items.GetElement(i);
                    try {
                        var bounds=Rect(item.CachedBoundingRectangle);
                        if(bounds.IsEmpty||bounds.Top<host.Top+host.Height/2)continue;
                        string name=item.CachedName??"";int type=item.CachedControlType;
                        if(type==Button&&Placement.DictationControl(name))anchor.VoiceActive=true;
                        if(Native.IsPermission(name))lefts.Add(new Reference {Bounds=bounds,Text=type==Text});
                        if(Native.IsModel(name))rights.Add(new Reference {Bounds=bounds,Text=type==Text});
                    }finally {Release(item);}
                }
            }finally {Release(items);}
            anchor.Permissions=lefts.Count;anchor.Models=rights.Count;
            if(anchor.VoiceActive){anchor.NeedsRecheck=false;anchor.ConfirmedHidden=true;anchor.Status="Диктовка — индикатор скрыт";return anchor;}
            lefts.Sort((a,b)=>a.Text==b.Text?b.Bounds.Top.CompareTo(a.Bounds.Top):a.Text?-1:1);
            rights.Sort((a,b)=>a.Text==b.Text?b.Bounds.Top.CompareTo(a.Bounds.Top):a.Text?-1:1);
            foreach(var left in lefts)foreach(var right in rights) {
                if(Math.Abs(left.Bounds.Top+left.Bounds.Height/2-right.Bounds.Top-right.Bounds.Height/2)>24*dpi||right.Bounds.Left<left.Bounds.Right||right.Bounds.Height>128*dpi)continue;
                double scale=Placement.InferScale(right.Bounds.Height,right.Text,dpi);
                var bounds=Placement.VisibleBadge(left.Bounds,right.Bounds,scale,false);
                if(bounds.IsEmpty)continue;
                anchor.Bounds=bounds;anchor.Scale=scale;anchor.Exact=true;anchor.NeedsRecheck=false;anchor.Status="Между доступом и моделью";
                anchor.LeftReference=left.Bounds;anchor.RightReference=right.Bounds;anchor.TextReference=right.Text;return anchor;
            }
            return anchor;
        }
        public void Dispose() {ResetFooter();Release(edit);Release(permissions);Release(controls);Release(cache);Release(walker);Release(automation);}
    }
}
