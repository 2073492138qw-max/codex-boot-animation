using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

// Close a real kill-on-close host job, not just a fake process list. A normal
// child must die and the detached child must survive. Never touches Codex.
internal static class CompanionLifetimeTests {
 [StructLayout(LayoutKind.Sequential)] struct BasicLimits {internal long UserTime,JobTime;internal uint Flags;internal UIntPtr MinWorking,MaxWorking;internal uint Active;internal UIntPtr Affinity;internal uint Priority,Scheduling;}
 [StructLayout(LayoutKind.Sequential)] struct IoCounters {internal ulong ReadCount,WriteCount,OtherCount,ReadBytes,WriteBytes,OtherBytes;}
 [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits {internal BasicLimits Basic;internal IoCounters Io;internal UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr security,string name);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int kind,ref ExtendedLimits limits,int size);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
 static string Exe {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BootPlayer.exe");}}
 internal static int Probe(string file){
  using(var process=Process.GetCurrentProcess())File.WriteAllText(file,process.Id+"|"+CompanionLifetime.InJob()+"|"+Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)+"|"+Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)+"|log-bytes="+(File.Exists(Path.Combine(IntroLog.Data,"playback.log"))?new FileInfo(Path.Combine(IntroLog.Data,"playback.log")).Length:0));
  Thread.Sleep(30000);return 0;
 }
 internal static int Parent(string folder){
  try{
  // The normal child's window is also hidden; it is still host-bound.
  Process.Start(new ProcessStartInfo{FileName=Exe,Arguments="--lifetime-probe "+CompanionLifetime.Quote(Path.Combine(folder,"bound")),UseShellExecute=false,CreateNoWindow=true});
  CompanionLifetime.LaunchDetached(Exe,"--lifetime-probe "+CompanionLifetime.Quote(Path.Combine(folder,"detached")));
  File.WriteAllText(Path.Combine(folder,"ready"),"ready");Thread.Sleep(30000);return 0;
  }catch(Exception e){IntroLog.Write("lifetime-parent-error type="+e.GetType().Name+" error="+e.Message);return 2;}
 }
 static bool Wait(Func<bool> check,int milliseconds){var timer=Stopwatch.StartNew();do{if(check())return true;Thread.Sleep(50);}while(timer.ElapsedMilliseconds<milliseconds);return false;}
 internal static bool Run(){
  string folder=Path.Combine(Path.GetTempPath(),"codex-lifetime-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  IntPtr job=IntPtr.Zero;CompanionLifetime.CreatedProcess parent=new CompanionLifetime.CreatedProcess();Process bound=null,detached=null;
  try{
   job=CreateJobObject(IntPtr.Zero,null);if(job==IntPtr.Zero)throw new InvalidOperationException("Create test job failed.");
   var limits=new ExtendedLimits();limits.Basic.Flags=0x2000|0x800; // KILL_ON_JOB_CLOSE | BREAKAWAY_OK
   if(!SetInformationJobObject(job,9,ref limits,Marshal.SizeOf(typeof(ExtendedLimits))))throw new InvalidOperationException("Set test job failed.");
   parent=CompanionLifetime.Create(Exe,"--lifetime-parent "+CompanionLifetime.Quote(folder),CompanionLifetime.Suspended);
   if(!AssignProcessToJobObject(job,parent.Process))throw new InvalidOperationException("Assign test job failed.");
   if(CompanionLifetime.ResumeThread(parent.Thread)==UInt32.MaxValue)throw new InvalidOperationException("Resume test parent failed.");
   if(!Wait(delegate{return File.Exists(Path.Combine(folder,"ready"))&&File.Exists(Path.Combine(folder,"bound"))&&File.Exists(Path.Combine(folder,"detached"));},10000))throw new InvalidOperationException("Lifetime fixtures did not start.");
   string[] a=File.ReadAllText(Path.Combine(folder,"bound")).Split('|'),b=File.ReadAllText(Path.Combine(folder,"detached")).Split('|');
   bound=Process.GetProcessById(Int32.Parse(a[0]));detached=Process.GetProcessById(Int32.Parse(b[0]));
   IntroLog.Write("lifetime-test-ownership bound="+a[1]+" detached="+b[1]+" detached-pid="+b[0]+" actual-job="+CompanionLifetime.InJob(detached.Handle));
   if(a[1]!="True"||b[1]!="False"||CompanionLifetime.InJob(detached.Handle))throw new InvalidOperationException("Incorrect host-job ownership.");
   CompanionLifetime.CloseHandle(job);job=IntPtr.Zero;
   bool terminated=bound.WaitForExit(5000),survived=Wait(delegate{return !detached.HasExited;},1000);Thread.Sleep(500);
   bool pass=terminated&&survived&&!detached.HasExited;
   IntroLog.Write("lifetime-test host-close-bound-exited="+terminated+" detached-survived="+pass);return pass;
  }catch(Exception e){IntroLog.Write("lifetime-test-failed type="+e.GetType().Name+" error="+e.Message);return false;}
  finally{
   if(parent.Process!=IntPtr.Zero){CompanionLifetime.TerminateProcess(parent.Process,0);CompanionLifetime.CloseHandle(parent.Process);CompanionLifetime.CloseHandle(parent.Thread);}
   if(job!=IntPtr.Zero)CompanionLifetime.CloseHandle(job);
   if(bound!=null)bound.Dispose();
   if(detached!=null){if(!detached.HasExited){detached.Kill();detached.WaitForExit(5000);}detached.Dispose();}
   // Only files in this newly-created, exact fixture directory are removed.
   foreach(string file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder);
  }
 }
}
