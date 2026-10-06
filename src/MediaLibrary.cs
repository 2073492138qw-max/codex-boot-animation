using System;
using System.Collections.Generic;
using System.IO;

internal enum VideoScene { Startup, NewChat, Anger, Completion, Idle }

// The directory is the source of truth: one MP4 is fixed, several are random.
// A scene never falls back to another scene's videos.
internal static class MediaLibrary {
 public const double PlaybackVolume=0.65;
 public const double StartupVolume=0.85;
 public static readonly string Folder=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","videos"));
 static readonly Random RandomChoice=new Random();
 static string[] angerFiles=new string[0];
 static readonly List<string> angerRemaining=new List<string>();
 static string lastAnger;

 public static string FolderFor(VideoScene scene){
  string name;
  switch(scene){
   case VideoScene.Startup:name="冷启动";break;
   case VideoScene.NewChat:name="新聊天";break;
   case VideoScene.Anger:name="生气回应";break;
   case VideoScene.Completion:name="任务完成";break;
   case VideoScene.Idle:name="闲置互动";break;
   default:throw new ArgumentOutOfRangeException("scene");
  }
  return Path.Combine(Folder,name);
 }

 public static string[] List(VideoScene scene){
  string directory=FolderFor(scene);
  if(!Directory.Exists(directory))return new string[0];
  string[] files=Directory.GetFiles(directory,"*.mp4",SearchOption.TopDirectoryOnly);
  Array.Sort(files,StringComparer.OrdinalIgnoreCase);
  return files;
 }

 internal static string Pick(string[] files,int index){return files.Length==0?null:files[index%files.Length];}
 static string Select(VideoScene scene){
  string[] files=List(scene);
  lock(RandomChoice){
   if(scene!=VideoScene.Anger)return Pick(files,files.Length==0?0:RandomChoice.Next(files.Length));
   if(files.Length==0){angerFiles=files;angerRemaining.Clear();lastAnger=null;return null;}
   bool changed=files.Length!=angerFiles.Length;
   if(!changed)for(int i=0;i<files.Length;i++)if(!String.Equals(files[i],angerFiles[i],StringComparison.OrdinalIgnoreCase)){changed=true;break;}
   if(changed){angerFiles=files;angerRemaining.Clear();}
   if(angerRemaining.Count==0){
    angerRemaining.AddRange(files);
    for(int i=angerRemaining.Count-1;i>0;i--){int j=RandomChoice.Next(i+1);string temp=angerRemaining[i];angerRemaining[i]=angerRemaining[j];angerRemaining[j]=temp;}
    int last=angerRemaining.Count-1;
    if(last>0&&String.Equals(angerRemaining[last],lastAnger,StringComparison.OrdinalIgnoreCase)){
     string temp=angerRemaining[last];angerRemaining[last]=angerRemaining[0];angerRemaining[0]=temp;
    }
   }
   int chosen=angerRemaining.Count-1;
   string path=angerRemaining[chosen];angerRemaining.RemoveAt(chosen);lastAnger=path;
   return path;
  }
 }
 public static string Startup(){return Select(VideoScene.Startup);}
 public static string Anger(){return Select(VideoScene.Anger);}
 public static string Completion(){return Select(VideoScene.Completion);}
 public static string Idle(){return Select(VideoScene.Idle);}
 public static string[] Prepared(){
  var files=new List<string>();
  foreach(VideoScene scene in new[]{VideoScene.Startup,VideoScene.NewChat})files.AddRange(List(scene));
  return files.ToArray();
 }
 internal static bool IsStartupReason(string reason){return reason=="desktop-start"||reason=="preview-startup";}
 public static double VolumeForReason(string reason){return IsStartupReason(reason)?StartupVolume:PlaybackVolume;}
 internal static VideoScene? SceneForReason(string reason){
  if(IsStartupReason(reason))return VideoScene.Startup;
  if(reason=="reaction-angry"||reason=="preview-anger")return VideoScene.Anger;
  if(reason=="idle-return"||reason=="preview-idle")return VideoScene.Idle;
  if(reason=="new-dialog-click"||reason=="new-dialog-log-fallback"||reason=="preview-new-chat")return VideoScene.NewChat;
  return null;
 }
 public static string ForReason(string reason){VideoScene? scene=SceneForReason(reason);return scene.HasValue?Select(scene.Value):null;}
 public static string FirstFrame(string video){return Path.Combine(Path.GetDirectoryName(video),Path.GetFileNameWithoutExtension(video)+"-first.png");}

 internal static bool SelfTest(){
  string[] sample={"first.mp4","second.mp4"};
  if(Pick(sample,0)!=sample[0]||Pick(sample,1)!=sample[1]||Pick(sample,2)!=sample[0])return false;
  if(Pick(new[]{sample[0]},7)!=sample[0]||Pick(new string[0],0)!=null)return false;
  if(SceneForReason("desktop-start")!=VideoScene.Startup||SceneForReason("new-dialog-click")!=VideoScene.NewChat)return false;
  if(SceneForReason("reaction-angry")!=VideoScene.Anger||SceneForReason("idle-return")!=VideoScene.Idle||SceneForReason("unexpected")!=null)return false;
  double startupVolume=VolumeForReason("desktop-start");
  if(startupVolume<=PlaybackVolume||startupVolume>1.0)return false;
  if(VolumeForReason("desktop-start")!=StartupVolume||VolumeForReason("preview-startup")!=StartupVolume)return false;
  foreach(string reason in new[]{"new-dialog-click","new-dialog-log-fallback","preview-new-chat","reaction-angry","preview-anger","idle-return","preview-idle","task-complete","preview-completion","unexpected"})if(VolumeForReason(reason)!=PlaybackVolume)return false;
  foreach(VideoScene scene in new[]{VideoScene.Startup,VideoScene.NewChat,VideoScene.Anger,VideoScene.Completion,VideoScene.Idle}){
   foreach(string path in List(scene))if(!String.Equals(Path.GetDirectoryName(path),FolderFor(scene),StringComparison.OrdinalIgnoreCase))return false;
  }
  foreach(string reason in new[]{"desktop-start","new-dialog-click","reaction-angry"}){
   VideoScene scene=SceneForReason(reason).Value;
   string chosen=ForReason(reason);
   if(chosen!=null&&!String.Equals(Path.GetDirectoryName(chosen),FolderFor(scene),StringComparison.OrdinalIgnoreCase))return false;
  }
  string[] anger=List(VideoScene.Anger);
  if(anger.Length>1){
   lock(RandomChoice){angerFiles=new string[0];angerRemaining.Clear();lastAnger=null;}
   string previous=null;
   for(int round=0;round<2;round++){
    var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for(int i=0;i<anger.Length;i++){
     string chosen=Anger();
     if(chosen==null||String.Equals(chosen,previous,StringComparison.OrdinalIgnoreCase)||!seen.Add(chosen))return false;
     previous=chosen;
    }
    if(seen.Count!=anger.Length)return false;
   }
  }
  return true;
 }
}
