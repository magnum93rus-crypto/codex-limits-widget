using System;
using System.Diagnostics;
using System.IO;
using CodexLimits;
class FakeCodexServer {
    static void Main() {
        File.AppendAllText("fake-server.pids",Process.GetCurrentProcess().Id+Environment.NewLine);
        string line;
        while((line=Console.ReadLine())!=null) {
            var request=Json.Map(Json.Serializer.DeserializeObject(line));
            var id=Json.Get(request,"id");if(id==null)continue;
            string method=Json.Text(request,"method");
            if(method=="account/rateLimits/read"&&File.Exists("fake-server.fail"))
                Console.WriteLine(Json.Serializer.Serialize(new {id=id,error=new {code=-1,message="test error"}}));
            else Console.WriteLine(Json.Serializer.Serialize(new {id=id,result=method=="initialize"?(object)new {}:new {rateLimits=new {primary=new {usedPercent=22,windowDurationMins=300}}}}));
        }
    }
}
