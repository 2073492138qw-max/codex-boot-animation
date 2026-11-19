using System;
using System.Collections.Generic;

// Only already-discovered live desktop logs: no directory walk or UI provider.
// Time comes from a monotonic clock; the existing watcher tick queues one job.
internal sealed class NavigationTailPoll {
 internal const int IntervalMs=250,MaximumPaths=16;
 readonly object gate=new object();
 readonly Dictionary<string,string> paths=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 readonly Queue<string> order=new Queue<string>();
 bool stopped;long next;
 internal bool Stopped {get{lock(gate)return stopped;}}
 internal bool Contains(string path){lock(gate)return paths.ContainsKey(path);}
 internal void Track(string path,string identity){
  if(String.IsNullOrEmpty(path)||String.IsNullOrEmpty(identity))return;
  lock(gate){
   if(stopped)return;
   if(paths.ContainsKey(path)){paths[path]=identity;return;}
   if(paths.Count==MaximumPaths){string oldest=order.Dequeue();paths.Remove(oldest);}
   paths.Add(path,identity);order.Enqueue(path);
  }
 }
 internal KeyValuePair<string,string>[] Snapshot(){lock(gate){if(stopped)return new KeyValuePair<string,string>[0];var result=new KeyValuePair<string,string>[paths.Count];int i=0;foreach(var pair in paths)result[i++]=pair;return result;}}
 internal void Retain(Func<string,string,bool> alive){
  foreach(var pair in Snapshot())if(!alive(pair.Key,pair.Value))lock(gate){
   string current;if(!paths.TryGetValue(pair.Key,out current)||current!=pair.Value)continue;
   paths.Remove(pair.Key);int count=order.Count;for(int i=0;i<count;i++){string path=order.Dequeue();if(!String.Equals(path,pair.Key,StringComparison.OrdinalIgnoreCase))order.Enqueue(path);}
  }
 }
 internal bool Take(long now){lock(gate){if(stopped||paths.Count==0||now<0||now<next)return false;next=now+IntervalMs;return true;}}
 internal void Stop(){lock(gate){stopped=true;paths.Clear();order.Clear();}}
}
