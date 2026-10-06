using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

// Manual preview uses the resident's existing scene arbitration, with an explicit reply.
internal static class IdlePreview {
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr window,int index);
 internal static string RequestStatus(string pipeName=null){
  try{using(var client=new NamedPipeClientStream(".",pipeName??ResidentProtocol.Name,PipeDirection.InOut)){
   client.Connect(80);var writer=new StreamWriter(client){AutoFlush=true};writer.WriteLine("__idle_preview_request__");
   var reader=new StreamReader(client);var response=reader.ReadLineAsync();
   if(response.Wait(500))return response.Result;
   client.Dispose();response.ContinueWith(delegate(Task<string> failed){var observed=failed.Exception;},TaskContinuationOptions.OnlyOnFaulted);return null;
  }}catch{return null;}
 }
 static bool ReplyTest(string reply){
  string name=ResidentProtocol.Name+"-idle-preview-test-"+Guid.NewGuid().ToString("N");
  using(var server=ResidentProtocol.CreateServer(name)){
   IAsyncResult connection=server.BeginWaitForConnection(null,null);
   var task=Task.Factory.StartNew(delegate{server.EndWaitForConnection(connection);string command=ResidentProtocol.ReadCommand(server,1000);var writer=new StreamWriter(server){AutoFlush=true};writer.WriteLine(reply);return command=="__idle_preview_request__";});
   string status=RequestStatus(name);return task.Wait(1500)&&task.Result&&status==reply;
  }
 }
 internal static bool SelfTest(){
  if(!ReplyTest("accepted")||!ReplyTest("busy-or-paused")||!ReplyTest("missing-video"))return false;
  if(RequestStatus(ResidentProtocol.Name+"-absent-"+Guid.NewGuid().ToString("N"))!=null)return false;
  string[] files=MediaLibrary.Prepared();if(files.Length==0)return false;
  var automatic=new CodexVideoWindow(IntPtr.Zero,files[0],VideoScene.Idle);
  var manual=new CodexVideoWindow(IntPtr.Zero,files[0],VideoScene.Idle,true);
  int manualClosed=0;manual.Closed+=delegate{manualClosed++;};
  try{
   if(!automatic.CancelsOnInput||manual.CancelsOnInput||automatic.ShowActivated||!manual.ShowActivated)return false;
   int passiveMask=0x20|0x08000000;
   if((GetWindowLong(new WindowInteropHelper(automatic).EnsureHandle(),-20)&passiveMask)!=passiveMask)return false;
   if((GetWindowLong(new WindowInteropHelper(manual).EnsureHandle(),-20)&passiveMask)!=0)return false;
   Button skip=null;
   foreach(UIElement child in ((Grid)automatic.Content).Children)if(child is Button)return false;
   foreach(UIElement child in ((Grid)manual.Content).Children)if(child is Button)skip=(Button)child;
   if(skip==null)return false;skip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   return manualClosed==1&&!automatic.IsVisible&&!manual.IsVisible;
  }finally{automatic.Close();if(manualClosed==0)manual.Close();}
 }
}
