using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

internal static class CompletionDelivery {
 internal static readonly string PipeName=ResidentProtocol.Name+"-completion-v1";
 internal static bool Send(string key,Func<string,bool> testDelivery,string logKey=null){
  return SendTo(key,delegate(string id,int deadline){return Exchange(PipeName,id,deadline);},Launch,testDelivery,logKey);
 }
 internal static bool SendTo(string key,Func<string,int,string> exchange,Func<string,Process> launch,Func<string,bool> testDelivery=null,string logKey=null){
  var elapsed=Stopwatch.StartNew();Process child=null;
  try{
   for(int attempt=0;attempt<CompletionDeliveryStore.MaxAttempts;attempt++){
    if(CompletionDeliveryStore.Receipt(key)!=null)return true;
    int remaining=Math.Min(CompletionDeliveryStore.Remaining(CompletionDeliveryStore.ReadRequest(key)),CompletionDeliveryStore.WindowMs-(int)elapsed.ElapsedMilliseconds);
    if(remaining<=0||!CompletionDeliveryStore.Attempt(key))break;
    int slice=Math.Max(1,remaining/(CompletionDeliveryStore.MaxAttempts-attempt));
    try{if(testDelivery!=null){if(testDelivery(key))CompletionDeliveryStore.Accept(key,"accepted");}
    else {
     // Already-playing host receives first. Never use pipe write/process launch as ACK.
     string reply=exchange(key,Math.Min(40,slice));
     if(CompletionDeliveryStore.Receipt(key)!=null)return true;
     if(reply==null&&(child==null||child.HasExited)){
      if(child!=null){child.Dispose();child=null;}
      child=launch(key);
      if(child!=null)IntroLog.Write("completion-player-launched task="+(logKey??key).Substring(0,12)+" player-pid="+child.Id+" delivery=acknowledged-host");
     }
     int budget=Math.Min(slice,CompletionDeliveryStore.WindowMs-(int)elapsed.ElapsedMilliseconds);
     if(budget>0)exchange(key,budget);
    }}catch(Exception e){IntroLog.Write("completion-delivery-attempt-error task="+key.Substring(0,12)+" type="+e.GetType().Name);}
    if(CompletionDeliveryStore.Receipt(key)!=null)return true;
    IntroLog.Write("completion-delivery-retry task="+key.Substring(0,12)+" attempt="+(attempt+1));
    int pause=Math.Min(50,CompletionDeliveryStore.WindowMs-(int)elapsed.ElapsedMilliseconds);if(attempt<CompletionDeliveryStore.MaxAttempts-1&&pause>0)Thread.Sleep(pause);
   }
   return CompletionDeliveryStore.Receipt(key)!=null;
  }catch(Exception e){IntroLog.Write("completion-delivery-error task="+key.Substring(0,12)+" type="+e.GetType().Name);return false;}
  finally{if(child!=null)child.Dispose();CompletionDeliveryStore.Finish(key);}
 }
 static Process Launch(string key){
  string player=PlayerPath();if(player==null)throw new InvalidOperationException("Installed completion player version mismatch.");
  return Process.Start(new ProcessStartInfo{FileName=player,Arguments="--completion-host "+key,UseShellExecute=false,CreateNoWindow=true});
 }
 // Active registration selects editable installed media, but a stale installed
 // executable must not interpret the new mode as a legacy intro command.
 internal static string PlayerPath(){
  string local=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BootPlayer.exe"),command;
  using(var registration=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))command=registration==null?null:registration.GetValue("CodexBootAnimation") as string;
  return SelectPlayer(local,command);
 }
 internal static string SelectPlayer(string local,string command){
  Match match=Regex.Match(command??"","^\"([^\"]+\\\\BootPlayer\\.exe)\" --supervise$",RegexOptions.IgnoreCase);
  if(String.IsNullOrEmpty(command))return local;
  if(!match.Success||!File.Exists(match.Groups[1].Value))return null;
  string installed=match.Groups[1].Value;
  using(var sha=System.Security.Cryptography.SHA256.Create()){
   byte[] left,right;using(var stream=File.OpenRead(local))left=sha.ComputeHash(stream);using(var stream=File.OpenRead(installed))right=sha.ComputeHash(stream);
   return Convert.ToBase64String(left)==Convert.ToBase64String(right)?installed:null;
  }
 }
 internal static string Exchange(string name,string key,int deadlineMs){
  if(!CompletionDeliveryStore.ValidKey(key)||deadlineMs<=0)return null;
  var elapsed=Stopwatch.StartNew();
  try{using(var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous)){
   client.Connect(deadlineMs);int remaining=deadlineMs-(int)elapsed.ElapsedMilliseconds;if(remaining<=0)return null;
   byte[] request=Encoding.ASCII.GetBytes(key+"|"+DateTime.UtcNow.AddMilliseconds(remaining).Ticks+"\n");
   var write=client.WriteAsync(request,0,request.Length);if(!write.Wait(remaining))return null;
   remaining=deadlineMs-(int)elapsed.ElapsedMilliseconds;if(remaining<=0)return null;
   byte[] response=new byte[32];int total=0;
   while(total<response.Length){var read=client.ReadAsync(response,total,response.Length-total);if(!read.Wait(remaining))return null;int count=read.Result;if(count==0)return null;total+=count;if(response[total-1]=='\n')return Encoding.ASCII.GetString(response,0,total-1).TrimEnd('\r');remaining=deadlineMs-(int)elapsed.ElapsedMilliseconds;if(remaining<=0)return null;}
   return null;
  }}catch{return null;}
 }
}
