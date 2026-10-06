using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class ResidentProtocolTests {
 static string UniqueName(){return ResidentProtocol.Name+"-test-"+Guid.NewGuid().ToString("N");}
 static Task<string> Receive(NamedPipeServerStream server,int deadline){
  // Register accept before connecting; otherwise the EOF case can disconnect
  // before its worker starts and never exercise ReadCommand at all.
  IAsyncResult connection=server.BeginWaitForConnection(null,null);
  return Task.Factory.StartNew(delegate{server.EndWaitForConnection(connection);return ResidentProtocol.ReadCommand(server,deadline);});
 }
 static bool Legal(string command,bool fragmented){
  string name=UniqueName();
  using(var server=ResidentProtocol.CreateServer(name)){
   Task<string> result=Receive(server,1000);
   using(var client=new NamedPipeClientStream(".",name,PipeDirection.Out)){
    client.Connect(1000);byte[] bytes=Encoding.UTF8.GetBytes(command+"\r\n");
    if(fragmented){client.Write(bytes,0,3);client.Flush();Thread.Sleep(15);client.Write(bytes,3,bytes.Length-3);}
    else client.Write(bytes,0,bytes.Length);
    client.Flush();return result.Wait(1500)&&result.Result==command;
   }
  }
 }
 internal static bool SelfTest(){
  try{return Run();}catch(Exception e){IntroLog.Write("resident-protocol-test-error type="+e.GetType().Name);return false;}
 }
 static bool Run(){
  if(ResidentProtocol.NameFor("user-a",1)==ResidentProtocol.NameFor("user-b",1)||ResidentProtocol.NameFor("user-a",1)==ResidentProtocol.NameFor("user-a",2))return false;
  string name=UniqueName();
  using(var server=ResidentProtocol.CreateServer(name)){
   string user=WindowsIdentity.GetCurrent().User.Value;int allowed=0;
   foreach(PipeAccessRule rule in server.GetAccessControl().GetAccessRules(true,true,typeof(SecurityIdentifier))){
    if(rule.AccessControlType==AccessControlType.Allow){if(rule.IdentityReference.Value!=user)return false;allowed++;}
   }
   if(allowed!=1||!server.GetAccessControl().AreAccessRulesProtected)return false;
   Task<string> result=Receive(server,150);
   using(var silent=new NamedPipeClientStream(".",name,PipeDirection.In)){
    silent.Connect(1000);var timer=Stopwatch.StartNew();
    if(!result.Wait(1000)||result.Result!=null||timer.ElapsedMilliseconds>950)return false;
    // Reuse the same name while the old client still holds its disconnected handle.
    using(var next=ResidentProtocol.CreateServer(name)){
     Task<string> received=Receive(next,1000);
     using(var client=new NamedPipeClientStream(".",name,PipeDirection.Out)){
      client.Connect(1000);byte[] bytes=Encoding.UTF8.GetBytes("__reload__\n");client.Write(bytes,0,bytes.Length);client.Flush();
      if(!received.Wait(1500)||received.Result!="__reload__")return false;
     }
    }
   }
  }
  foreach(string command in new[]{"desktop-start","new-dialog-click","reaction-angry","__reload__","__close_idle__","__idle_return_request__:"+Guid.NewGuid().ToString("N"),"__task_complete__:"+new string('a',64)})if(!Legal(command,true))return false;
  if(!Legal(new string('x',ResidentProtocol.MaxCommandBytes-1),false))return false; // CR is part of byte cap.
  name=UniqueName();
  using(var server=ResidentProtocol.CreateServer(name)){
   Task<string> result=Receive(server,1000);
   using(var client=new NamedPipeClientStream(".",name,PipeDirection.Out)){
    client.Connect(1000);byte[] bytes=Encoding.UTF8.GetBytes(new string('x',ResidentProtocol.MaxCommandBytes+1)+"\n");client.Write(bytes,0,bytes.Length);client.Flush();
    if(!result.Wait(1500)||result.Result!=null)return false;
   }
  }
  // A dribbling client must not reset the absolute deadline on each read.
  name=UniqueName();
  using(var server=ResidentProtocol.CreateServer(name)){
   Task<string> result=Receive(server,150);
   using(var client=new NamedPipeClientStream(".",name,PipeDirection.Out)){
    client.Connect(1000);client.WriteByte((byte)'x');client.Flush();Thread.Sleep(90);client.WriteByte((byte)'x');client.Flush();
    if(!result.Wait(200)||result.Result!=null)return false;
   }
  }
  name=UniqueName();
  using(var server=ResidentProtocol.CreateServer(name)){
   Task<string> result=Receive(server,1000);
   using(var client=new NamedPipeClientStream(".",name,PipeDirection.Out)){client.Connect(1000);client.WriteByte((byte)'x');client.Flush();}
   if(!result.Wait(1500)||result.Result!="x")return false;
  }
  return true;
 }
}
