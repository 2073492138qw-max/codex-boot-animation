using System;
using System.IO;
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
 readonly Dictionary<string,long> offsets=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
 readonly object gate=new object();
 FileSystemWatcher watcher;
 static readonly Regex SessionId=new Regex(@"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}",RegexOptions.Compiled|RegexOptions.IgnoreCase);

 public CompletionJournal():this(null){}
 CompletionJournal(string testRoot){
  string codexHome=Environment.GetEnvironmentVariable("CODEX_HOME");
  if(String.IsNullOrWhiteSpace(codexHome))codexHome=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
  root=testRoot??Path.Combine(codexHome,"sessions");
  if(!Directory.Exists(root)){IntroLog.Write("completion-journal-unavailable");return;}
  foreach(string file in RecentFiles())try{offsets[file]=new FileInfo(file).Length;}catch{}
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
 public void Reconcile(){try{foreach(string file in RecentFiles())Read(file);}catch(Exception e){IntroLog.Write("completion-journal-reconcile-error type="+e.GetType().Name);}}
 void Read(string path){
  try{
   if(!path.EndsWith(".jsonl",StringComparison.OrdinalIgnoreCase))return;
   string complete=null;
   lock(gate){
    long offset;
    bool known=offsets.TryGetValue(path,out offset);
    using(FileStream stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){
     if(!known)offset=File.GetCreationTimeUtc(path)>=startedUtc.AddSeconds(-2)?0:stream.Length;
     if(stream.Length<offset)offset=0;
     if(stream.Length==offset){offsets[path]=offset;return;}
     stream.Position=offset;
     using(MemoryStream buffer=new MemoryStream()){
      stream.CopyTo(buffer);
      byte[] bytes=buffer.ToArray();int end=Array.LastIndexOf(bytes,(byte)'\n');
      if(end<0){offsets[path]=offset;return;}
      offsets[path]=offset+end+1;
      complete=Encoding.UTF8.GetString(bytes,0,end+1);
     }
    }
   }
   Match match=SessionId.Match(Path.GetFileName(path));
   if(!match.Success)return;
   using(StringReader reader=new StringReader(complete)){
    string line;
    while((line=reader.ReadLine())!=null){
     string turn,message;long durationMs;
     if(TryParse(line,out turn,out message,out durationMs)){
      string session=match.Value;
      ThreadPool.QueueUserWorkItem(delegate{TaskCompletion.OnJournal(session,turn,message,durationMs);});
     }
    }
   }
  }catch(IOException){}catch(Exception e){IntroLog.Write("completion-journal-read-error type="+e.GetType().Name);}
 }
 public static bool TryParse(string line,out string turn,out string message,out long durationMs){
  turn=null;message=null;durationMs=0;
  if(line==null||line.IndexOf("\"task_complete\"",StringComparison.Ordinal)<0)return false;
  try{
   var record=new JavaScriptSerializer{MaxJsonLength=16777216}.DeserializeObject(line) as Dictionary<string,object>;
   if(record==null||!String.Equals(record["type"] as string,"event_msg",StringComparison.Ordinal))return false;
   var payload=record["payload"] as Dictionary<string,object>;
   if(payload==null||!String.Equals(payload["type"] as string,"task_complete",StringComparison.Ordinal))return false;
   turn=payload["turn_id"] as string;message=payload["last_agent_message"] as string;
   object raw;if(payload.TryGetValue("duration_ms",out raw)&&raw!=null)durationMs=Convert.ToInt64(raw);
   return !String.IsNullOrWhiteSpace(turn)&&!String.IsNullOrWhiteSpace(message);
  }catch{return false;}
 }
 public static bool SelfTest(){
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
  string state=Path.Combine(IntroLog.Data,"turns",key+".candidate");
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
  finally{if(journal!=null)journal.Dispose();if(File.Exists(state))File.Delete(state);if(Directory.Exists(testRoot))Directory.Delete(testRoot,true);}
 }
 public void Dispose(){if(watcher!=null){watcher.EnableRaisingEvents=false;watcher.Dispose();}}
}
