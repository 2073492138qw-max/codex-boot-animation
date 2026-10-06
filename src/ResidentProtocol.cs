using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

// Private per-user/per-session IPC, bounded even after a client connects.
internal static class ResidentProtocol {
 internal const int MaxCommandBytes=256;
 internal const int ReadDeadlineMs=1000;
 internal static readonly string Name=NameFor(WindowsIdentity.GetCurrent().User.Value,Process.GetCurrentProcess().SessionId);
 internal static string NameFor(string sid,int session){return "CodexBootAnimationResidentV2-"+sid+"-"+session;}
 internal static NamedPipeServerStream CreateServer(string name){
  var security=new PipeSecurity();
  security.SetAccessRuleProtection(true,false);
  security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
  return new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,512,512,security);
 }
 internal static string ReadCommand(NamedPipeServerStream pipe,int deadlineMs){
  var timer=Stopwatch.StartNew();var message=new MemoryStream();byte[] buffer=new byte[64];
  try{
   while(true){
    int remaining=deadlineMs-(int)timer.ElapsedMilliseconds;
    if(remaining<=0){pipe.Dispose();return null;}
    Task<int> read=pipe.ReadAsync(buffer,0,buffer.Length);
    if(!read.Wait(remaining)){
     // Close this exact connection to cancel pending I/O; never await indefinitely.
     pipe.Dispose();read.ContinueWith(delegate(Task<int> failed){var observed=failed.Exception;},TaskContinuationOptions.OnlyOnFaulted);
     return null;
    }
    int count=read.Result;
    if(count==0)return Decode(message);
    for(int index=0;index<count;index++){
     if(buffer[index]=='\n')return Decode(message);
     if(message.Length>=MaxCommandBytes)return null;
     message.WriteByte(buffer[index]);
    }
   }
  }finally{message.Dispose();}
 }
 static string Decode(MemoryStream message){
  try{return new UTF8Encoding(false,true).GetString(message.ToArray()).TrimEnd('\r');}
  catch(DecoderFallbackException){return null;}
 }
}
