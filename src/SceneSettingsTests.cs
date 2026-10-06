using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;

// Render fixture controls without a visible window or foreground activation.
internal sealed class SilentSettingsTestForm:SceneSettingsForm {
 internal SilentSettingsTestForm(SceneSettingsStore store,Action notify):base(store,notify){Opacity=0;ShowInTaskbar=false;}
 protected override bool ShowWithoutActivation {get{return true;}}
 protected override System.Windows.Forms.CreateParams CreateParams {get{var parameters=base.CreateParams;parameters.ExStyle|=0x08000000;return parameters;}}
}

internal static class SceneSettingsTests {
 static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
 internal static bool Run(){
  string root=Path.Combine(Path.GetTempPath(),"codex-scene-settings-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   var store=new SceneSettingsStore(Path.Combine(root,"scene-settings.json"));string revision;
   var defaults=store.Read(out revision);Require(revision=="missing:False"&&!File.Exists(store.FilePath),"Reading defaults wrote a file.");
   foreach(VideoScene scene in Enum.GetValues(typeof(VideoScene))){var preference=defaults.Get(scene);Require(preference.Enabled&&!preference.Muted&&preference.VolumePercent==(scene==VideoScene.Startup?85:65),"Defaults changed.");}
   foreach(var item in new[]{new{Scene=VideoScene.Startup,Reason="desktop-start"},new{Scene=VideoScene.NewChat,Reason="new-dialog-click"},new{Scene=VideoScene.Anger,Reason="reaction-angry"},new{Scene=VideoScene.Completion,Reason="task-complete"},new{Scene=VideoScene.Idle,Reason="idle-return"}}){
    var onlyOff=defaults.With(item.Scene,new ScenePreference(false,true,0));
    Require(!SceneSettings.Allows(onlyOff,item.Reason),"Automatic scene escaped disable.");
    foreach(VideoScene other in Enum.GetValues(typeof(VideoScene)))if(other!=item.Scene)Require(onlyOff.Get(other).Enabled&&onlyOff.Get(other).VolumePercent==defaults.Get(other).VolumePercent,"Scene isolation failed.");
    foreach(string preview in new[]{"preview-startup","preview-new-chat","preview-anger","preview-completion","preview-idle"})Require(SceneSettings.Allows(onlyOff,preview),"Disable broke manual preview.");
   }
   var audio=defaults;
   foreach(VideoScene scene in Enum.GetValues(typeof(VideoScene)))audio=audio.With(scene,new ScenePreference(true,((int)scene)%2==0,10+10*(int)scene));
   string[] videos=MediaLibrary.Prepared();Require(videos.Length>0,"Player fixture video missing.");
   SceneSettings.WithTestSnapshot(audio,delegate{
    foreach(VideoScene scene in new[]{VideoScene.Anger,VideoScene.Completion,VideoScene.Idle}){
     var window=new CodexVideoWindow(IntPtr.Zero,videos[0],scene,true);
     try{var media=((Grid)window.Content).Children.OfType<MediaElement>().Single();Require(media.Volume==audio.Get(scene).Volume&&media.IsMuted==audio.Get(scene).Muted&&media.SpeedRatio==1.0,"Codex-window audio preferences were not applied.");}finally{window.Close();}
    }
    foreach(string reason in new[]{"preview-startup","preview-new-chat"}){
     var intro=new IntroWindow(videos[0],"--play",reason);
     try{var media=((Grid)intro.Content).Children.OfType<MediaElement>().Single();var preference=SceneSettings.ForReason(reason);Require(media.Volume==preference.Volume&&media.IsMuted==preference.Muted&&media.SpeedRatio==1.2,"Intro audio preference or speed changed.");}finally{intro.Close();}
    }
   });
   var off=defaults.With(VideoScene.NewChat,new ScenePreference(false,false,65)).With(VideoScene.Anger,new ScenePreference(false,false,65)).With(VideoScene.Idle,new ScenePreference(false,false,65));
   SceneSettings.WithTestSnapshot(off,delegate{var intro=new IntroWindow(videos[0],"--resident","preview-new-chat");try{intro.PlayResident("new-dialog-click");intro.PlayResident("new-dialog-log-fallback");intro.PlayResident("reaction-angry");string why;Require(!intro.IsVisible&&!intro.TryPlayIdle("idle-return",out why),"Disabled automatic scene opened a player.");}finally{intro.Close();}});
   File.WriteAllText(Path.Combine(root,"idle-disabled"),"disabled");defaults=store.Read(out revision);
   Require(!defaults.Get(VideoScene.Idle).Enabled,"Legacy idle disable lost.");
   var wanted=defaults.With(VideoScene.NewChat,new ScenePreference(false,true,25)).With(VideoScene.Completion,new ScenePreference(true,false,100));
   string next=store.Save(wanted,revision);var loaded=store.Read(out revision);
   Require(revision==next&&loaded.Get(VideoScene.NewChat).Volume==0.25&&!loaded.Get(VideoScene.NewChat).Enabled&&loaded.Get(VideoScene.NewChat).Muted,"Preferences did not survive a new reader.");
   Require(loaded.Get(VideoScene.Completion).Volume==1.0,"100% volume invalid.");
   string original=File.ReadAllText(store.FilePath);bool rejected=false;
   try{store.Save(defaults,"missing:False");}catch(InvalidOperationException){rejected=true;}
   Require(rejected&&File.ReadAllText(store.FilePath)==original,"Stale editor overwrote current settings.");
   store.Save(loaded.With(VideoScene.Idle,new ScenePreference(true,false,0)),revision);
   loaded=store.Read(out revision);Require(loaded.Get(VideoScene.Idle).Enabled&&loaded.Get(VideoScene.Idle).Volume==0&&File.Exists(Path.Combine(root,"idle-disabled")),"Legacy flag not preserved or became a conflicting second switch.");
   Require(Directory.GetFiles(Path.Combine(root,"settings-backups")).Any(path=>File.ReadAllText(path)==original),"Atomic replace did not preserve previous settings.");
   original=File.ReadAllText(store.FilePath);
   using(var held=new FileStream(store.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read)){
    rejected=false;try{store.Save(defaults,revision);}catch(IOException){rejected=true;}
    Require(rejected,"Locked replacement incorrectly reported success.");
   }
   Require(File.ReadAllText(store.FilePath)==original&&Directory.GetFiles(root,"*.pending-*").Length==0,"Failed save damaged original or retained its staged file.");
   foreach(string invalid in new[]{"{}","{\"schemaVersion\":2,\"scenes\":{}}",original.Replace("\"volumePercent\":25","\"volumePercent\":101"),original.Replace("\"enabled\":false","\"enabled\":\"false\""),original.Replace("\"volumePercent\":25","\"volumePercent\":25.5")}){
    rejected=false;try{SceneSettingsStore.Parse(invalid);}catch{rejected=true;}Require(rejected,"Malformed preferences were accepted.");
   }
   File.WriteAllText(store.FilePath,"bad json");rejected=false;try{store.Save(defaults,revision);}catch{rejected=true;}Require(rejected&&File.ReadAllText(store.FilePath)=="bad json","Unreadable config was silently overwritten.");
   File.WriteAllText(store.FilePath,new string('x',SceneSettingsStore.MaxBytes+1));rejected=false;try{store.Read(out revision);}catch(InvalidDataException){rejected=true;}Require(rejected,"Unbounded config read.");
   var uiStore=new SceneSettingsStore(Path.Combine(root,"editor","scene-settings.json"));Require(SceneSettingsForm.SelfTest(uiStore),"Editor save/cancel isolation failed.");
   IntroLog.Write("scene-settings-test pass=True fixture-retained=True");return true;
  }catch(Exception e){IntroLog.Write("scene-settings-test-failed type="+e.GetType().Name+" reason="+e.Message);return false;}
 }
}
