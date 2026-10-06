using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Security.Cryptography;

// A logical request can span several engine turns. Persist only its kind and
// a hashed origin key, never the prompt or the answer. New user input replaces it.
internal static class CompletionStateStore {
 internal static string TestFolder;
 internal static string Folder {get{return TestFolder??Path.Combine(IntroLog.Data,"turns");}}
 internal static string Hash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
 internal static string Key(string session,string turn){return Hash(session+"\n"+turn);}
 static string ScopePath(string session){return Path.Combine(Folder,"session-"+Hash(session)+".candidate");}
 internal static string TurnPath(string key){return Path.Combine(Folder,key+".candidate");}
 internal static bool Claimed(string key){return File.Exists(Path.Combine(Folder,key+".claimed"));}
 internal sealed class ScopeLock:IDisposable {
  readonly Mutex mutex;
  internal ScopeLock(string session){mutex=new Mutex(false,"Local\\CodexCompletionState-"+Hash(session));bool entered;try{entered=mutex.WaitOne(3000);}catch(AbandonedMutexException){entered=true;}if(!entered){mutex.Dispose();throw new TimeoutException("Completion state is busy.");}}
  public void Dispose(){mutex.ReleaseMutex();mutex.Dispose();}
 }
 internal sealed class Resolution {
  internal string Kind,OriginKey,Source;
 }
 internal static void Record(string session,string turn,string kind){
  using(new ScopeLock(session)){
   Directory.CreateDirectory(Folder);
   string key=Key(session,turn);
   File.WriteAllText(TurnPath(key),kind);
   File.WriteAllText(ScopePath(session),kind+"|"+key);
   foreach(string pattern in new[]{"*.candidate","*.claimed"})foreach(string file in Directory.GetFiles(Folder,pattern))try{if(File.GetLastWriteTimeUtc(file)<DateTime.UtcNow.AddDays(-1))File.Delete(file);}catch{}
  }
 }
 internal static Resolution Resolve(string session,string key){
  string direct=TurnPath(key);
  if(File.Exists(direct))return new Resolution{Kind=File.ReadAllText(direct).Trim(),OriginKey=key,Source="turn"};
  string scope=ScopePath(session);
  if(!File.Exists(scope)||File.GetLastWriteTimeUtc(scope)<DateTime.UtcNow.AddDays(-1))return null;
  string[] parts=File.ReadAllText(scope).Trim().Split('|');
  if(parts.Length!=2||(parts[0]!="1"&&parts[0]!="0"&&parts[0]!="x")||parts[1].Length!=64||Claimed(parts[1]))return null;
  return new Resolution{Kind=parts[0],OriginKey=parts[1],Source="session-inherited"};
 }
 internal static bool Claim(string key){
  Directory.CreateDirectory(Folder);
  try{using(new FileStream(Path.Combine(Folder,key+".claimed"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){}return true;}catch(IOException){return false;}
 }
 internal static void Consume(string session,string key,Resolution state){
  string direct=TurnPath(key);if(File.Exists(direct))File.Delete(direct);
  if(state==null)return;
  if(state.OriginKey!=key)Claim(state.OriginKey);
  // Never remove a newer request's scope when an older turn ends.
  string scope=ScopePath(session);
  if(File.Exists(scope)&&File.ReadAllText(scope).Trim().EndsWith("|"+state.OriginKey,StringComparison.Ordinal))File.Delete(scope);
  string origin=TurnPath(state.OriginKey);if(File.Exists(origin))File.Delete(origin);
 }
}
