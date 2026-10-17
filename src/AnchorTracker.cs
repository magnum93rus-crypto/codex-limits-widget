using System;
using System.Threading;

namespace CodexLimits {
    // UI Automation COM references stay in one MTA, rather than moving through pool threads.
    public sealed class AnchorTracker : IDisposable {
        readonly AutoResetEvent wake=new AutoResetEvent(false);
        readonly object gate=new object();
        readonly Thread worker;
        Anchor latest;
        volatile bool stopping;
        public AnchorTracker() {
            worker=new Thread(Run) {IsBackground=true,Name="Codex footer tracker"};
            worker.SetApartmentState(ApartmentState.MTA);worker.Start();
        }
        public Anchor Read() {lock(gate)return latest;}
        public void Request() {if(!stopping)wake.Set();}
        void Run() {
            var stability=new AnchorStability();Anchor previous=null;
            try {
                while(true) {
                    wake.WaitOne();if(stopping)return;
                    Anchor found;
                    try {found=Native.Locate(previous);}
                    catch(Exception) {found=new Anchor {Window=previous==null?IntPtr.Zero:previous.Window,NeedsRecheck=true,Status="Повторный поиск строки ввода"};}
                    previous=stability.Resolve(found,DateTimeOffset.UtcNow);
                    lock(gate)latest=previous;
                }
            }finally {Native.ReleaseAutomation();wake.Dispose();}
        }
        public void Dispose() {stopping=true;wake.Set();}
    }
}
