using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

// Run only from an isolated copy whose video library contains one idle clip.
internal static class EmptyResidentTests {
 internal static bool Run(){
  if(MediaLibrary.Prepared().Length!=0||MediaLibrary.List(VideoScene.Idle).Length!=1)return false;
  bool pass=false;Application app=null;IntroWindow window=null;
  try{
   app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
   window=new IntroWindow(null,"--resident","desktop-start");
   string pipeName=ResidentProtocol.Name+"-empty-test-"+Guid.NewGuid().ToString("N");
   IntroWatcher.StartResidentPipe(window,pipeName);
   var watchdog=new DispatcherTimer{Interval=TimeSpan.FromSeconds(4)};
   watchdog.Tick+=delegate{watchdog.Stop();app.Shutdown();};watchdog.Start();
   window.Show();
   Task.Factory.StartNew(delegate{
    bool pipeReady=false;
    try{
     using(var client=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut)){
      client.Connect(2000);var writer=new StreamWriter(client){AutoFlush=true};
      writer.WriteLine("__idle_return_request__:invalid-token");
      var read=new StreamReader(client).ReadLineAsync();pipeReady=read.Wait(1000)&&read.Result=="deferred";
     }
    }catch(Exception e){IntroLog.Write("resident-empty-pipe-test-error type="+e.GetType().Name);}
    window.Dispatcher.BeginInvoke(new Action(delegate{
     pass=pipeReady&&window.EmptyLibrarySelfTest()&&MediaLibrary.Idle()!=null&&IntroWindow.CanStartIdle(false,false,false,false,false);
     watchdog.Stop();app.Shutdown();
    }));
   });
   app.Run();return pass;
  }catch(Exception e){IntroLog.Write("resident-empty-test-error type="+e.GetType().Name+" message="+e.Message);return false;}
  finally{if(window!=null)window.Close();if(app!=null&&!app.Dispatcher.HasShutdownStarted)app.Shutdown();}
 }
}
