using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal enum CompletionNoticeSize { Small, Medium, Large }
internal enum CompletionNoticeCorner { BottomRight, BottomLeft }

internal sealed class CompletionLayoutPreference {
 internal readonly CompletionNoticeSize Size;
 internal readonly CompletionNoticeCorner Corner;
 internal CompletionLayoutPreference(CompletionNoticeSize size,CompletionNoticeCorner corner){
  if(!Enum.IsDefined(typeof(CompletionNoticeSize),size)||!Enum.IsDefined(typeof(CompletionNoticeCorner),corner))throw new ArgumentException("Unknown layout preset.");
  Size=size;Corner=corner;
 }
 internal static CompletionLayoutPreference Default {get{return new CompletionLayoutPreference(CompletionNoticeSize.Large,CompletionNoticeCorner.BottomRight);}}
 internal double WidthFraction {get{return Size==CompletionNoticeSize.Small?0.30:Size==CompletionNoticeSize.Medium?0.40:0.50;}}
 internal double HeightFraction {get{return Size==CompletionNoticeSize.Small?0.35:Size==CompletionNoticeSize.Medium?0.45:0.55;}}
}

// Independent of scene-settings.json: no migration or edits to validated audio settings.
internal sealed class CompletionLayoutStore {
 internal const int MaxBytes=4096;
 internal readonly string FilePath;
 readonly string mutexName;
 internal CompletionLayoutStore(string path){FilePath=Path.GetFullPath(path);mutexName="Local\\CodexCompletionLayout-"+Hash(Encoding.UTF8.GetBytes(FilePath.ToUpperInvariant()));}
 static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");}
 byte[] ReadBytes(){
  try{using(var stream=new FileStream(FilePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
   if(stream.Length>MaxBytes)throw new InvalidDataException("Layout settings too large.");
   var bytes=new byte[(int)stream.Length];int offset=0,read;
   while(offset<bytes.Length&&(read=stream.Read(bytes,offset,bytes.Length-offset))>0)offset+=read;
   if(offset!=bytes.Length)throw new IOException("Incomplete layout read.");return bytes;
  }}catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}
 }
 internal CompletionLayoutPreference Read(out string revision){var bytes=ReadBytes();revision=bytes==null?"missing":Hash(bytes);return bytes==null?CompletionLayoutPreference.Default:Parse(new UTF8Encoding(false,true).GetString(bytes).TrimStart('\ufeff'));}
 internal static CompletionLayoutPreference Parse(string json){
  var document=new JavaScriptSerializer{MaxJsonLength=MaxBytes,RecursionLimit=4}.DeserializeObject(json) as Dictionary<string,object>;
  if(document==null||document.Count!=3||!document.ContainsKey("schemaVersion")||!(document["schemaVersion"] is int)||(int)document["schemaVersion"]!=1||!document.ContainsKey("size")||!(document["size"] is string)||!document.ContainsKey("corner")||!(document["corner"] is string))throw new InvalidDataException("Unsupported layout schema.");
  string size=(string)document["size"],corner=(string)document["corner"];
  CompletionNoticeSize parsedSize;CompletionNoticeCorner parsedCorner;
  if(!Enum.TryParse(size,out parsedSize)||!Enum.TryParse(corner,out parsedCorner)||parsedSize.ToString()!=size||parsedCorner.ToString()!=corner)throw new InvalidDataException("Unknown layout preset.");
  return new CompletionLayoutPreference(parsedSize,parsedCorner);
 }
 internal static string Serialize(CompletionLayoutPreference value){return new JavaScriptSerializer().Serialize(new{schemaVersion=1,size=value.Size.ToString(),corner=value.Corner.ToString()});}
 internal string Save(CompletionLayoutPreference value,string expectedRevision){
  string json=Serialize(value);Parse(json);
  using(var mutex=new Mutex(false,mutexName)){
   bool acquired=false;string staged=null;
   try{
    try{acquired=mutex.WaitOne(1000);}catch(AbandonedMutexException){acquired=true;}
    if(!acquired)throw new IOException("Layout writer busy.");
    string revision;Read(out revision);if(revision!=expectedRevision)throw new InvalidOperationException("Layout changed in another window.");
    string folder=Path.GetDirectoryName(FilePath);Directory.CreateDirectory(folder);
    staged=FilePath+".pending-"+Guid.NewGuid().ToString("N");byte[] bytes=new UTF8Encoding(false).GetBytes(json);
    using(var stream=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
    if(File.Exists(FilePath)){string backup=Path.Combine(folder,"settings-backups");Directory.CreateDirectory(backup);File.Replace(staged,FilePath,Path.Combine(backup,"completion-layout-"+Guid.NewGuid().ToString("N")+".json"));}
    else File.Move(staged,FilePath);
    staged=null;return Hash(bytes);
   }finally{if(staged!=null&&File.Exists(staged))try{File.Delete(staged);}catch{}if(acquired)mutex.ReleaseMutex();}
  }
 }
}

internal static class CompletionLayoutSettings {
 internal static readonly CompletionLayoutStore Store=new CompletionLayoutStore(Path.Combine(RuntimeData.Root,"completion-layout.json"));
 internal static CompletionLayoutPreference Current=CompletionLayoutPreference.Default;
 // Only one-shot completion players read this, before window creation. No new-chat I/O.
 internal static void Initialize(){try{string revision;Current=Store.Read(out revision);}catch(Exception e){Current=CompletionLayoutPreference.Default;IntroLog.Write("completion-layout-read-failed type="+e.GetType().Name+" default-used=True");}}
}
