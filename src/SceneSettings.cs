using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

// Immutable snapshots: playback reads memory, never JSON on the click path.
internal sealed class ScenePreference {
 internal readonly bool Enabled, Muted;
 internal readonly int VolumePercent;
 internal ScenePreference(bool enabled, bool muted, int volume) {
  if(volume<0||volume>100)throw new ArgumentOutOfRangeException("volume");
  Enabled=enabled;Muted=muted;VolumePercent=volume;
 }
 internal double Volume {get{return VolumePercent/100.0;}}
}
internal sealed class SceneSettingsSnapshot {
 readonly ScenePreference[] values;
 internal SceneSettingsSnapshot(ScenePreference[] preferences) {
  if(preferences==null||preferences.Length!=5)throw new ArgumentException("Five scenes required.");
  values=(ScenePreference[])preferences.Clone();
  foreach(ScenePreference value in values)if(value==null)throw new ArgumentException("Missing scene.");
 }
 internal ScenePreference Get(VideoScene scene){return values[(int)scene];}
 internal SceneSettingsSnapshot With(VideoScene scene,ScenePreference value){var copy=(ScenePreference[])values.Clone();copy[(int)scene]=value;return new SceneSettingsSnapshot(copy);}
 internal static SceneSettingsSnapshot Defaults(bool legacyIdleDisabled=false){
  var values=new ScenePreference[5];
  for(int i=0;i<5;i++)values[i]=new ScenePreference(i!=(int)VideoScene.Idle||!legacyIdleDisabled,false,i==(int)VideoScene.Startup?85:65);
  return new SceneSettingsSnapshot(values);
 }
 internal static SceneSettingsSnapshot SafeDisabled(){var result=Defaults();foreach(VideoScene scene in Enum.GetValues(typeof(VideoScene)))result=result.With(scene,new ScenePreference(false,false,result.Get(scene).VolumePercent));return result;}
}

// Only this new file is replaced; old runtime flags, videos and Codex are untouched.
internal sealed class SceneSettingsStore {
 internal const int MaxBytes=8192;
 internal readonly string FilePath;
 readonly string mutexName;
 internal SceneSettingsStore(string path){
  FilePath=Path.GetFullPath(path);
  using(var sha=SHA256.Create())mutexName="Local\\CodexSceneSettings-"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(FilePath.ToUpperInvariant()))).Replace("-","");
 }
 static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");}
 byte[] ReadBytes(){
  try{using(var stream=new FileStream(FilePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
   if(stream.Length>MaxBytes)throw new InvalidDataException("Settings too large.");
   var bytes=new byte[(int)stream.Length];int offset=0,read;
   while(offset<bytes.Length&&(read=stream.Read(bytes,offset,bytes.Length-offset))>0)offset+=read;
   if(offset!=bytes.Length)throw new IOException("Incomplete settings read.");return bytes;
  }}catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}
 }
 internal SceneSettingsSnapshot Read(out string revision){
  byte[] bytes=ReadBytes();
  if(bytes==null){bool idle=File.Exists(Path.Combine(Path.GetDirectoryName(FilePath),"idle-disabled"));revision="missing:"+idle;return SceneSettingsSnapshot.Defaults(idle);}
  revision=Hash(bytes);return Parse(new UTF8Encoding(false,true).GetString(bytes).TrimStart('\ufeff'));
 }
 internal static SceneSettingsSnapshot Parse(string json){
  var parser=new JavaScriptSerializer{MaxJsonLength=MaxBytes,RecursionLimit=8};
  var document=parser.DeserializeObject(json) as Dictionary<string,object>;
  if(document==null||document.Count!=2||!document.ContainsKey("schemaVersion")||!(document["schemaVersion"] is int)||(int)document["schemaVersion"]!=1||!document.ContainsKey("scenes"))throw new InvalidDataException("Unsupported settings schema.");
  var scenes=document["scenes"] as Dictionary<string,object>;
  if(scenes==null||scenes.Count!=5)throw new InvalidDataException("Five scenes required.");
  var values=new ScenePreference[5];
  foreach(VideoScene scene in Enum.GetValues(typeof(VideoScene))){
   object raw;var item=scenes.TryGetValue(scene.ToString(),out raw)?raw as Dictionary<string,object>:null;
   if(item==null||item.Count!=3||!item.ContainsKey("enabled")||!(item["enabled"] is bool)||!item.ContainsKey("muted")||!(item["muted"] is bool)||!item.ContainsKey("volumePercent")||!(item["volumePercent"] is int))throw new InvalidDataException("Malformed scene preference.");
   values[(int)scene]=new ScenePreference((bool)item["enabled"],(bool)item["muted"],(int)item["volumePercent"]);
  }
  return new SceneSettingsSnapshot(values);
 }
 internal static string Serialize(SceneSettingsSnapshot snapshot){
  var scenes=new Dictionary<string,object>();
  foreach(VideoScene scene in Enum.GetValues(typeof(VideoScene))){ScenePreference value=snapshot.Get(scene);scenes.Add(scene.ToString(),new{enabled=value.Enabled,muted=value.Muted,volumePercent=value.VolumePercent});}
  return new JavaScriptSerializer().Serialize(new{schemaVersion=1,scenes=scenes});
 }
 internal string Save(SceneSettingsSnapshot snapshot,string expectedRevision){
  // Serialize/validate before touching disk; compare revision under a bounded lock.
  string json=Serialize(snapshot);Parse(json);
  using(var mutex=new Mutex(false,mutexName)){
   bool acquired=false;string staged=null;
   try{
    try{acquired=mutex.WaitOne(1000);}catch(AbandonedMutexException){acquired=true;}
    if(!acquired)throw new IOException("Settings writer busy.");
    string currentRevision;Read(out currentRevision);
    if(currentRevision!=expectedRevision)throw new InvalidOperationException("Settings changed in another window; reopen before saving.");
    string folder=Path.GetDirectoryName(FilePath);Directory.CreateDirectory(folder);
    staged=FilePath+".pending-"+Guid.NewGuid().ToString("N");
    byte[] bytes=new UTF8Encoding(false).GetBytes(json);
    using(var stream=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
    if(File.Exists(FilePath)){
     string backupFolder=Path.Combine(folder,"settings-backups");Directory.CreateDirectory(backupFolder);
     File.Replace(staged,FilePath,Path.Combine(backupFolder,"scene-settings-"+Guid.NewGuid().ToString("N")+".json"));
    }else File.Move(staged,FilePath);
    staged=null;return Hash(bytes);
   }finally{if(staged!=null&&File.Exists(staged))try{File.Delete(staged);}catch{}if(acquired)mutex.ReleaseMutex();}
  }
 }
}

