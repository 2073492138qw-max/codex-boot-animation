using System;
using System.IO;
using System.Linq;
using System.Windows;

internal static class CompletionLayoutTests {
 static void Require(bool value,string reason){if(!value)throw new Exception(reason);}
 internal static bool Run(){
  string root=Path.Combine(Path.GetTempPath(),"codex-completion-layout-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   var store=new CompletionLayoutStore(Path.Combine(root,"completion-layout.json"));string revision;
   var defaults=store.Read(out revision);Require(defaults.Size==CompletionNoticeSize.Large&&defaults.Corner==CompletionNoticeCorner.BottomRight&&!File.Exists(store.FilePath),"Defaults changed or read wrote a file.");
   foreach(Rect work in new[]{new Rect(0,0,1920,1040),new Rect(-2560,0,2560,1400),new Rect(0,-1280,720,1280),new Rect(10,20,320,200),new Rect(0,0,5120,1400)}){
    foreach(double scale in new[]{1.0,1.25,2.0,Double.NaN,0,Double.PositiveInfinity}){
     double previous=0;
     foreach(CompletionNoticeSize size in Enum.GetValues(typeof(CompletionNoticeSize))){
      Rect right=CompletionPlayback.NotificationBounds(work,scale,new CompletionLayoutPreference(size,CompletionNoticeCorner.BottomRight));
      Rect left=CompletionPlayback.NotificationBounds(work,scale,new CompletionLayoutPreference(size,CompletionNoticeCorner.BottomLeft));
      Require(work.Contains(left)&&work.Contains(right)&&right.Width>previous&&right.Height>0,"Bounds outside work area or presets not ordered.");
      Require(Math.Abs(right.Width/right.Height-16.0/9)<0.0001&&left.Size==right.Size&&Math.Abs((left.Left-work.Left)-(work.Right-right.Right))<0.0001,"Aspect or corner mirror failed.");
      previous=right.Width;
     }
     Require(CompletionPlayback.NotificationBounds(work,scale)==CompletionPlayback.NotificationBounds(work,scale,defaults),"Default no longer matches validated layout.");
    }
   }
   File.WriteAllText(Path.Combine(root,"scene-settings.json"),"audio-sentinel");
   var wanted=new CompletionLayoutPreference(CompletionNoticeSize.Small,CompletionNoticeCorner.BottomLeft);
   store.Save(wanted,revision);var loaded=new CompletionLayoutStore(store.FilePath).Read(out revision);Require(loaded.Size==wanted.Size&&loaded.Corner==wanted.Corner,"Restart reader lost settings.");
   string original=File.ReadAllText(store.FilePath);bool rejected=false;
   try{store.Save(defaults,"missing");}catch(InvalidOperationException){rejected=true;}
   Require(rejected&&File.ReadAllText(store.FilePath)==original,"Stale editor overwrote a preference.");
   store.Save(defaults,revision);store.Read(out revision);Require(Directory.GetFiles(Path.Combine(root,"settings-backups")).Any(file=>File.ReadAllText(file)==original),"Previous preference backup missing.");
   original=File.ReadAllText(store.FilePath);
   using(var held=new FileStream(store.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read)){rejected=false;try{store.Save(wanted,revision);}catch(IOException){rejected=true;}Require(rejected,"Locked save was accepted.");}
   Require(File.ReadAllText(store.FilePath)==original&&Directory.GetFiles(root,"*.pending-*").Length==0,"Failed save damaged config.");
   foreach(string invalid in new[]{"{}",original.Replace("\"schemaVersion\":1","\"schemaVersion\":2"),original.Replace("Large","Huge"),original.Replace("Large","2"),original.Replace("BottomRight","TopLeft"),original.Replace("\"size\":\"Large\"","\"size\":2"),original.Replace("Large","large")}){rejected=false;try{CompletionLayoutStore.Parse(invalid);}catch{rejected=true;}Require(rejected,"Invalid layout accepted.");}
   File.WriteAllText(store.FilePath,"broken");rejected=false;try{store.Save(defaults,revision);}catch{rejected=true;}Require(rejected&&File.ReadAllText(store.FilePath)=="broken","Corrupt settings overwritten.");
   File.WriteAllText(store.FilePath,new string('x',CompletionLayoutStore.MaxBytes+1));rejected=false;try{store.Read(out revision);}catch(InvalidDataException){rejected=true;}Require(rejected,"Unbounded read accepted.");
   Require(File.ReadAllText(Path.Combine(root,"scene-settings.json"))=="audio-sentinel","Audio config changed.");
   Require(CompletionLayoutForm.SelfTest(new CompletionLayoutStore(Path.Combine(root,"editor","completion-layout.json"))),"Editor preview/save/cancel failed.");
   IntroLog.Write("completion-layout-test pass=True fixture="+root);return true;
  }catch(Exception e){IntroLog.Write("completion-layout-test-failed type="+e.GetType().Name+" reason="+e.Message);return false;}
 }
}
