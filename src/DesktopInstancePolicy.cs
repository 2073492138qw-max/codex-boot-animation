using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Security.Cryptography;
using System.Text;

// Process start ticks distinguish PID reuse. Claims survive watcher recovery and logon races.
internal static class DesktopInstancePolicy {
 static readonly System.Collections.Concurrent.ConcurrentDictionary<string,bool> known = new System.Collections.Concurrent.ConcurrentDictionary<string,bool>();
 internal static bool IsCodex(Process process) {
  if (!String.Equals(process.ProcessName,"ChatGPT",StringComparison.OrdinalIgnoreCase)) return false;
  string identity=Identity(process); bool result;
  if (known.TryGetValue(identity,out result)) return result;
  result=process.MainModule.FileName.IndexOf("OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)>=0;
  if (known.Count>256) known.Clear();
  known[identity]=result; return result;
 }
 internal static string Identity(Process process){return process.Id+":"+process.StartTime.ToUniversalTime().Ticks;}
 internal static bool Claim(string identity,string folder){
  Directory.CreateDirectory(folder);
  string key;using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","");
  try{using(var file=new FileStream(Path.Combine(folder,key+".claim"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){}return true;}catch(IOException){return false;}
 }
 internal static bool Claim(string identity){return Claim(identity,Path.Combine(IntroLog.Data,"desktop-instances"));}
 internal static void AdoptRunning(){foreach(var process in Process.GetProcessesByName("ChatGPT"))using(process){try{if(IsCodex(process))Claim(Identity(process));}catch{}}}
 internal static bool SelfTest(){
  string folder=Path.Combine(Path.GetTempPath(),"codex-desktop-policy-"+Guid.NewGuid().ToString("N"));
  try{
   if(!Claim("123:100",folder)||Claim("123:100",folder)||!Claim("123:101",folder)||!Claim("124:100",folder))return false;
   int winners=0;System.Threading.Tasks.Parallel.For(0,16,delegate(int i){if(Claim("concurrent:200",folder))Interlocked.Increment(ref winners);});return winners==1;
  }
  finally{if(Directory.Exists(folder)){foreach(string path in Directory.GetFiles(folder,"*.claim"))File.Delete(path);Directory.Delete(folder);}}
 }
}
