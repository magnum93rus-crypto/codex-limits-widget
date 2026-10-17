using System;
using System.Diagnostics;
using System.IO;
using CodexLimits;
namespace CodexLimits {static class Program {public static string DataDirectory;}}
class ClientTests {
    static void NoServer() {
        foreach(string pid in File.ReadAllLines(Path.Combine(Program.DataDirectory,"fake-server.pids"))) {
            try {using(var p=Process.GetProcessById(int.Parse(pid)))if(!p.HasExited)throw new Exception("Server remains alive after the request");}
            catch(ArgumentException){}
        }
    }
    static void Main(string[] args) {
        Program.DataDirectory=Path.GetFullPath(args[1]);Directory.CreateDirectory(Program.DataDirectory);
        foreach(string file in Directory.GetFiles(Program.DataDirectory,"fake-server.*"))File.Delete(file);
        using(var client=new CodexClient(Path.GetFullPath(args[0]))) {
            try {
                for(int i=0;i<2;i++) {
                    if(client.Read().MainWindow.Remaining!=78)throw new Exception("Incorrect limits");
                    NoServer();
                }
                File.WriteAllText(Path.Combine(Program.DataDirectory,"fake-server.fail"),"");
                bool failed=false;try {client.Read();}catch(InvalidOperationException){failed=true;}
                if(!failed)throw new Exception("Expected server error");NoServer();
                Console.WriteLine("PASS server exits after each successful read, repeated read, and error");
            }catch(Exception e){Console.WriteLine("FAIL "+e.Message);Environment.ExitCode=1;}
        }
    }
}
