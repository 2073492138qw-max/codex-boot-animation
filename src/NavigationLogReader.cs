using System;
using System.IO;
using System.Text;

// Byte offsets belong to complete lines, not to the last write notification.
internal static class NavigationLogReader {
 internal static long InitialOffset(Stream stream){
  // Walk backward in fixed-size blocks; do not load the historical log into RAM.
  long end=stream.Length;byte[] block=new byte[4096];
  while(end>0){
   long start=Math.Max(0,end-block.Length);stream.Position=start;int count=0,wanted=(int)(end-start);
   while(count<wanted){int read=stream.Read(block,count,wanted-count);if(read==0)break;count+=read;}
   for(int index=count-1;index>=0;index--)if(block[index]=='\n')return start+index+1;
   end=start;
  }
  return 0;
 }
 internal static string Read(Stream stream,ref long offset){
  if(stream.Length<offset)offset=0;
  if(stream.Length<=offset)return "";
  stream.Position=offset;
  using(var buffer=new MemoryStream()){
   stream.CopyTo(buffer);
   byte[] bytes=buffer.ToArray();int end=Array.LastIndexOf(bytes,(byte)'\n');
   if(end<0)return "";
   string result;
   using(var complete=new MemoryStream(bytes,0,end+1))
   using(var reader=new StreamReader(complete,Encoding.UTF8,true))result=reader.ReadToEnd();
   offset+=end+1;return result;
  }
 }
 static void Append(MemoryStream stream,byte[] bytes,int start,int count){stream.Position=stream.Length;stream.Write(bytes,start,count);}
 internal static bool SelfTest(){
  string line="info IAB_LIFECYCLE received browser sidebar owner sync conversationId=client-new-thread:test ownerRoutePath=/ windowId=1\r\n";
  byte[] bytes=Encoding.UTF8.GetBytes(line);
  // Every split must deliver exactly one complete event, including a split CRLF.
  for(int split=1;split<bytes.Length;split++)using(var stream=new MemoryStream()){
   long offset=0;Append(stream,bytes,0,split);
   if(Read(stream,ref offset)!=""||offset!=0)return false;
   Append(stream,bytes,split,bytes.Length-split);
   string complete=Read(stream,ref offset);bool blank;
   if(complete!=line||offset!=bytes.Length||!IntroWatcher.TryGetDialogRoute(complete,out blank)||!blank)return false;
   if(Read(stream,ref offset)!="")return false;
  }
  // Startup must ignore historical complete lines, but retain a pending tail.
  for(int split=1;split<bytes.Length;split++)using(var stream=new MemoryStream()){
   byte[] history=Encoding.UTF8.GetBytes("historical event\n");Append(stream,history,0,history.Length);Append(stream,bytes,0,split);
   long offset=InitialOffset(stream);
   if(offset!=history.Length||Read(stream,ref offset)!="")return false;
   Append(stream,bytes,split,bytes.Length-split);
   if(Read(stream,ref offset)!=line||Read(stream,ref offset)!="")return false;
  }
  using(var stream=new MemoryStream(bytes))if(InitialOffset(stream)!=bytes.Length)return false;
  using(var stream=new MemoryStream())if(InitialOffset(stream)!=0)return false;
  foreach(int size in new[]{4095,4096,4097,8193})using(var stream=new MemoryStream()){
   byte[] history=Encoding.UTF8.GetBytes("history\n"),tail=Encoding.UTF8.GetBytes(new string('x',size));
   Append(stream,history,0,history.Length);Append(stream,tail,0,tail.Length);
   long offset=InitialOffset(stream);if(offset!=history.Length||Read(stream,ref offset)!="")return false;
   stream.Position=stream.Length;stream.WriteByte((byte)'\n');
   if(Read(stream,ref offset)!=new string('x',size)+"\n")return false;
  }
  byte[] unicode=Encoding.UTF8.GetBytes("你好\n后一行\n尾巴");
  using(var stream=new MemoryStream()){
   long offset=0;Append(stream,unicode,0,1);
   if(Read(stream,ref offset)!=""||offset!=0)return false;
   Append(stream,unicode,1,unicode.Length-1);
   if(Read(stream,ref offset)!="你好\n后一行\n")return false;
   byte[] tail=Encoding.UTF8.GetBytes("结束\n");Append(stream,tail,0,tail.Length);
   if(Read(stream,ref offset)!="尾巴结束\n")return false;
   stream.SetLength(0);byte[] shorter=Encoding.UTF8.GetBytes("新\n");Append(stream,shorter,0,shorter.Length);
   if(Read(stream,ref offset)!="新\n"||offset!=shorter.Length)return false;
  }
  using(var stream=new MemoryStream()){
   long offset=0;byte[] bom=Encoding.UTF8.GetPreamble();Append(stream,bom,0,bom.Length);Append(stream,bytes,0,bytes.Length);
   if(Read(stream,ref offset)!=line)return false;
  }
  return true;
 }
}
