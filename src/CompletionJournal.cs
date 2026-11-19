using System;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Threading;

// Codex desktop currently writes task_complete to its session journal even when
// its Stop command hook is not invoked. This is a local fallback, not an API.
internal sealed class CompletionJournal : IDisposable {
 readonly string root;
 readonly DateTime startedUtc=DateTime.UtcNow;
 internal const int MaxTrackedFiles=512, MaxWorkers=2;
 sealed class Entry {
  internal string Path;
  internal CompletionCursor Cursor;
  internal bool Queued,Running,Requested;
 }
 readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
 readonly Queue<Entry> pending=new Queue<Entry>();
 readonly object gate=new object();
 int workers;
 bool stopped,capacityReported;
 readonly Action<string,string,string,long,int> deliver;
 readonly Action<string,string,string,DateTime?> deliverPrompt;
 readonly Action<string> beforeRead;
 readonly Dictionary<string,int> turnTools=new Dictionary<string,int>(StringComparer.Ordinal);
 FileSystemWatcher watcher;
 static readonly Regex SessionId=new Regex(@"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}",RegexOptions.Compiled|RegexOptions.IgnoreCase);

 public CompletionJournal():this(null){}
 CompletionJournal(string testRoot):this(testRoot,null,null,true){}
 internal CompletionJournal(string testRoot,Action<string,string,string,long> delivery,Action<string> readProbe,bool watch):this(testRoot,delivery==null?(Action<string,string,string,long,int>)null:delegate(string s,string t,string m,long d,int tools){delivery(s,t,m,d);},null,readProbe,watch){}
 internal CompletionJournal(string testRoot,Action<string,string,string,long> delivery,Action<string,string,string,DateTime?> promptDelivery,Action<string> readProbe,bool watch):this(testRoot,delivery==null?(Action<string,string,string,long,int>)null:delegate(string s,string t,string m,long d,int tools){delivery(s,t,m,d);},promptDelivery,readProbe,watch){}
 internal CompletionJournal(string testRoot,Action<string,string,string,long,int> delivery,Action<string,string,string,DateTime?> promptDelivery,Action<string> readProbe,bool watch){
  deliver=delivery??delegate(string session,string turn,string message,long duration,int tools){TaskCompletion.OnJournal(session,turn,message,duration,tools);};
  deliverPrompt=promptDelivery??(delivery==null?(Action<string,string,string,DateTime?>)delegate(string session,string turn,string prompt,DateTime? ts){PromptReaction.OnJournalPrompt(session,turn,prompt,ts);}:delegate{});
  beforeRead=readProbe;
  string codexHome=Environment.GetEnvironmentVariable("CODEX_HOME");
  if(String.IsNullOrWhiteSpace(codexHome))codexHome=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
  root=testRoot??Path.Combine(codexHome,"sessions");
  if(!Directory.Exists(root)){IntroLog.Write("completion-journal-unavailable");return;}
  foreach(string file in RecentFiles())try{
   if(entries.Count>=MaxTrackedFiles)break;
   using(var stream=Open(file))entries[file]=new Entry{Path=file,Cursor=new CompletionCursor(stream,true)};
  }catch{}
  if(!watch)return;
  try{
   watcher=new FileSystemWatcher(root,"*.jsonl"){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size};
   watcher.Created+=delegate(object s,FileSystemEventArgs e){Read(e.FullPath);};
   watcher.Changed+=delegate(object s,FileSystemEventArgs e){Read(e.FullPath);};
   watcher.EnableRaisingEvents=true;
   IntroLog.Write("completion-journal-ready");
  }catch(Exception e){IntroLog.Write("completion-journal-watch-error type="+e.GetType().Name);}
 }

