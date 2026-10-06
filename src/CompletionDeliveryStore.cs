using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;

// Separate from legacy candidate/claimed files. No prompt, reply or raw IDs.
internal static class CompletionDeliveryStore {
 internal const int WindowMs=2000,MaxAttempts=3;
 internal static string Folder {get{return CompletionStateStore.TestFolder==null?Path.Combine(IntroLog.Data,"completion-deliveries"):Path.Combine(CompletionStateStore.TestFolder,"delivery");}}
 internal static bool ValidKey(string key){return key!=null&&Regex.IsMatch(key,"^[0-9a-f]{64}$");}
 internal sealed class Request {internal long Started;internal int Attempts;internal string Status;}
 sealed class Guard:IDisposable {
  readonly Mutex mutex;
  internal Guard(string key){mutex=new Mutex(false,"Local\\CodexCompletionDelivery-"+CompletionStateStore.Hash(Folder+"\n"+key));bool entered;try{entered=mutex.WaitOne(50);}catch(AbandonedMutexException){entered=true;}if(!entered){mutex.Dispose();throw new TimeoutException("Completion delivery state busy.");}}
  public void Dispose(){mutex.ReleaseMutex();mutex.Dispose();}
 }
 static string PathFor(string key,string suffix){if(!ValidKey(key))throw new ArgumentException("Invalid delivery key.");return Path.Combine(Folder,key+suffix);}
 static string[] Read(string file){if(!File.Exists(file))return null;if(new FileInfo(file).Length>256)throw new InvalidDataException("Oversized delivery record.");return File.ReadAllText(file,Encoding.ASCII).Trim().Split('|');}
 static void Write(string file,string value){
  Directory.CreateDirectory(Folder);
  string temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{
   using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
    byte[] bytes=Encoding.ASCII.GetBytes(value);
    stream.Write(bytes,0,bytes.Length);stream.Flush(true);
   }
   if(File.Exists(file))File.Replace(temporary,file,null);
   else File.Move(temporary,file);
  }finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 internal static Request ReadRequest(string key){
  string[] parts=Read(PathFor(key,".request"));if(parts==null)return null;
  long ticks;int attempts;
  if(parts.Length!=4||parts[0]!="1"||
   !Int64.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out ticks)||
   ticks<DateTime.MinValue.Ticks||ticks>DateTime.MaxValue.Ticks||
   !Int32.TryParse(parts[2],out attempts)||attempts<0||attempts>MaxAttempts||
   (parts[3]!="pending"&&parts[3]!="delivered"&&parts[3]!="failed"))
   throw new InvalidDataException("Invalid delivery request.");
  return new Request{Started=ticks,Attempts=attempts,Status=parts[3]};
 }
 static void Save(string key,Request request){Write(PathFor(key,".request"),"1|"+request.Started.ToString(CultureInfo.InvariantCulture)+"|"+request.Attempts+"|"+request.Status);}
 internal static string Receipt(string key){
  string[] parts=Read(PathFor(key,".receipt"));if(parts==null)return null;
  long ticks;
  if(parts.Length!=3||parts[0]!="1"||
   !Int64.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out ticks)||
   ticks<DateTime.MinValue.Ticks||ticks>DateTime.MaxValue.Ticks||
   (parts[2]!="accepted"&&parts[2]!="merged"&&parts[2]!="dismissed"))
   throw new InvalidDataException("Invalid delivery receipt.");
  return parts[2];
 }
 internal static int Remaining(Request request){if(request==null)return 0;long elapsed=(DateTime.UtcNow.Ticks-request.Started)/TimeSpan.TicksPerMillisecond;return elapsed<0||elapsed>=WindowMs?0:WindowMs-(int)elapsed;}
 internal static bool Begin(string key){using(new Guard(key)){if(Receipt(key)!=null)return false;Request request=ReadRequest(key);if(request!=null){if(request.Status=="pending"&&Remaining(request)==0){request.Status="failed";Save(key,request);}return false;}Save(key,new Request{Started=DateTime.UtcNow.Ticks,Status="pending"});return true;}}
 internal static bool Attempt(string key){using(new Guard(key)){Request request=ReadRequest(key);if(request==null||request.Status!="pending"||request.Attempts>=MaxAttempts||Remaining(request)==0)return false;request.Attempts++;Save(key,request);return true;}}
 internal static bool Accept(string key,string outcome){if(outcome!="accepted"&&outcome!="merged"&&outcome!="dismissed")return false;using(new Guard(key)){if(Receipt(key)!=null)return true;Request request=ReadRequest(key);if(request==null||request.Status!="pending"||Remaining(request)==0)return false;Write(PathFor(key,".receipt"),"1|"+DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)+"|"+outcome);return true;}}
 internal static void Finish(string key){using(new Guard(key)){Request request=ReadRequest(key);if(request==null)return;request.Status=Receipt(key)==null?"failed":"delivered";Save(key,request);}}
 // Keep the continuation's original delivery identity even if newer input
 // replaces the session scope before a crashed sender can consume its state.
 internal static void Link(string finalKey,string originKey){
  if(!ValidKey(originKey))throw new ArgumentException("Invalid delivery origin.");
  using(new Guard(finalKey)){
   string existing=Origin(finalKey);
   if(existing!=null&&existing!=originKey)throw new InvalidDataException("Conflicting delivery origin.");
   if(existing==null)Write(PathFor(finalKey,".alias"),"1|"+originKey);
  }
 }
 internal static string Origin(string finalKey){
  string[] parts=Read(PathFor(finalKey,".alias"));if(parts==null)return null;
  if(parts.Length!=2||parts[0]!="1"||!ValidKey(parts[1]))throw new InvalidDataException("Invalid delivery origin.");
  return parts[1];
 }
}

// Receipt means a moving completion video started, not that it ran to the end.
// A second distinct task can merge only while that same automatic window is live.
internal sealed class CompletionDeliveryReceiver:IDisposable {
 readonly ManualResetEventSlim readyOrClosed=new ManualResetEventSlim(false);
 int ready,closed;volatile bool accepted;
 internal void MarkReady(){Interlocked.Exchange(ref ready,1);readyOrClosed.Set();}
 internal void MarkClosed(){Interlocked.Exchange(ref closed,1);readyOrClosed.Set();}
 internal bool HasAccepted {get{return accepted;}}
 internal void Dismiss(string key){
  if(CompletionDeliveryStore.Accept(key,"dismissed")){accepted=true;IntroLog.Write("completion-receiver-dismissed task="+key.Substring(0,12));}
 }
 internal string Receive(string key,int waitMs){
  string receipt=CompletionDeliveryStore.Receipt(key);if(receipt!=null)return receipt;
  if(!readyOrClosed.Wait(Math.Max(0,waitMs)))return "pending";
  receipt=CompletionDeliveryStore.Receipt(key);if(receipt!=null)return receipt;
  if(Volatile.Read(ref closed)!=0||Volatile.Read(ref ready)==0)return "closed";
  string outcome=accepted?"merged":"accepted";
  if(!CompletionDeliveryStore.Accept(key,outcome))return "expired";
  accepted=true;IntroLog.Write("completion-receiver-"+outcome+" task="+key.Substring(0,12));return outcome;
 }
 public void Dispose(){readyOrClosed.Dispose();}
}