internal static class SceneSettings {
 internal static readonly SceneSettingsStore Store=new SceneSettingsStore(Path.Combine(RuntimeData.Root,"scene-settings.json"));
 static SceneSettingsSnapshot current=SceneSettingsSnapshot.Defaults();
 static readonly object refreshLock=new object();
 static FileSystemWatcher watcher;
 static int queued,dirty;
 static bool loadedValid;
 static string lastError;
 internal static SceneSettingsSnapshot Current {get{return Volatile.Read(ref current);}}
 internal static string LastError {get{lock(refreshLock)return lastError;}}
 internal static void Refresh(){
  lock(refreshLock)try{string revision;var next=Store.Read(out revision);Volatile.Write(ref current,next);loadedValid=true;lastError=null;}
  catch(Exception e){if(!loadedValid)Volatile.Write(ref current,SceneSettingsSnapshot.SafeDisabled());string error=e.GetType().Name;if(lastError!=error)IntroLog.Write("scene-settings-read-failed type="+error);lastError=error;}
 }
 static void QueueRefresh(){
  Interlocked.Exchange(ref dirty,1);if(Interlocked.Exchange(ref queued,1)!=0)return;
  ThreadPool.QueueUserWorkItem(delegate{try{while(Interlocked.Exchange(ref dirty,0)!=0)Refresh();}finally{Interlocked.Exchange(ref queued,0);if(Volatile.Read(ref dirty)!=0)QueueRefresh();}});
 }
 internal static void Initialize(bool watchChanges){
  Refresh();if(!watchChanges||watcher!=null)return;
  try{Directory.CreateDirectory(RuntimeData.Root);watcher=new FileSystemWatcher(RuntimeData.Root,"scene-settings.json"){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size};
   watcher.Changed+=delegate{QueueRefresh();};watcher.Created+=delegate{QueueRefresh();};watcher.Deleted+=delegate{QueueRefresh();};watcher.Renamed+=delegate{QueueRefresh();};
   watcher.Error+=delegate{QueueRefresh();};watcher.EnableRaisingEvents=true;Refresh();
  }catch(Exception e){IntroLog.Write("scene-settings-watch-failed type="+e.GetType().Name);}
 }
 internal static bool Allows(SceneSettingsSnapshot snapshot,string reason){
  if(reason!=null&&reason.StartsWith("preview-",StringComparison.Ordinal))return true;
  VideoScene? scene=reason=="task-complete"?VideoScene.Completion:MediaLibrary.SceneForReason(reason);
  return !scene.HasValue||snapshot.Get(scene.Value).Enabled;
 }
 internal static bool Allows(string reason){return Allows(Current,reason);}
 internal static ScenePreference ForReason(string reason){VideoScene? scene=MediaLibrary.SceneForReason(reason);return Current.Get(scene??VideoScene.NewChat);}
 internal static void WithTestSnapshot(SceneSettingsSnapshot snapshot,Action test){var previous=Current;try{Volatile.Write(ref current,snapshot);test();}finally{Volatile.Write(ref current,previous);}}
 internal static void PublishSaved(){Refresh();IntroWatcher.SignalResident("__settings_reload__");IntroLog.Write("scene-settings-saved");}
}
