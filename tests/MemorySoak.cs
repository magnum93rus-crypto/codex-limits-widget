using System;
using System.Diagnostics;
using System.Threading;
using CodexLimits;
class MemorySoak {
    static long PrivateBytes() {using(var p=Process.GetCurrentProcess())return p.PrivateMemorySize64;}
    [MTAThread] static void Main(string[] args) {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        int count=args.Length==0?3000:int.Parse(args[0]);
        Anchor previous=null;long baseline=0;int valid=0;
        var clock=Stopwatch.StartNew();
        for(int i=0;i<count;i++) {
            previous=Native.Locate(previous);
            if(!previous.Bounds.IsEmpty)valid++;
            if(i==99)baseline=PrivateBytes();
            if(i%500==499)using(var p=Process.GetCurrentProcess())Console.WriteLine(Json.Serializer.Serialize(new {scans=i+1,seconds=Math.Round(clock.Elapsed.TotalSeconds,1),valid=valid,footerElements=previous.Elements,privateMB=Math.Round(p.PrivateMemorySize64/1048576d,2),workingMB=Math.Round(p.WorkingSet64/1048576d,2),managedMB=Math.Round(GC.GetTotalMemory(false)/1048576d,2),gen2Collections=GC.CollectionCount(2)}));
        }
        long finalBytes=PrivateBytes();Native.ReleaseAutomation();
        if(valid<count*.95){Console.WriteLine("INCONCLUSIVE: composer unavailable for more than 5 percent of checks");Environment.ExitCode=2;return;}
        if(finalBytes>96*1048576L||finalBytes-baseline>16*1048576L){Console.WriteLine("FAIL memory budget exceeded");Environment.ExitCode=1;}
        else Console.WriteLine("PASS bounded footer scanning memory");
    }
}
