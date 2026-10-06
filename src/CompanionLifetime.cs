using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Management;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using System.Text.RegularExpressions;

// A hidden window is not a detached process. Codex/terminal jobs can terminate
// their children on exit. Refuse an attached supervisor instead of reporting
// a temporary running process as successful persistent installation.
internal static class CompanionLifetime {
 internal const uint Suspended=4, NoWindow=0x08000000;
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct Startup {
  internal int Size;internal string Reserved,Desktop,Title;
  internal int X,Y,XSize,YSize,XChars,YChars,Fill,Flags;
  internal short Show,ReservedSize;internal IntPtr ReservedBytes,Input,Output,Error;
 }
 [StructLayout(LayoutKind.Sequential)] internal struct CreatedProcess {
  internal IntPtr Process,Thread;internal uint Pid,Tid;
 }
 [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool IsProcessInJob(IntPtr process,IntPtr job,out bool result);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string file,StringBuilder command,IntPtr processSecurity,IntPtr threadSecurity,bool inherit,uint flags,IntPtr environment,string directory,ref Startup startup,out CreatedProcess process);
 [DllImport("kernel32.dll",SetLastError=true)] internal static extern uint ResumeThread(IntPtr thread);
 [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool TerminateProcess(IntPtr process,uint code);
 [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
 internal static bool InJob(IntPtr process){bool result;if(!IsProcessInJob(process,IntPtr.Zero,out result))throw new Win32Exception(Marshal.GetLastWin32Error());return result;}
 internal static bool InJob(){using(var process=Process.GetCurrentProcess())return InJob(process.Handle);}
 internal static string Quote(string text){return "\""+text.Replace("\"","\\\"")+"\"";}
 internal static CreatedProcess Create(string exe,string arguments,uint flags){
  exe=Path.GetFullPath(exe);var startup=new Startup{Size=Marshal.SizeOf(typeof(Startup)),Flags=1,Show=0};CreatedProcess process;
  if(!CreateProcess(exe,new StringBuilder(Quote(exe)+" "+arguments),IntPtr.Zero,IntPtr.Zero,false,flags|NoWindow,IntPtr.Zero,Path.GetDirectoryName(exe),ref startup,out process))throw new Win32Exception(Marshal.GetLastWin32Error());
  return process;
 }
 internal static int LaunchDetached(string exe,string arguments){
  // Documented local Win32_Process.Create does not inherit the caller's job.
  // No scheduled task, elevated token, registry change or app-package patch.
  using(var processClass=new ManagementClass("Win32_Process"))
  using(var input=processClass.GetMethodParameters("Create")){
   input["CommandLine"]=Quote(Path.GetFullPath(exe))+" "+arguments;
   input["CurrentDirectory"]=Path.GetDirectoryName(Path.GetFullPath(exe));
   using(var output=processClass.InvokeMethod("Create",input,null)){
    uint result=Convert.ToUInt32(output["ReturnValue"]);
    if(result!=0)throw new InvalidOperationException("Independent companion launch failed; WMI result="+result);
    int id=Convert.ToInt32(output["ProcessId"]);
    using(var child=Process.GetProcessById(id))using(var current=Process.GetCurrentProcess()){
     if(child.SessionId!=current.SessionId||InJob(child.Handle)){
      child.Kill();throw new InvalidOperationException("Independent companion has incorrect session/job ownership.");
     }
    }
    IntroLog.Write("companion-launch-broker=wmi child-pid="+id+" host-job=False");return id;
   }
  }
 }
 internal static bool DetachSupervisor(){
  if(!InJob())return false;
  int child=LaunchDetached(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BootPlayer.exe"),"--supervise --outside-host");
  IntroLog.Write("supervisor-detached child-pid="+child+" host-job=True");return true;
 }
 internal static bool CheckSupervisor(int id){
  try{using(var process=Process.GetProcessById(id))using(var current=Process.GetCurrentProcess())return process.SessionId==current.SessionId&&String.Equals(process.MainModule.FileName,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BootPlayer.exe"),StringComparison.OrdinalIgnoreCase)&&!InJob(process.Handle);}
  catch{return false;}
 }
 internal static bool EnsureSupervisor(){
  try{
   try{using(Mutex.OpenExisting("Local\\CodexIntroSupervisorV1"))return true;}catch(WaitHandleCannotBeOpenedException){}
   string command;using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))command=key==null?null:key.GetValue("CodexBootAnimation") as string;
   Match match=Regex.Match(command??"","^\"([^\"]+\\\\BootPlayer\\.exe)\" --supervise$",RegexOptions.IgnoreCase);
   if(!match.Success||!File.Exists(match.Groups[1].Value)){IntroLog.Write("ensure-supervisor-failed reason=missing-active-registration");return false;}
   int id=LaunchDetached(match.Groups[1].Value,"--supervise --outside-host");
   IntroLog.Write("ensure-supervisor-launched child-pid="+id+" source=active-registration");return true;
  }catch(Exception e){IntroLog.Write("ensure-supervisor-failed type="+e.GetType().Name);return false;}
 }
}
