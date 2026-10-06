using System;

// A manual preview is a single attempt, never an automatic focus-wait request.
internal static class CompletionPreview {
 internal delegate bool CreateWindow(out CodexVideoWindow window,out string reason);
 internal static int Run(CreateWindow create,Action<CodexVideoWindow> show){
  CodexVideoWindow window;string reason;
  if(!create(out window,out reason)){IntroLog.Write("completion-preview-rejected reason="+reason);return 2;}
  show(window);return 0;
 }
 internal static bool SelfTest(){
  int attempts=0,shown=0;
  foreach(string failure in new[]{"not-codex-foreground","missing-video","target-error"}){
   int before=attempts;
   int result=Run(delegate(out CodexVideoWindow window,out string reason){attempts++;window=null;reason=failure;return false;},delegate(CodexVideoWindow window){shown++;});
   if(result!=2||attempts!=before+1||shown!=0)return false;
  }
  string[] files=MediaLibrary.Prepared();if(files.Length==0)return false;
  CodexVideoWindow fixture=new CodexVideoWindow(IntPtr.Zero,files[0],VideoScene.Completion);int closed=0;
  fixture.Closed+=delegate{closed++;};
  try{
   int result=Run(delegate(out CodexVideoWindow window,out string reason){attempts++;window=fixture;reason="ready";return true;},delegate(CodexVideoWindow window){if(window==fixture)shown++;window.Close();});
   return result==0&&attempts==4&&shown==1&&closed==1;
  }finally{if(closed==0)fixture.Close();}
 }
}
