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

// Debounce a natural return (mouse movement + click), not just a single input.
// Keep the opportunity briefly while Codex is background/busy; consume only on acceptance.
internal sealed class IdleReturnPolicy {
 const uint IdleThresholdMs=15u*60u*1000u;
 const uint ReturnQuietMs=2000u;
 const uint ReturnExpiryMs=60000u;
 enum Stage {Watching,Armed,Settling}
 Stage stage;
 bool initialized;
 uint previousInputTick,returnTick,quietTick;
 public string State {get{return stage.ToString();}}
 public bool Pending {get{return stage==Stage.Settling;}}
 public void Reset(){stage=Stage.Watching;initialized=false;}
 public void Complete(){stage=Stage.Watching;}
 public bool Advance(uint inputTick,uint idleMilliseconds,uint nowTick,bool enabled){return Advance(inputTick,idleMilliseconds,nowTick,enabled,true);}
 public bool Advance(uint inputTick,uint idleMilliseconds,uint nowTick,bool enabled,bool ready){
  if(!initialized){initialized=true;previousInputTick=inputTick;if(enabled&&idleMilliseconds>=IdleThresholdMs)stage=Stage.Armed;return false;}
  if(!enabled){stage=Stage.Watching;previousInputTick=inputTick;return false;}
  uint previousTick=previousInputTick;
  bool changed=inputTick!=previousTick;
  previousInputTick=inputTick;
  if(stage==Stage.Watching){
   // A resume from sleep may deliver the return input before the next poll.
   uint gap=unchecked(nowTick-previousTick);
   if(changed&&gap>=IdleThresholdMs&&gap<=7u*24u*60u*60u*1000u){stage=Stage.Settling;quietTick=returnTick=nowTick;return false;}
   if(idleMilliseconds>=IdleThresholdMs)stage=Stage.Armed;
   return false;
  }
  if(stage==Stage.Armed){if(changed){stage=Stage.Settling;quietTick=returnTick=nowTick;}return false;}
  if(unchecked(nowTick-returnTick)>=ReturnExpiryMs){stage=Stage.Watching;return false;}
  if(changed){quietTick=nowTick;return false;}
  return ready&&unchecked(nowTick-quietTick)>=ReturnQuietMs;
 }
 public static bool SelfTest(){
  var policy=new IdleReturnPolicy();
  if(policy.Advance(10,0,10,true)||policy.Advance(10,900000,900010,true)||policy.Advance(900020,0,900020,true)||policy.Advance(900020,0,902019,true)||!policy.Advance(900020,0,902020,true))return false;
  policy.Complete();if(policy.Advance(900020,0,902100,true))return false;
  policy.Reset();
  if(policy.Advance(1,900000,900001,true)||policy.Advance(900010,0,900010,true)||policy.Advance(900050,0,900050,true)||policy.Advance(900050,0,902049,true)||!policy.Advance(900050,0,902050,true))return false;
  // Background Codex does not consume the opportunity; accepting does.
  if(policy.Advance(900050,0,903000,true,false)||!policy.Advance(900050,0,904000,true,true))return false;
  policy.Complete();if(policy.Advance(900050,0,905000,true))return false;
  policy.Reset();
  if(policy.Advance(1,900000,900001,true)||policy.Advance(900010,0,900010,true)||policy.Advance(900010,0,960010,true))return false;
  policy.Reset();
  if(policy.Advance(1,900000,900001,true)||policy.Advance(900010,0,900010,true)||policy.Advance(900010,0,902010,false)||policy.Advance(900010,0,904010,true))return false;
  policy.Reset();
  if(policy.Advance(1,0,1,true)||policy.Advance(900010,0,900010,true)||!policy.Advance(900010,0,902010,true))return false;
  policy.Reset();
  // Last-input counters wrap approximately every 49 days.
  uint before=uint.MaxValue-900000;
  if(policy.Advance(before,0,before,true)||policy.Advance(20,0,20,true)||policy.Advance(20,0,2019,true)||!policy.Advance(20,0,2020,true))return false;
  return true;
 }
}
