using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodexLimits {
    public sealed class CodexClient : IDisposable {
        Process process;
        readonly string executable;
        public CodexClient(string executablePath=null) {executable=executablePath;}
        readonly object writeLock = new object();
        readonly ConcurrentDictionary<int,TaskCompletionSource<Dictionary<string,object>>> pending = new ConcurrentDictionary<int,TaskCompletionSource<Dictionary<string,object>>>();
        int nextId;
        public static string FindExecutable() {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
            if (Directory.Exists(root)) {
                var candidates = Directory.GetFiles(root,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
                if (candidates.Length > 0) return candidates[0];
            }
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
                try { string file = Path.Combine(directory.Trim('"'),"codex.exe"); if (File.Exists(file)) return file; } catch (ArgumentException) { }
            }
            throw new FileNotFoundException("Не найден codex.exe. Откройте Codex и повторите обновление.");
        }
        void Send(object message) {
            lock(writeLock) { process.StandardInput.WriteLine(Json.Serializer.Serialize(message)); process.StandardInput.Flush(); }
        }
        Dictionary<string,object> Request(string method, object parameters) {
            int id = Interlocked.Increment(ref nextId);
            var source = new TaskCompletionSource<Dictionary<string,object>>(); pending[id] = source;
            try {
                Send(new { id=id, method=method, @params=parameters });
                if (!source.Task.Wait(20000)) throw new TimeoutException("Codex не ответил за 20 секунд");
                var response = source.Task.Result;
                if (Json.Get(response,"error") != null) throw new InvalidOperationException("Не удалось получить лимиты. Проверьте вход в аккаунт Codex.");
                var result = Json.Map(Json.Get(response,"result"));
                if (result == null) throw new FormatException("Codex вернул ответ без данных");
                return result;
            } finally { TaskCompletionSource<Dictionary<string,object>> removed; pending.TryRemove(id,out removed); }
        }
        void Connect() {
            Dispose();
            var info = new ProcessStartInfo(executable??FindExecutable(),"app-server --stdio") {
                UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=true,
                RedirectStandardOutput=true, RedirectStandardError=true,
                StandardOutputEncoding=System.Text.Encoding.UTF8, StandardErrorEncoding=System.Text.Encoding.UTF8,
                WorkingDirectory=Program.DataDirectory
            };
            process = new Process { StartInfo=info, EnableRaisingEvents=true };
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                if (e.Data == null) return;
                try {
                    var response = Json.Map(Json.Serializer.DeserializeObject(e.Data));
                    var id = Json.Number(response,"id");
                    if (!id.HasValue) return;
                    if (Json.Get(response,"method") != null) {
                        // This client never handles tool requests or approvals.
                        Send(new { id=(int)id.Value, error=new { code=-32601, message="Read-only limits client" } }); return;
                    }
                    TaskCompletionSource<Dictionary<string,object>> source;
                    if (pending.TryGetValue((int)id.Value,out source)) source.TrySetResult(response);
                } catch (Exception) { /* Ignore non-protocol diagnostic lines. */ }
            };
            process.ErrorDataReceived += delegate { }; // Drain diagnostics without storing credentials or server logs.
            process.Exited += delegate { foreach (var entry in pending) entry.Value.TrySetException(new IOException("Соединение с Codex завершилось")); };
            if (!process.Start()) throw new IOException("Не удалось запустить интерфейс Codex");
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            Request("initialize",new { clientInfo=new { name="codex_limits_widget", title="Codex Limits", version="1.0.0" } });
            Send(new { method="initialized" });
        }
        public Snapshot Read() {
            try {
                if (process == null || process.HasExited) Connect();
                return Snapshot.Parse(Json.Serializer.Serialize(Request("account/rateLimits/read",null)));
            } finally { Dispose(); } // Do not keep an app-server in memory between minute-long refreshes.
        }
        public void Dispose() {
            var current = process; process=null; if (current == null) return;
            try { if (!current.HasExited) { current.StandardInput.Close(); if (!current.WaitForExit(1500)) current.Kill(); } }
            catch (InvalidOperationException) { }
            finally { current.Dispose(); }
        }
    }
}
