using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

// Exercise the production state/filter/claim path without displaying videos.
// Playback is separately verified through a real journal replay on the installed helper.
internal static class CompletionFlowTests {
 static Dictionary<string,object> Payload(string session,string turn,string text,bool prompt){return new Dictionary<string,object>{{"session_id",session},{"turn_id",turn},{prompt?"prompt":"last_assistant_message",text}};}
 public static bool Run(){
  string folder=Path.Combine(Path.GetTempPath(),"codex-completion-flow-"+Guid.NewGuid().ToString("N"));
  int deliveries=0;
  CompletionStateStore.TestFolder=folder;
  TaskCompletion.TestDelivery=delegate(string key){System.Threading.Interlocked.Increment(ref deliveries);return true;};
  try{
   // The exact short request that was missed; scope survives an engine turn change.
   string session=Guid.NewGuid().ToString();
   TaskCompletion.OnPrompt(Payload(session,"origin","解决吧",true));
   TaskCompletion.ProcessCompletion(Payload(session,"continued","已修复并测试通过。",false),"journal",30000);
   if(deliveries!=1)return false;
   // Stop and journal race on the same final: exactly one delivery.
   Parallel.For(0,16,delegate(int i){TaskCompletion.ProcessCompletion(Payload(session,"continued","已修复。",false),i%2==0?"journal":"stop-hook",30000);});
   if(deliveries!=1)return false;
   // Late delivery for the origin of the same request must not replay either.
   TaskCompletion.ProcessCompletion(Payload(session,"origin","已修复。",false),"journal",1402255);
   if(deliveries!=1)return false;
   // Missing state after upgrade/continuation: the long-turn fallback can actually run.
   TaskCompletion.ProcessCompletion(Payload(Guid.NewGuid().ToString(),"lost","已修复并更新你电脑上的助手。",false),"journal",1402255);
   if(deliveries!=2)return false;
   // Short state-less chat and failure remain suppressed.
   TaskCompletion.ProcessCompletion(Payload(Guid.NewGuid().ToString(),"short","已完成。",false),"journal",119999);
   TaskCompletion.ProcessCompletion(Payload(Guid.NewGuid().ToString(),"failed","无法完成。",false),"journal",1402255);
   if(deliveries!=2)return false;
   // A new greeting replaces an older task; it cannot inherit the older positive flag.
   session=Guid.NewGuid().ToString();
   TaskCompletion.OnPrompt(Payload(session,"old-task","帮我修复插件",true));
   TaskCompletion.OnPrompt(Payload(session,"greeting","你好",true));
   TaskCompletion.ProcessCompletion(Payload(session,"greeting","已完成。",false),"journal",1000);
   if(deliveries!=2)return false;
   // Reaction-only survives an automatic turn change, including a long-duration record.
   session=Guid.NewGuid().ToString();
   TaskCompletion.OnPrompt(Payload(session,"insult","你是傻逼（测试）",true));
   TaskCompletion.ProcessCompletion(Payload(session,"reaction-continuation","已完成。",false),"stop-hook",0);
   TaskCompletion.ProcessCompletion(Payload(session,"reaction-continuation","已完成。",false),"journal",1402255);
   if(deliveries!=2)return false;
   // Stop has no duration. A false candidate must not block journal's long-task fallback.
   session=Guid.NewGuid().ToString();
   TaskCompletion.OnPrompt(Payload(session,"question","这段代码是什么原因？",true));
   TaskCompletion.ProcessCompletion(Payload(session,"question","已检查并整理结果。",false),"stop-hook",0);
   TaskCompletion.ProcessCompletion(Payload(session,"question","已检查并整理结果。",false),"journal",120000);
   if(deliveries!=3)return false;
   // A refused delivery must not consume the logical request as if it played.
   session=Guid.NewGuid().ToString();
   TaskCompletion.OnPrompt(Payload(session,"delivery-failure","帮我修复插件",true));
   TaskCompletion.TestDelivery=delegate(string key){return false;};
   TaskCompletion.ProcessCompletion(Payload(session,"delivery-failure","已修复。",false),"stop-hook",0);
   string failedKey=CompletionStateStore.Key(session,"delivery-failure");
   return !CompletionStateStore.Claimed(failedKey)&&File.Exists(CompletionStateStore.TurnPath(failedKey));
  }catch(Exception e){IntroLog.Write("completion-flow-test-error type="+e.GetType().Name);return false;}
  finally{
   TaskCompletion.TestDelivery=null;CompletionStateStore.TestFolder=null;
   if(Directory.Exists(folder))Directory.Delete(folder,true);
  }
 }
}
