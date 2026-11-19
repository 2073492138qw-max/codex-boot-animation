using System;
using System.Runtime.InteropServices;

// Only the last-input tick is read. No key, mouse coordinate, or activity history is stored.
internal static class IdleInput {
 [StructLayout(LayoutKind.Sequential)]struct LastInputInfo {public uint Size;public uint Tick;}
 [DllImport("user32.dll")]static extern bool GetLastInputInfo(ref LastInputInfo info);
 public static bool TrySnapshot(out uint inputTick,out uint idleMilliseconds,out uint nowTick){
  inputTick=idleMilliseconds=nowTick=0;
  var info=new LastInputInfo{Size=(uint)Marshal.SizeOf(typeof(LastInputInfo))};
  if(!GetLastInputInfo(ref info))return false;
  nowTick=unchecked((uint)Environment.TickCount);
  inputTick=info.Tick;
  idleMilliseconds=unchecked(nowTick-inputTick);
  if(idleMilliseconds>7u*24u*60u*60u*1000u)idleMilliseconds=0;
  return true;
 }
}

// Human-centered companion idle policy:
// 1. Foreground idle (5 min with Codex visible & no input): companion proactively plays.
// 2. Codex neglect (10 min in other apps, even while active there) -> returning to Codex + 0.8s pause.
// 3. Away/sleep return (5 min global idle) -> returning to Codex within 5 min + 0.8s pause.
// 4. 15-minute cooldown after playback so it feels alive without being spammy.
internal sealed class IdleReturnPolicy {
 internal const uint ForegroundIdleMs=5u*60u*1000u;
 internal const uint AwayIdleMs=5u*60u*1000u;
 internal const uint NeglectThresholdMs=10u*60u*1000u;
 internal const uint ReturnQuietMs=800u;
 internal const uint ReturnExpiryMs=5u*60u*1000u;
 internal const uint CooldownMs=15u*60u*1000u;
 const uint MaxPlausibleGapMs=7u*24u*60u*60u*1000u;
 enum Stage {Watching,Armed,Settling,Cooldown}
 Stage stage;
 bool initialized,directForeground;
 uint previousInputTick,lastTick,returnTick,quietTick,cooldownStartTick,neglectMs;
 public string State {get{return stage.ToString();}}
 public bool Pending {get{return stage==Stage.Settling;}}
 public void Reset(){stage=Stage.Watching;initialized=false;directForeground=false;neglectMs=0;}
 public void Complete(){Complete(unchecked((uint)Environment.TickCount));}
 public void Complete(uint nowTick){stage=Stage.Cooldown;cooldownStartTick=nowTick;directForeground=false;neglectMs=0;}
 public bool Advance(uint inputTick,uint idleMilliseconds,uint nowTick,bool enabled){return Advance(inputTick,idleMilliseconds,nowTick,enabled,true);}
 public bool Advance(uint inputTick,uint idleMilliseconds,uint nowTick,bool enabled,bool codexForeground){
  if(!initialized){
   initialized=true;previousInputTick=inputTick;lastTick=nowTick;neglectMs=0;directForeground=false;
   if(enabled&&idleMilliseconds>=AwayIdleMs){
    if(codexForeground){stage=Stage.Settling;directForeground=true;quietTick=returnTick=nowTick;}
    else stage=Stage.Armed;
   }
   return false;
  }
  uint delta=unchecked(nowTick-lastTick);
  if(delta>MaxPlausibleGapMs)delta=0;
  lastTick=nowTick;
  if(!enabled){stage=Stage.Watching;previousInputTick=inputTick;neglectMs=0;directForeground=false;return false;}
  uint previousTick=previousInputTick;
  bool changed=inputTick!=previousTick;
  previousInputTick=inputTick;

  if(stage==Stage.Cooldown){
   if(unchecked(nowTick-cooldownStartTick)>=CooldownMs){stage=Stage.Watching;neglectMs=0;}
   return false;
  }

  if(stage==Stage.Watching){
   // A resume from sleep may deliver the return input before the next poll.
   uint gap=unchecked(nowTick-previousTick);
   if(changed&&gap>=AwayIdleMs&&gap<=MaxPlausibleGapMs){
    stage=Stage.Settling;directForeground=false;quietTick=returnTick=nowTick;neglectMs=0;
    return false;
   }
   if(codexForeground){
    neglectMs=0;
    if(idleMilliseconds>=ForegroundIdleMs){
     stage=Stage.Settling;directForeground=true;quietTick=returnTick=nowTick;
     return true;
    }
    return false;
   }
   // Codex is in the background: accumulate neglect time even when user is busy in other apps.
   if(idleMilliseconds>=AwayIdleMs){
    stage=Stage.Armed;neglectMs=0;return false;
   }
   uint nextNeglect=neglectMs+delta;
   neglectMs=nextNeglect<neglectMs||nextNeglect>NeglectThresholdMs?NeglectThresholdMs:nextNeglect;
   if(neglectMs>=NeglectThresholdMs)stage=Stage.Armed;
   return false;
  }

  if(stage==Stage.Armed){
   if(codexForeground){
    stage=Stage.Settling;directForeground=false;quietTick=returnTick=nowTick;neglectMs=0;
   }else if(changed&&idleMilliseconds<AwayIdleMs&&neglectMs<NeglectThresholdMs){
    // User returned from global away/sleep to another app: give them 5 minutes to switch back to Codex.
    stage=Stage.Settling;directForeground=false;quietTick=returnTick=nowTick;
   }
   return false;
  }

  // Stage.Settling
  if(directForeground){
   if(!codexForeground||changed){stage=Stage.Watching;directForeground=false;return false;}
   return true;
  }
  if(unchecked(nowTick-returnTick)>=ReturnExpiryMs){stage=Stage.Watching;neglectMs=0;return false;}
  if(changed){quietTick=nowTick;return false;}
  return codexForeground&&unchecked(nowTick-quietTick)>=ReturnQuietMs;
 }

