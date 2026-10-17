using System;
using System.Collections.Generic;
using System.Drawing;
using CodexLimits;

class ModelTests {
    static int failures, count;
    static void Test(string name, Action check) {
        count++;
        try { check(); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
    static void Equal(object actual, object expected) {
        if (!object.Equals(actual, expected)) throw new Exception("Expected " + expected + ", got " + actual);
    }
    static void Main() {
        Test("live response is accepted", delegate { Snapshot.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":8,\"windowDurationMins\":10080,\"resetsAt\":1792817648}}}"); });
        Test("8 percent used means 92 remaining", delegate { Equal(QuotaWindow.Parse(Json.Serializer.DeserializeObject("{\"usedPercent\":8}")).Remaining, 92d); });
        Test("missing percentage stays unknown", delegate { Equal(QuotaWindow.Parse(Json.Serializer.DeserializeObject("{}")).Remaining, null); });
        Test("null secondary is absent", delegate { Equal(Snapshot.Parse("{\"rateLimits\":{\"primary\":null,\"secondary\":null}}").Main.Windows.Count,0); });
        Test("weekly primary is labelled by duration", delegate { Equal(QuotaWindow.Parse(Json.Serializer.DeserializeObject("{\"windowDurationMins\":10080}")).Label,"Неделя"); });
        Test("5 hour secondary is labelled by duration", delegate { Equal(QuotaWindow.Parse(Json.Serializer.DeserializeObject("{\"windowDurationMins\":300}")).Label,"5 ч"); });
        Test("bucket map takes priority over legacy", delegate { Equal(Snapshot.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":1}},\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":70}}}}").MainWindow.Remaining,30d); });
        Test("most restrictive window appears in badge", delegate { Equal(Snapshot.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":8},\"secondary\":{\"usedPercent\":90}}}").MainWindow.Remaining,10d); });
        Test("unknown quota never becomes 100 percent", delegate { Equal(Snapshot.Parse("{}").MainWindow,null); });
        Test("remaining clamps to zero", delegate { Equal(QuotaWindow.Parse(Json.Serializer.DeserializeObject("{\"usedPercent\":120}")).Remaining,0d); });
        Test("remaining clamps to 100", delegate { Equal(QuotaWindow.Parse(Json.Serializer.DeserializeObject("{\"usedPercent\":-5}")).Remaining,100d); });
        Test("past reset never fabricates renewed quota", delegate {
            var w=QuotaWindow.Parse(Json.Serializer.DeserializeObject("{\"usedPercent\":100,\"resetsAt\":1}"));
            Equal(w.Countdown(DateTimeOffset.UtcNow),"ожидаем обновление"); Equal(w.Remaining,0d);
        });
        Test("missing reset count stays unknown", delegate { Equal(Snapshot.Parse("{}").ResetsAvailable,null); });
        Test("reset count uses authoritative total", delegate { Equal(Snapshot.Parse("{\"rateLimitResetCredits\":{\"availableCount\":3,\"credits\":[]}}").ResetsAvailable,3); });
        Test("missing duration stays unknown", delegate { Equal(QuotaWindow.Parse(Json.Serializer.DeserializeObject("{}")).Label,"Период неизвестен"); });
        Test("badge stays inside actual control gap", delegate { var r=Placement.Between(new Rectangle(30,100,120,32),new Rectangle(465,100,180,32),1); Equal(r.Left>=158 && r.Right<=457,true); });
        Test("narrow gap hides badge rather than covering model", delegate { Equal(Placement.Between(new Rectangle(30,100,120,32),new Rectangle(230,100,180,32),1),Rectangle.Empty); });
        Test("controls on different rows cannot anchor badge", delegate { Equal(Placement.Between(new Rectangle(30,100,120,32),new Rectangle(465,200,180,32),1),Rectangle.Empty); });
        Test("reset timestamp displays Moscow time", delegate { Equal(QuotaWindow.Parse(Json.Serializer.DeserializeObject("{\"resetsAt\":1}")).ResetMoscow,"01.01.1970, 03:00 МСК"); });
        Test("dictation hides badge even if footer still exists", delegate { Equal(Placement.VisibleBadge(new Rectangle(30,100,120,32),new Rectangle(465,100,180,32),1,true),Rectangle.Empty); });
        Test("idle microphone does not hide badge", delegate { Equal(Placement.DictationControl("Начать диктовку"),false);Equal(Placement.DictationControl("Start dictation"),false); });
        Test("active dictation controls hide badge in both languages", delegate { Equal(Placement.DictationControl("Остановить диктовку"),true);Equal(Placement.DictationControl("Cancel dictation"),true); });
        Test("chat text mentioning dictation is not a recording control", delegate {Equal(Placement.DictationControl("Нажмите кнопку Остановить диктовку"),false);});
        Test("UI zoom scales text from model label rather than OS DPI", delegate {Equal(Placement.InferScale(34,true,1),2d);Equal(Placement.InferScale(17,true,2),1d);});
        Test("unknown text height uses display DPI", delegate {Equal(Placement.InferScale(0,true,1.25),1.25d);});
        Test("200 percent zoom badge fits scaled footer gap", delegate {var r=Placement.VisibleBadge(new Rectangle(60,200,240,64),new Rectangle(930,200,360,64),2,false);Equal(r.Height,52);Equal(r.Left>=316&&r.Right<=914,true);});
        Test("temporary focus on badge never creates hide-show oscillation",delegate {
            foreach(long foreground in new long[]{266218,1906112,266218,1906112,266218})Equal(Placement.HostContext(foreground,266218,1906112,false,false),true);
        });
        Test("foreground diagnostic distinguishes another app",delegate {Equal(Placement.HostContext(999,266218,1906112,false,false),false);});
        Test("missing host never authorizes showing badge",delegate {Equal(Placement.HostContext(1906112,0,1906112,true,true),false);});
        Test("temporary UI tree replacement preserves confirmed placement",delegate {
            var memory=new AnchorStability();var now=DateTimeOffset.UtcNow;var bounds=new Rectangle(200,100,280,26);
            memory.Resolve(new Anchor {Window=new IntPtr(1),Bounds=bounds,Exact=true},now);
            Equal(memory.Resolve(new Anchor {Window=new IntPtr(1),NeedsRecheck=true},now.AddMilliseconds(250)).Bounds,bounds);
            Equal(memory.Resolve(new Anchor {Window=new IntPtr(1),NeedsRecheck=true},now.AddMilliseconds(500)).Bounds,bounds);
        });
        Test("persistent footer loss eventually hides rather than floating",delegate {
            var memory=new AnchorStability();var now=DateTimeOffset.UtcNow;
            memory.Resolve(new Anchor {Window=new IntPtr(1),Bounds=new Rectangle(200,100,280,26)},now);
            Equal(memory.Resolve(new Anchor {Window=new IntPtr(1),NeedsRecheck=true},now.AddMilliseconds(1200)).Bounds,Rectangle.Empty);
        });
        Test("dictation immediately clears remembered placement",delegate {
            var memory=new AnchorStability();var now=DateTimeOffset.UtcNow;
            memory.Resolve(new Anchor {Window=new IntPtr(1),Bounds=new Rectangle(200,100,280,26)},now);
            Equal(memory.Resolve(new Anchor {Window=new IntPtr(1),VoiceActive=true},now.AddMilliseconds(10)).Bounds,Rectangle.Empty);
            Equal(memory.Resolve(new Anchor {Window=new IntPtr(1),NeedsRecheck=true},now.AddMilliseconds(100)).Bounds,Rectangle.Empty);
        });
        Test("another host cannot inherit a remembered location",delegate {
            var memory=new AnchorStability();var now=DateTimeOffset.UtcNow;
            memory.Resolve(new Anchor {Window=new IntPtr(1),Bounds=new Rectangle(200,100,280,26)},now);
            Equal(memory.Resolve(new Anchor {Window=new IntPtr(2),NeedsRecheck=true},now.AddMilliseconds(10)).Bounds,Rectangle.Empty);
        });
        Test("background Codex keeps its indicator until the host is minimized",delegate {
            Equal(Placement.ShowForHost(123,false,false,false),true);
            Equal(Placement.ShowForHost(123,true,false,false),false);
            Equal(Placement.ShowForHost(123,false,true,false),false);
            Equal(Placement.ShowForHost(123,false,false,true),false);
            Equal(Placement.ShowForHost(0,false,false,false),false);
        });
        Test("stale UI references are reacquired before changing visibility",delegate {
            var restored=new Anchor {Window=new IntPtr(1),Bounds=new Rectangle(200,100,280,26)};
            Equal(Native.Refresh(new Anchor {Window=new IntPtr(1)},()=>restored).Bounds,restored.Bounds);
        });
        Test("reacquiring a dictation footer never reuses the old badge",delegate {
            Equal(Native.Refresh(new Anchor {Window=new IntPtr(1)},()=>new Anchor {VoiceActive=true}).VoiceActive,true);
        });
        Console.WriteLine(count + " checks, " + failures + " failures");
        Environment.ExitCode = failures == 0 ? 0 : 1;
    }
}