 internal static DateTime[] RecentDates(DateTime local,DateTime utc){
  var dates=new List<DateTime>();
  foreach(DateTime date in new[]{local.Date,local.Date.AddDays(-1),utc.Date,utc.Date.AddDays(-1)})if(!dates.Contains(date))dates.Add(date);
  return dates.ToArray();
 }
 IEnumerable<string> RecentFiles(){
  // Journal directories may use UTC while Windows uses a different local date.
  foreach(DateTime date in RecentDates(DateTime.Now,DateTime.UtcNow)){
   string directory=Path.Combine(root,date.ToString("yyyy"),date.ToString("MM"),date.ToString("dd"));
   if(!Directory.Exists(directory))continue;
   foreach(string file in Directory.GetFiles(directory,"*.jsonl",SearchOption.TopDirectoryOnly))yield return file;
  }
 }
 public void Reconcile(){
  try{
   // Retire only idle files outside the same date window used by discovery.
   var directories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var date in RecentDates(DateTime.Now,DateTime.UtcNow))directories.Add(Path.Combine(root,date.ToString("yyyy"),date.ToString("MM"),date.ToString("dd")));
   lock(gate){
    var retired=new List<string>();
    foreach(var pair in entries)if(!pair.Value.Running&&!pair.Value.Queued&&!directories.Contains(Path.GetDirectoryName(pair.Key)))retired.Add(pair.Key);
    foreach(string path in retired)entries.Remove(path);
    if(entries.Count<MaxTrackedFiles)capacityReported=false;
   }
   foreach(string file in RecentFiles())Read(file);
  }catch(Exception e){IntroLog.Write("completion-journal-reconcile-error type="+e.GetType().Name);}
 }
 static FileStream Open(string path){return new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);}
 internal void Read(string path){
  if(!path.EndsWith(".jsonl",StringComparison.OrdinalIgnoreCase)||!SessionId.IsMatch(Path.GetFileName(path)))return;
  bool report=false;
  lock(gate){
   if(stopped)return;
   Entry entry;
   if(!entries.TryGetValue(path,out entry)){
    if(entries.Count>=MaxTrackedFiles){report=!capacityReported;capacityReported=true;}
    else{entry=new Entry{Path=path};entries.Add(path,entry);}
   }
   if(entry!=null){
    entry.Requested=true;
    if(!entry.Running&&!entry.Queued){entry.Queued=true;pending.Enqueue(entry);}
    int start=Math.Min(MaxWorkers-workers,pending.Count);
    for(int i=0;i<start;i++){workers++;ThreadPool.QueueUserWorkItem(delegate{Drain();});}
   }
  }
  if(report)IntroLog.Write("completion-journal-deferred reason=file-capacity");
 }
 void Drain(){
  for(;;){
   Entry entry;
   lock(gate){
    if(stopped||pending.Count==0){workers--;Monitor.PulseAll(gate);return;}
    entry=pending.Dequeue();entry.Queued=false;entry.Running=true;entry.Requested=false;
   }
   bool more=false;
   try{
    if(beforeRead!=null)beforeRead(entry.Path);
    string line,skipped;
    using(var stream=Open(entry.Path)){
     if(entry.Cursor==null)entry.Cursor=new CompletionCursor(stream,File.GetCreationTimeUtc(entry.Path)<startedUtc.AddSeconds(-2));
     line=entry.Cursor.Read(stream,out more,out skipped);
    }
    if(skipped!=null)IntroLog.Write("completion-journal-line-skipped reason="+skipped);
    string turn,message;long duration;DateTime? promptTimeUtc;
    string session=SessionId.Match(Path.GetFileName(entry.Path)).Value;
    if(line!=null&&TryParse(line,out turn,out message,out duration)){
     int tools=TakeToolCount(session,turn);
     bool active;lock(gate)active=!stopped;
     if(active)deliver(session,turn,message,duration,tools);
    }else if(line!=null&&TryParseUserPrompt(line,out turn,out message,out promptTimeUtc)){
     bool active;lock(gate)active=!stopped;
     if(active)deliverPrompt(session,turn,message,promptTimeUtc);
    }else if(line!=null&&TryParseToolItem(line,out turn)){
     RecordToolCall(session,turn);
    }
   }catch(IOException){more=false;}
   catch(Exception e){more=false;IntroLog.Write("completion-journal-read-error type="+e.GetType().Name);}
   finally{
    lock(gate){
     entry.Running=false;
     if(!stopped&&(more||entry.Requested)){entry.Queued=true;pending.Enqueue(entry);}
     Monitor.PulseAll(gate);
    }
   }
  }
 }
 internal bool WaitForIdle(int timeoutMs){
  var elapsed=System.Diagnostics.Stopwatch.StartNew();
  lock(gate){while(workers>0){int left=timeoutMs-(int)elapsed.ElapsedMilliseconds;if(left<=0)return false;Monitor.Wait(gate,left);}return true;}
 }
 public static bool TryParse(string line,out string turn,out string message,out long durationMs){
  turn=null;message=null;durationMs=0;
  if(line==null||line.IndexOf("\"task_complete\"",StringComparison.Ordinal)<0)return false;
  try{
   var record=new JavaScriptSerializer{MaxJsonLength=CompletionCursor.MaxChars}.DeserializeObject(line) as Dictionary<string,object>;
   if(record==null||!String.Equals(record["type"] as string,"event_msg",StringComparison.Ordinal))return false;
   var payload=record["payload"] as Dictionary<string,object>;
   if(payload==null||!String.Equals(payload["type"] as string,"task_complete",StringComparison.Ordinal))return false;
   turn=payload["turn_id"] as string;message=payload["last_agent_message"] as string;
   object raw;if(payload.TryGetValue("duration_ms",out raw)&&raw!=null)durationMs=Convert.ToInt64(raw);
   return !String.IsNullOrWhiteSpace(turn)&&!String.IsNullOrWhiteSpace(message);
  }catch{return false;}
 }
 public static bool TryParseUserPrompt(string line,out string turn,out string prompt,out DateTime? timestampUtc){
  turn=null;prompt=null;timestampUtc=null;
  if(line==null||(line.IndexOf("\"UserMessage\"",StringComparison.Ordinal)<0&&line.IndexOf("\"user_message\"",StringComparison.Ordinal)<0))return false;
  try{
   var record=new JavaScriptSerializer{MaxJsonLength=CompletionCursor.MaxChars}.DeserializeObject(line) as Dictionary<string,object>;
   if(record==null||!String.Equals(record["type"] as string,"event_msg",StringComparison.Ordinal))return false;
   object rawTs;DateTime parsedTs;
   if(record.TryGetValue("timestamp",out rawTs)&&rawTs is string&&DateTime.TryParse((string)rawTs,CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal|DateTimeStyles.AssumeUniversal,out parsedTs))timestampUtc=parsedTs;
   var payload=record["payload"] as Dictionary<string,object>;
   if(payload==null)return false;
   string kind=payload["type"] as string;
   if(String.Equals(kind,"item_completed",StringComparison.Ordinal)){
    var item=payload.ContainsKey("item")?payload["item"] as Dictionary<string,object>:null;
    if(item==null||!String.Equals(item["type"] as string,"UserMessage",StringComparison.Ordinal))return false;
    turn=payload.ContainsKey("turn_id")?payload["turn_id"] as string:null;
    object rawContent;
    if(item.TryGetValue("content",out rawContent)&&rawContent is object[]){
     var parts=new List<string>();
     foreach(object element in (object[])rawContent){
      var part=element as Dictionary<string,object>;
      if(part==null)continue;
      string partType=part.ContainsKey("type")?part["type"] as string:null;
      string text=part.ContainsKey("text")?part["text"] as string:null;
      if((String.Equals(partType,"text",StringComparison.Ordinal)||String.Equals(partType,"input_text",StringComparison.Ordinal))&&!String.IsNullOrWhiteSpace(text))parts.Add(text);
     }
     if(parts.Count>0)prompt=String.Join("\n",parts.ToArray());
    }
    return !String.IsNullOrWhiteSpace(turn)&&!String.IsNullOrWhiteSpace(prompt);
   }
   if(String.Equals(kind,"user_message",StringComparison.Ordinal)){
    turn=payload.ContainsKey("turn_id")?payload["turn_id"] as string:null;
    prompt=payload.ContainsKey("message")?payload["message"] as string:null;
    return !String.IsNullOrWhiteSpace(turn)&&!String.IsNullOrWhiteSpace(prompt);
   }
   return false;
  }catch{return false;}
 }
 void RecordToolCall(string session,string turn){
  if(String.IsNullOrEmpty(session)||String.IsNullOrEmpty(turn))return;
  string key=session+"\n"+turn;
  lock(gate){
   if(turnTools.Count>=MaxTrackedFiles&&!turnTools.ContainsKey(key))turnTools.Clear();
   int count;turnTools.TryGetValue(key,out count);turnTools[key]=count+1;
  }
 }
 int TakeToolCount(string session,string turn){
  if(String.IsNullOrEmpty(session)||String.IsNullOrEmpty(turn))return 0;
  string key=session+"\n"+turn;
  lock(gate){int count;if(turnTools.TryGetValue(key,out count)){turnTools.Remove(key);return count;}return 0;}
 }
 public static bool TryParseToolItem(string line,out string turn){
  turn=null;
  if(line==null||line.IndexOf("\"item_completed\"",StringComparison.Ordinal)<0)return false;
  try{
   var record=new JavaScriptSerializer{MaxJsonLength=CompletionCursor.MaxChars}.DeserializeObject(line) as Dictionary<string,object>;
   if(record==null||!String.Equals(record["type"] as string,"event_msg",StringComparison.Ordinal))return false;
   var payload=record["payload"] as Dictionary<string,object>;
   if(payload==null||!String.Equals(payload["type"] as string,"item_completed",StringComparison.Ordinal))return false;
   var item=payload.ContainsKey("item")?payload["item"] as Dictionary<string,object>:null;
   string itemType=item!=null&&item.ContainsKey("type")?item["type"] as string:null;
   if(String.IsNullOrWhiteSpace(itemType)||String.Equals(itemType,"UserMessage",StringComparison.Ordinal)||String.Equals(itemType,"AgentMessage",StringComparison.Ordinal)||String.Equals(itemType,"Reasoning",StringComparison.Ordinal))return false;
   turn=payload.ContainsKey("turn_id")?payload["turn_id"] as string:null;
   return !String.IsNullOrWhiteSpace(turn);
  }catch{return false;}
 }
 public static bool SelfTest(string runtimeRoot){
  if(String.IsNullOrEmpty(runtimeRoot))return false;
  DateTime utc=new DateTime(2026,10,1,2,0,0,DateTimeKind.Utc);
  DateTime[] west=RecentDates(utc.AddHours(-7),utc),east=RecentDates(utc.AddHours(23),utc);
  if(west.Length!=3||Array.IndexOf(west,utc.Date)<0||Array.IndexOf(west,utc.Date.AddDays(-1))<0||east.Length!=3||Array.IndexOf(east,utc.Date)<0)return false;
  string turn,message;long durationMs;
  string complete="{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"turn-1\",\"last_agent_message\":\"已修复。\",\"duration_ms\":120000}}";
  if(!TryParse(complete,out turn,out message,out durationMs)||turn!="turn-1"||message!="已修复。"||durationMs!=120000||TryParse("{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}",out turn,out message,out durationMs))return false;
  string session=Guid.NewGuid().ToString(),testTurn=Guid.NewGuid().ToString();
  string testRoot=Path.Combine(Path.GetTempPath(),"codex-boot-journal-"+Guid.NewGuid().ToString("N"));
  string key;
  using(var sha=System.Security.Cryptography.SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(session+"\n"+testTurn))).Replace("-","").ToLowerInvariant();
  // Both the journal fixture and the completion state folder are injected;
  // nothing is written to the real profile, even if the caller binds nothing.
  string stateFolder=Path.Combine(runtimeRoot,"turns");
  string state=Path.Combine(stateFolder,key+".candidate");
  string previousTestFolder=CompletionStateStore.TestFolder;
  CompletionStateStore.TestFolder=stateFolder;
  CompletionJournal journal=null;
  try{
   Directory.CreateDirectory(testRoot);
   TaskCompletion.OnPrompt(new Dictionary<string,object>{{"session_id",session},{"turn_id",testTurn},{"prompt","帮我修复这个插件"}});
   if(!File.Exists(state)||File.ReadAllText(state).Trim()!="1")return false;
   journal=new CompletionJournal(testRoot);
   string directory=Path.Combine(testRoot,DateTime.Now.ToString("yyyy"),DateTime.Now.ToString("MM"),DateTime.Now.ToString("dd"));
   Directory.CreateDirectory(directory);
   string file=Path.Combine(directory,"rollout-test-"+session+".jsonl");
   string record="{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\""+testTurn+"\",\"last_agent_message\":\"之后会修复。\"}}\n";
   File.WriteAllText(file,record,new UTF8Encoding(false));
   for(int retry=0;retry<30&&File.Exists(state);retry++){journal.Reconcile();Thread.Sleep(100);}
   return !File.Exists(state);
  }catch{return false;}
  finally{if(journal!=null)journal.Dispose();CompletionStateStore.TestFolder=previousTestFolder;if(File.Exists(state))File.Delete(state);if(Directory.Exists(testRoot))Directory.Delete(testRoot,true);}
 }
 public void Dispose(){lock(gate){stopped=true;pending.Clear();}if(watcher!=null){watcher.EnableRaisingEvents=false;watcher.Dispose();}WaitForIdle(2000);}
}