 public static bool SelfTest(){
  // 1. Foreground idle (5 min): triggers directly on Codex without requiring return input.
  var fg=new IdleReturnPolicy();
  if(fg.Advance(10,0,10,true,true))return false;
  if(fg.Advance(10,ForegroundIdleMs-1,10+ForegroundIdleMs-1,true,true))return false;
  if(!fg.Advance(10,ForegroundIdleMs,10+ForegroundIdleMs,true,true))return false;
  // Cooldown (15 min) blocks immediate re-trigger, then allows again after cooldown expires.
  uint doneAt=10+ForegroundIdleMs;
  fg.Complete(doneAt);
  if(fg.Advance(10,ForegroundIdleMs+60000,doneAt+60000,true,true))return false;
  if(fg.Advance(10,ForegroundIdleMs+CooldownMs,doneAt+CooldownMs,true,true))return false; // exits Cooldown -> Watching
  if(!fg.Advance(10,ForegroundIdleMs+CooldownMs+100,doneAt+CooldownMs+100,true,true))return false;

  // 2. Codex neglect (10 min active in Chrome/other apps) -> switch to Codex -> 0.8s pause -> triggers!
  var neglect=new IdleReturnPolicy();
  uint t=1000;
  if(neglect.Advance(t,0,t,true,false))return false;
  for(int min=1;min<=10;min++){
   t+=60000;
   if(neglect.Advance(t,0,t,true,false))return false; // continuously active in another app!
  }
  if(neglect.State!="Armed")return false;
  // User clicks back into Codex -> enters Settling, fires after 800ms pause.
  t+=500;if(neglect.Advance(t,0,t,true,true))return false;
  if(neglect.Advance(t,799,t+799,true,true))return false;
  if(!neglect.Advance(t,800,t+800,true,true))return false;

  // 3. Away from PC (5 min) -> return in another app for 2 min (< 5 min expiry) -> switch to Codex -> 0.8s -> triggers!
  var away=new IdleReturnPolicy();
  if(away.Advance(1,0,1,true,false))return false;
  if(away.Advance(1,AwayIdleMs,1+AwayIdleMs,true,false))return false; // Armed
  uint back=1+AwayIdleMs+1000;
  if(away.Advance(back,0,back,true,false))return false; // Settling in another app
  uint switchToCodex=back+120000; // 2 min later
  if(away.Advance(switchToCodex,0,switchToCodex,true,true))return false;
  if(!away.Advance(switchToCodex,ReturnQuietMs,switchToCodex+ReturnQuietMs,true,true))return false;

  // 4. Expiry after 5 min without switching to Codex cancels the opportunity.
  var expired=new IdleReturnPolicy();
  expired.Advance(1,AwayIdleMs,1+AwayIdleMs,true,false);
  expired.Advance(1+AwayIdleMs+10,0,1+AwayIdleMs+10,true,true);
  if(expired.Advance(1+AwayIdleMs+10,0,1+AwayIdleMs+10+ReturnExpiryMs,true,true))return false;

  // 5. Disabled resets cleanly.
  var off=new IdleReturnPolicy();
  off.Advance(1,AwayIdleMs,1+AwayIdleMs,true,false);
  off.Advance(1+AwayIdleMs+10,0,1+AwayIdleMs+10,true,true);
  if(off.Advance(1+AwayIdleMs+10,0,1+AwayIdleMs+10+ReturnQuietMs,false,true))return false;
  if(off.Advance(1+AwayIdleMs+10,0,1+AwayIdleMs+10+ReturnQuietMs+100,true,true))return false;

  // 6. 49-day uint tick wrap-around works cleanly.
  var wrap=new IdleReturnPolicy();
  uint before=uint.MaxValue-AwayIdleMs;
  if(wrap.Advance(before,0,before,true,true)||wrap.Advance(20,0,20,true,true)||wrap.Advance(20,0,20+ReturnQuietMs-1,true,true)||!wrap.Advance(20,0,20+ReturnQuietMs,true,true))return false;

  return true;
 }
}
