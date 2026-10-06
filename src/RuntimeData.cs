using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// LocalAppData can be redirected inside packaged Codex. Keep the companion
// and hooks on one user-profile path, with an explicit, reversible migration.
internal static class RuntimeData {
 internal static string Root {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex-boot-animation");}}
 static readonly Regex Turn=new Regex(@"^(?:[a-f0-9]{64}\.(?:candidate|claimed)|session-[a-f0-9]{64}\.candidate)$");
 static readonly Regex Desktop=new Regex(@"^[A-Fa-f0-9]{64}\.claim$");
 static IEnumerable<string> States(string root){
  foreach(string flag in new[]{"paused","idle-disabled"})if(File.Exists(Path.Combine(root,flag)))yield return flag;
  foreach(string name in new[]{"turns","desktop-instances"}){
   string folder=Path.Combine(root,name);if(!Directory.Exists(folder))continue;
   foreach(string file in Directory.GetFiles(folder)){string leaf=Path.GetFileName(file);if((name=="turns"?Turn:Desktop).IsMatch(leaf))yield return Path.Combine(name,leaf);}
  }
 }
 static void Validate(string relative,string file){
  if(new FileInfo(file).Length>512)throw new InvalidDataException("Runtime state is unexpectedly large.");
  if(relative.EndsWith(".candidate",StringComparison.Ordinal)){
   string[] parts=File.ReadAllText(file).Trim().Split('|');
   if((parts[0]!="0"&&parts[0]!="1"&&parts[0]!="x")||parts.Length>2||(parts.Length==2&&!Regex.IsMatch(parts[1],"^[a-f0-9]{64}$")))throw new InvalidDataException("Runtime candidate is malformed.");
  }
 }
 internal static string ExportLegacy(){
  var sources=new List<string>{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexBootAnimation")};
  string packages=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),@"AppData\Local\Packages");
  if(Directory.Exists(packages))foreach(string package in Directory.GetDirectories(packages,"OpenAI.Codex_*"))sources.Add(Path.Combine(package,@"LocalCache\Local\CodexBootAnimation"));
  string snapshot=Path.Combine(Root,"migration","snapshot-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(snapshot);
  int index=0,count=0;
  foreach(string source in sources.Distinct(StringComparer.OrdinalIgnoreCase)){
   if(!Directory.Exists(source))continue;string target=Path.Combine(snapshot,(index++).ToString());
   foreach(string relative in States(source)){
    string file=Path.Combine(source,relative);Validate(relative,file);string copy=Path.Combine(target,relative);
    Directory.CreateDirectory(Path.GetDirectoryName(copy));File.Copy(file,copy,false);File.SetLastWriteTimeUtc(copy,File.GetLastWriteTimeUtc(file));count++;
   }
  }
  File.WriteAllText(Path.Combine(snapshot,"ready"),count.ToString());IntroLog.Write("runtime-legacy-export files="+count+" sources="+index);return snapshot;
 }
 internal static int Merge(string root,IEnumerable<string> sources,bool apply){
  var choices=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  foreach(string source in new[]{root}.Concat(sources))foreach(string relative in States(source)){
   string file=Path.Combine(source,relative);Validate(relative,file);
   if(relative.EndsWith(".candidate",StringComparison.Ordinal)&&File.GetLastWriteTimeUtc(file)<DateTime.UtcNow.AddDays(-1))continue;
   string previous;if(!choices.TryGetValue(relative,out previous)){choices[relative]=file;continue;}
   if(!relative.EndsWith(".candidate",StringComparison.Ordinal))continue;
   int comparison=DateTime.Compare(File.GetLastWriteTimeUtc(file),File.GetLastWriteTimeUtc(previous));
   if(comparison>0)choices[relative]=file;
   else if(comparison==0&&File.ReadAllText(file)!=File.ReadAllText(previous))throw new InvalidDataException("Same-time candidate conflict; migration stopped without changes.");
  }
  var pending=new List<KeyValuePair<string,string>>();
  foreach(var pair in choices){
   string relative=pair.Key;
   if(relative.EndsWith(".candidate",StringComparison.Ordinal)){
    string origin=Path.GetFileNameWithoutExtension(relative);
    if(origin.StartsWith("session-",StringComparison.Ordinal)){string[] values=File.ReadAllText(pair.Value).Trim().Split('|');if(values.Length!=2)throw new InvalidDataException("Session candidate has no origin.");origin=values[1];}
    if(choices.ContainsKey(Path.Combine("turns",origin+".claimed")))continue;
   }
   string target=Path.Combine(root,relative);if(String.Equals(Path.GetFullPath(target),Path.GetFullPath(pair.Value),StringComparison.OrdinalIgnoreCase))continue;
   if(File.Exists(target)&&File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(pair.Value)))continue;
   pending.Add(pair);
  }
  if(!apply)return pending.Count;
  string backup=Path.Combine(root,"migration","merge-backup-"+Guid.NewGuid().ToString("N"));
  foreach(var pair in pending){string target=Path.Combine(root,pair.Key);if(File.Exists(target)){string saved=Path.Combine(backup,pair.Key);Directory.CreateDirectory(Path.GetDirectoryName(saved));File.Copy(target,saved,false);}}
  var touched=new List<string>();
  try{foreach(var pair in pending){
   string target=Path.Combine(root,pair.Key);Directory.CreateDirectory(Path.GetDirectoryName(target));string staged=target+".migrating-"+Guid.NewGuid().ToString("N");
   try{File.Copy(pair.Value,staged,false);File.SetLastWriteTimeUtc(staged,File.GetLastWriteTimeUtc(pair.Value));if(File.Exists(target))File.Replace(staged,target,null);else File.Move(staged,target);touched.Add(pair.Key);}
   finally{if(File.Exists(staged))File.Delete(staged);}
  }}catch{
   foreach(string relative in touched){string target=Path.Combine(root,relative),saved=Path.Combine(backup,relative);if(File.Exists(saved))File.Copy(saved,target,true);else if(File.Exists(target))File.Delete(target);}
   throw;
  }
  return pending.Count;
 }
 internal static int MergeLegacy(bool apply){
  string folder=Path.Combine(Root,"migration");var sources=new List<string>();
  if(Directory.Exists(folder))foreach(string snapshot in Directory.GetDirectories(folder,"snapshot-*"))if(File.Exists(Path.Combine(snapshot,"ready")))sources.AddRange(Directory.GetDirectories(snapshot));
  int count=Merge(Root,sources,apply);IntroLog.Write("runtime-legacy-merge apply="+apply+" changed="+count);return count;
 }
 internal static bool Probe(string token){
  if(!Regex.IsMatch(token??"","^[a-f0-9]{32}$"))return false;
  string folder=Path.Combine(Root,"probes");Directory.CreateDirectory(folder);
  using(var process=Process.GetCurrentProcess())File.AppendAllText(Path.Combine(folder,token),process.Id+"|"+CompanionLifetime.InJob()+Environment.NewLine);
  return true;
 }
 internal static bool SelfTest(){
  string fixture=Path.Combine(Path.GetTempPath(),"codex-runtime-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
  try{
   string root=Path.Combine(fixture,"shared"),a=Path.Combine(fixture,"a"),b=Path.Combine(fixture,"b"),key=new string('a',64),relative=Path.Combine("turns",key+".candidate");
   Directory.CreateDirectory(Path.Combine(a,"turns"));Directory.CreateDirectory(Path.Combine(b,"turns"));
   File.WriteAllText(Path.Combine(a,relative),"0");File.SetLastWriteTimeUtc(Path.Combine(a,relative),DateTime.UtcNow.AddMinutes(-2));File.WriteAllText(Path.Combine(b,relative),"1");
   File.WriteAllText(Path.Combine(a,"paused"),"paused");File.WriteAllText(Path.Combine(a,"playback.log"),"private historical log");
   if(Merge(root,new[]{a,b},false)!=2||Directory.Exists(root))return false;
   if(Merge(root,new[]{a,b},true)!=2||File.ReadAllText(Path.Combine(root,relative))!="1"||File.Exists(Path.Combine(root,"playback.log")))return false;
   if(Merge(root,new[]{a,b},true)!=0||!File.Exists(Path.Combine(a,"playback.log")))return false;
   File.WriteAllText(Path.Combine(b,"turns",key+".claimed"),"");Merge(root,new[]{a,b},true);if(!File.Exists(Path.Combine(root,"turns",key+".claimed")))return false;
   string second=Path.Combine(fixture,"conflict"),next=new string('b',64)+".candidate";DateTime equal=DateTime.UtcNow.AddMinutes(-1);
   File.WriteAllText(Path.Combine(a,"turns",next),"0");File.WriteAllText(Path.Combine(b,"turns",next),"x");File.SetLastWriteTimeUtc(Path.Combine(a,"turns",next),equal);File.SetLastWriteTimeUtc(Path.Combine(b,"turns",next),equal);
   bool rejected=false;try{Merge(second,new[]{a,b},true);}catch(InvalidDataException){rejected=true;}return rejected&&!Directory.Exists(second);
  }finally{foreach(string file in Directory.GetFiles(fixture,"*",SearchOption.AllDirectories))File.Delete(file);foreach(string folder in Directory.GetDirectories(fixture,"*",SearchOption.AllDirectories).OrderByDescending(x=>x.Length))Directory.Delete(folder);Directory.Delete(fixture);}
 }
}
