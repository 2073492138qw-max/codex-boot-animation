using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

internal static class IntroLog {
 internal static string Data { get { return RuntimePaths.Root; } }
 public static void Write(string text) {
  try { RotatingLog.Append(Path.Combine(Data, "playback.log"), DateTime.UtcNow.ToString("o") + " pid=" + Process.GetCurrentProcess().Id + " " + text + Environment.NewLine); } catch { }
 }
}
internal static class PromptReaction {
 // Only exclude requests *about* the words. A bare "测试" in "你连测试都没跑" is not meta-discussion.
 static readonly Regex MetaDiscussion=new Regex(@"^(?:(?:比如|例如|举例|假设|假如|测试|试试|检测|识别).{0,100}(?:触发|动画|片头|视频|词|说法|骂人)|(?:(?:请|你能不能|你可以|你帮我|能否|可以|帮我)\s*)?(?:帮我|给我)?\s*(?:写|改写|翻译|解释|分析|生成|描述|列举|造句|查找|找).{0,100}(?:台词|句子|这句话|文本|视频|动画|说法|词|骂人|脏话|是什么意思|什么意思)|(?:(?:请|你帮我|你可以|帮我|给我)\s*)?(?:翻译|解释)\s*[:：]|怎么解释.{0,30}(?:这个词|一词|词|意思)|.{0,24}(?:是什么意思|什么意思)[？?。\s]*$|.{0,24}是一种(?:网络用语|脏话|骂人话|俗语|流行语))",RegexOptions.Compiled|RegexOptions.IgnoreCase|RegexOptions.Singleline);
 const string DirectedCurse=@"(?:我\s*)?[操艹草肏曹]\s*(?:你\s*妈|尼玛|泥马|泥玛)(?:的|逼)?";
 const string SpokenExpletive=@"(?:(?<!妈)妈的|(?:他妈|踏马|特么)(?:的)?|(?<![a-z])tmd?(?![a-z])(?=$|[\s，,。.!！?？；;：:]|怎么|为什么|又|还|一直|老是|总是))";
 static readonly Regex QuotedVideo=new Regex(@"(?:视频里|动画里|片头里|台词里|歌词里|小说里|角色说|角色骂).{0,50}(?:你|codex|gpt|傻|笨蛋|二货|狗屁|狗日|废物|垃圾|太差|太失望|sb|"+DirectedCurse+"|"+SpokenExpletive+")",RegexOptions.Compiled|RegexOptions.IgnoreCase|RegexOptions.Singleline);
 static readonly Regex DirectProfanity=new Regex(@"(?:你|你们|codex|chatgpt|gpt|助手)[\s，。！？,!.：:]{0,4}(?:特么|特妈|他妈|你妈|丫的|tmd?(?![a-z]))",RegexOptions.Compiled|RegexOptions.IgnoreCase);
 // Bare, clearly hostile messages do not need a preceding "你". Keep word
 // boundaries so discussion of an insult inside a longer sentence is excluded.
 static readonly Regex BareInsult=new Regex(@"(?:^|[\s，,。.!！?？；;：:])(?:傻[逼屄比叉笔b]|煞笔|沙币|蠢货|废物|垃圾|脑残|智障|王八蛋|狗东西|狗逼|傻屌|sb|(?:我\s*)?[操艹草肏曹]\s*(?:你\s*妈|尼玛|泥马)(?:的|逼)?|去你妈的|滚你妈的|cnm|nmsl)(?:$|[\s，,。.!！?？；;：:])",RegexOptions.Compiled|RegexOptions.IgnoreCase);
 static readonly Regex AngryExpletive=new Regex(@"^(?:妈的|他妈的|踏马的|特么的|妈了个逼|妈了个巴子|妈卖批|tmd?)(?:$|[\s，,。.!！?？；;：:]|(?=傻逼|废物|垃圾|你))",RegexOptions.Compiled|RegexOptions.IgnoreCase);
 static readonly Regex DirectInsult=new Regex(@"(?:你|你们|codex|chatgpt|gpt|助手).{0,32}(?:傻逼|傻比|傻叉|(?<![a-z])sb(?![a-z])|智障|弱智|废物|垃圾(?!文件|分类|桶|回收|数据|清理|缓存|临时文件)|脑残|蠢货|蠢|没用(?!的?(?:文件|缓存|变量))|骗子|骗我|撒谎|敷衍|滚|去死|烂透|操你|草你|放屁|胡说八道|瞎扯|扯淡)",RegexOptions.Compiled|RegexOptions.IgnoreCase|RegexOptions.Singleline);
 static readonly Regex DirectComplaint=new Regex(@"(?:你|你们|codex|chatgpt|gpt|助手).{0,28}(?:太差|真差|很差|做得差|不靠谱|无能|气死我|让我生气|让我失望|烂得|烂透|怎么还不行|到底行不行|到底会不会|会不会做|搞什么鬼|搞什么玩意|什么破玩意|又搞砸|又搞错|又弄错|连.{0,8}都没(?:做|跑|检查)|越(?:改|修|弄)越(?:差|慢|烂|糟|坏))",RegexOptions.Compiled|RegexOptions.IgnoreCase|RegexOptions.Singleline);
 static readonly Regex StrongComplaint=new Regex(@"(?:怎么(?:又|还)(?:不行|没好|搞错|搞砸|弄错|做错)|越(?:改|修|弄)越(?:差|慢|烂|糟|坏)|(?:又|再)给我搞砸了|(?:又|再)(?:搞错|弄错|做错|搞砸|弄坏)了?|(?:这点|这么点|这么简单的)事都做不好|这(?:都|也)能(?:搞错|做错)|(?:做|写|改|修)的?什么(?:破)?玩意儿?|(?:这|什么)(?:破玩意|什么玩意)|搞什么鬼|什么狗屁|一坨屎|浪费我(?:的)?时间)",RegexOptions.Compiled|RegexOptions.IgnoreCase);
 static readonly Regex ExpletiveComplaint=new Regex(@"^(?:卧槽|我[操艹草]|靠|草)[\s，,。.!！?？；;：:]{0,4}.{0,32}(?:又(?:错|搞砸|搞错|弄错)|没(?:做好|弄好|修好)|做不好|什么(?:破)?玩意|太烂|太差)",RegexOptions.Compiled|RegexOptions.IgnoreCase|RegexOptions.Singleline);
 // Explicit curses can run straight into a request/complaint in spoken Chinese.
 // Keep neutral explanations (e.g. 是什么意思) outside the continuation list.
 static readonly Regex SpokenDirectedCurse=new Regex(@"(?:^|[\s，,。.!！?？；;：:*_`()\[\]])"+DirectedCurse+@"(?=$|[\s，,。.!！?？；;：:*_`()\[\]]|你|这|那|怎么|为什么|别|还|又|赶紧|能不能|到底|插件|视频|播放器)",RegexOptions.Compiled|RegexOptions.IgnoreCase);
 // An undirected swear inside a mixed message needs a nearby complaint. Neither
 // a plain technical failure nor enthusiastic profanity is sufficient alone.
 const string SpokenFailure=@"(?:(?:怎么|为什么|又|还|还是|一直|老是|总是|根本|完全|压根|就是|每次都)[^，,。.!！?？；;\r\n]{0,10}(?:卡住|卡顿|不动|不行|不播放|不触发|没(?:有)?(?:反应|响应)|没修好|不管用)|太差|太烂|做不好|修不好|没修好)";
 static readonly Regex MixedProfanityComplaint=new Regex("(?:"+SpokenExpletive+@"[^。.!！?？；;\r\n]{0,48}"+SpokenFailure+"|"+SpokenFailure+@"[^。.!！?？；;\r\n]{0,48}"+SpokenExpletive+")",RegexOptions.Compiled|RegexOptions.IgnoreCase);
 // Bounded modifiers/suffixes cover colloquial Chinese without substring-matching
 // words such as 垃圾桶, USB or a pet described inside an ordinary sentence.
 static readonly Regex ColloquialInsult=new Regex(@"(?:^|[\s，,。.!！?？；;：:*_`()\[\]])(?:(?:大|死|臭|超级|纯纯的)){0,2}(?:傻[ \t·]{0,2}[逼屄比叉笔b]|傻狗|傻屌|笨蛋|二货|蠢货|废物|垃圾|脑残|智障|狗东西|狗屁|狗日的)(?:玩意儿?|东西|家伙|货)?(?:啊|呀|吧|呢)?(?=$|[\s，,。.!！?？；;：:*_`()\[\]])",RegexOptions.Compiled|RegexOptions.IgnoreCase);
 static readonly Regex ColloquialDirected=new Regex(@"(?:你|你们|codex|chatgpt|gpt|助手).{0,28}(?:笨(?!蛋糕)|差劲|没脑子|根本(?:就)?不会(?:做|弄|写|修)|瞎搞|乱改|忽悠我|又(?:给我)?(?:出|弄|搞|做)?错(?:了|啦|啊|吧|$))",RegexOptions.Compiled|RegexOptions.IgnoreCase|RegexOptions.Singleline);
 static readonly Regex ColloquialComplaint=new Regex(@"^(?:真(?:的)?太(?:差劲|失望|离谱)了|太(?:差劲|失望|离谱)了|(?:真|真是)服了(?:你了)?|这都做不好|怎么还没(?:修|弄|做)好|又(?:出|弄|搞|做)?错了|烦死了|差劲死了)(?=$|[\s，,。.!！?？；;：:])",RegexOptions.Compiled);
 // Only whole, clearly negated/praising messages are excluded. Do not cancel
 // "你不是傻逼，你是废物" or a genuine complaint ending with a polite phrase.
 static readonly Regex NonHostileDirect=new Regex(@"^(?:(?:你|codex|chatgpt|gpt)(?:真(?:的)?|根本)?不是(?:笨蛋|傻逼|废物|垃圾)|你没有乱改[，,]做得不错|你(?:特么|他妈)?(?:真(?:的)?|太)?(?:厉害|棒|聪明)(?:啊|呀|了)?)[。.!！?？\s]*$",RegexOptions.Compiled|RegexOptions.IgnoreCase);
 // Remove narrowly neutral fragments, not entire mixed messages. A technical
 // instruction or negated insult must not hide a second, genuinely hostile clause.
 static readonly Regex NeutralFragment=new Regex(
  @"(?:你|codex|chatgpt|gpt)(?:真(?:的)?|根本|完全|绝对|其实)?(?:并不是|不是|一点也不)(?:白痴|笨蛋|傻逼|废物|垃圾|没用|差劲|差)(?:的助手)?(?=$|[\s，,。.!！?？；;])"+
  @"|(?:别|不要)(?:说|觉得)(?:自己|你)?(?:是)?(?:没用|白痴|废物)"+
  @"|(?:别|不要|请勿|避免)(?:再)?(?:乱改|瞎改|瞎搞)[^，,。.!！?？；;\r\n]{0,4}(?:测试(?:用例|夹具)?|配置|文件)"+
  @"|反(?:反复复|复)(?:出错|犯错)的(?:服务|程序|进程|设备)",RegexOptions.Compiled|RegexOptions.IgnoreCase);
 static readonly Regex ExpandedDirected=new Regex(
  @"(?:你|你们|codex|chatgpt|gpt|助手)[^，,。.!！?？；;\r\n]{0,28}(?:白痴|脑子(?:有病|进水)|有病吧|故意气我|别瞎折腾|就这(?:点)?水平|一点(?:用|作用)都没有"+
  @"|只会嘴硬|装懂|(?:压根|根本)(?:就)?没看懂|糊弄(?:谁|我)|扯犊子|受够|总犯同一个错|老是犯同样的错|反(?:反复复|复)(?:出错|犯错)|老是出错|还是不改"+
  @"|自作聪明|一团糟|耍我|破助手|连最基本的都不会|(?:就知道)?甩锅|只会找借口|根本不(?:听我的要求|看我说的话))",RegexOptions.Compiled|RegexOptions.IgnoreCase);
 static readonly Regex DirectedDisappointment=new Regex(@"(?:我(?:真的|已经)?(?:对(?:你|codex|chatgpt|gpt)(?:很|太)?失望(?:了)?|受够(?:你|codex|chatgpt|gpt)了)|(?:你|codex|chatgpt|gpt)让我(?:很|太)?失望)",RegexOptions.Compiled|RegexOptions.IgnoreCase);
 static readonly Regex ExpandedComplaint=new Regex(@"^[\s*_`]*(?:真(?:的)?垃圾(?:啊|呀|了)?|什么破(?:东西|玩意儿?)|气死我了|烂得要命|烂透了|离谱得很|太糟糕了|越(?:搞|改|弄|修)越乱)(?=$|[\s，,。.!！?？；;：:*_`()\[\]])",RegexOptions.Compiled);
 public static bool ShouldPlay(string prompt){
  if(String.IsNullOrWhiteSpace(prompt))return false;
  string text=prompt.Trim();
  if(text.StartsWith("<send_user_message_question_reply>",StringComparison.OrdinalIgnoreCase))return false;
  try{text=text.Normalize(NormalizationForm.FormKC);}catch(ArgumentException){return false;}
  if(MetaDiscussion.IsMatch(text)||QuotedVideo.IsMatch(text)||NonHostileDirect.IsMatch(text))return false;
  text=NeutralFragment.Replace(text," ");
  return DirectProfanity.IsMatch(text)||DirectInsult.IsMatch(text)||DirectComplaint.IsMatch(text)||BareInsult.IsMatch(text)||AngryExpletive.IsMatch(text)||StrongComplaint.IsMatch(text)||ExpletiveComplaint.IsMatch(text)||SpokenDirectedCurse.IsMatch(text)||MixedProfanityComplaint.IsMatch(text)||ColloquialInsult.IsMatch(text)||ColloquialDirected.IsMatch(text)||ColloquialComplaint.IsMatch(text)||ExpandedDirected.IsMatch(text)||DirectedDisappointment.IsMatch(text)||ExpandedComplaint.IsMatch(text)||AngerLexicon.IsMatch(text);
 }
 public static int RunHook(){var trace=new ReactionTrace(ReactionPeer.CurrentId(),"hook");trace.Record("hook-entry");try{
   char[] buffer=new char[65536];int total=0,read;using(var reader=new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false),true)){while(total<buffer.Length&&(read=reader.Read(buffer,total,buffer.Length-total))>0)total+=read;}trace.Record("input-read");if(total==buffer.Length){trace.Record("rejected","input-too-large");return 0;}
   var payload=new JavaScriptSerializer().DeserializeObject(new string(buffer,0,total)) as Dictionary<string,object>;
   if(payload==null){trace.Record("rejected","invalid-payload");return 0;}trace.Record("payload-parsed");
   object raw;if(!payload.TryGetValue("prompt",out raw)){trace.Record("state-begin");TaskCompletion.OnPrompt(payload);trace.Record("state-end");trace.Record("rejected","missing-prompt");return 0;}string prompt=raw as string;
   object rawSession,rawTurn;string session=payload.TryGetValue("session_id",out rawSession)?rawSession as string:null;string turn=payload.TryGetValue("turn_id",out rawTurn)?rawTurn as string:null;
   bool claimed=CompletionStateStore.TryClaimPrompt(session,turn);
   trace.Record("match-begin");bool matched=claimed&&ShouldPlay(prompt);trace.Record("match",claimed?(matched?"yes":"no"):"already-claimed");
   PromptReactionFlow.Run(matched,delegate{if(File.Exists(Path.Combine(IntroLog.Data,"paused"))){trace.Record("rejected","paused");return;}IntroLog.Write("anger-prompt-matched");trace.Record("dispatch-begin");IntroWatcher.Launch("reaction-angry",trace);},delegate{trace.Record("state-begin");TaskCompletion.OnPrompt(payload);trace.Record("state-end");});
  }catch(Exception e){trace.Record("rejected","hook-error-"+e.GetType().Name);IntroLog.Write("prompt-hook-error type="+e.GetType().Name);}finally{trace.Record("hook-exit");trace.Flush();}return 0;}
 // Local journal fallback when Codex disables/removes plugin hooks: consumes
 // UserMessage events from rollout-*.jsonl, triggers anger reaction on live
 // prompts and records task candidates so short task completions work too.
 public static int OnJournalPrompt(string session,string turn,string prompt,DateTime? timestampUtc){
  var trace=new ReactionTrace(ReactionPeer.CurrentId(),"journal");trace.Record("journal-prompt-entry");
  try{
   if(String.IsNullOrWhiteSpace(session)||String.IsNullOrWhiteSpace(turn)||String.IsNullOrWhiteSpace(prompt)){trace.Record("rejected","missing-fields");return 0;}
   if(!CompletionStateStore.TryClaimPrompt(session,turn)){trace.Record("rejected","already-claimed");return 0;}
   var payload=new Dictionary<string,object>{{"session_id",session},{"turn_id",turn},{"prompt",prompt}};
   bool stale=timestampUtc.HasValue&&(DateTime.UtcNow-timestampUtc.Value).TotalSeconds>30;
   trace.Record("match-begin");bool matched=!stale&&ShouldPlay(prompt);trace.Record("match",stale?"stale":(matched?"yes":"no"));
   PromptReactionFlow.Run(matched,delegate{if(File.Exists(Path.Combine(IntroLog.Data,"paused"))){trace.Record("rejected","paused");return;}IntroLog.Write("anger-prompt-matched source=journal");trace.Record("dispatch-begin");IntroWatcher.Launch("reaction-angry",trace);},delegate{trace.Record("state-begin");TaskCompletion.OnPrompt(payload);trace.Record("state-end");});
  }catch(Exception e){trace.Record("rejected","journal-prompt-error-"+e.GetType().Name);IntroLog.Write("journal-prompt-error type="+e.GetType().Name);}
  finally{trace.Record("journal-prompt-exit");trace.Flush();}
  return 0;
 }
}
internal static class TaskCompletion {
 static readonly Regex Request=new Regex(@"(?:^继续(?:吧|完善|处理)?[。！!\s]*$|帮我|给我|请你|请帮|麻烦|替我|把.{0,80}(?:改|做|设|装|建|修|整理|分析|检查|找|提取|转换|配置|优化|清理|删|汇总|总结|翻译|同步|重构)|(?:修复|修改|创建|生成|安装|部署|实现|测试|验证|解决|完善|执行|检查|整理|查找|提取|转换|配置|编写|更新|继续解决|做成|加上|调高|设置|优化|清理|删除|删掉|重构|排查|对比|总结|汇总|翻译|写个|写一段|写好|改一下|查一下|看一下|算一下|补上|补充|同步|升级|迁移|打包|压缩|拆分|合并)|\b(?:fix|build|create|implement|install|deploy|test|verify|update|write|set up|configure|optimize|clean|delete|remove|refactor|debug|check|summarize|translate|add|edit|modify|run)\b)",RegexOptions.Compiled|RegexOptions.IgnoreCase|RegexOptions.Singleline);
 static readonly Regex QuestionOnly=new Regex(@"^(?:你好|嗨|在吗|谢谢|晚安|早上好|早安|你是谁|怎么了|为什么|是什么|能不能|可以吗|行吗|这能完成吗|这个能完成吗|聊聊|讲个笑话|说个笑话)[？?！!。\s]*$",RegexOptions.Compiled|RegexOptions.IgnoreCase);
 static readonly Regex QuestionSignal=new Regex(@"(?:为什么|为何|怎么回事|什么原因|怎么办|如何|怎么|是否|是不是|有没有|会不会|能不能|可以吗|什么意思|是什么)",RegexOptions.Compiled);
 static readonly Regex ExplicitRequest=new Regex(@"(?:帮我|给我|请(?!问)|麻烦|替我|把.{0,80}(?:改|做|设|装|建|修|检查|整理|找|提取|转换|配置|优化|清理|删|总结|翻译|同步|重构)|(?:修复|修好|改好|重做|解决|检查|重新做|优化|清理|删除|删掉|重构|排查|总结|翻译|修改|同步|补上|查一下|看一下)(?:这个|这段|这些|它|问题|代码|错误|功能|一下|目录|文件))",RegexOptions.Compiled|RegexOptions.Singleline);
 static readonly Regex Success=new Regex(@"(?:^|[。！!\n\r])\s*(?:[#>*-]\s*)*(?:已经(?:完成|修复|安装|创建|更新|部署|配置|添加|设置|生成|提取|整理|检查|找到|处理|修改|做好|优化|清理|删除|移除|同步|生效|验证|保存|提交|合并|重构|补齐|写好|改完|修完|跑通|排查|总结|翻译|列出|搞定)|搞定|完成了|做好了|修好了|装好了|改好了|写好了|改完了|修完了|写完了|做完了|整理好了|检查完了|排查完了|清理完了|优化完了|同步完了|跑通了|验证完了|配置好了|部署好了|处理好了|处理完了|测试通过|验证通过|全部通过|实现了|已完成|已修复|已安装|已创建|已更新|已部署|已配置|已添加|已设置|已生成|已提取|已整理|已检查|已找到|已调好|已优化|已清理|已删除|已移除|已同步|已生效|已验证|已保存|已提交|已合并|已重构|已拆分|已迁移|已补充|已补齐|已写好|已改完|已修完|已装完|已跑通|已排查|已确认|已汇总|已总结|已翻译|已列出|已输出|完成：|检查结果如下|排查结果如下|分析结果如下|统计结果如下|结果如下[:：]|总结如下[:：]|Done[.!:]|All done[.!:]|Completed[.!:]|Fixed[.!:]|Everything is (?:ready|done|updated|fixed))",RegexOptions.Compiled|RegexOptions.IgnoreCase);
 static readonly Regex NaturalSuccess=new Regex(@"(?:已经|已|均已|都已|全部)(?:(?!(?:计划|准备|打算|需要|会|将))[^。！!，,；;：:\n\r]){0,25}?(?:完成|修复|改好|做好|保存|提交|写好|部署|设置|配置|添加|更新|安装|实现|生成|提取|整理|检查|测试通过|验证通过|全部通过|优化|清理|删除|移除|同步|生效|验证|重构|补齐|补上|改完|修完|跑通|总结|翻译|搞定)",RegexOptions.Compiled);
 static readonly Regex Failure=new Regex(@"(?:无法完成|未完成|没完成|尚未完成|还没完成|无法修复|未修复|没修好|失败了|未能完成|不能完成|无法安装|没有成功|未成功|没成功|测试失败|验证失败|需要你先|请先提供|需要你提供|我不能|我无法|没有权限|权限不足|blocked|not completed|could not|failed to|unable to)",RegexOptions.Compiled|RegexOptions.IgnoreCase);
 // A successfully installed patch can separately disclose unperformed second-PC
 // or reboot acceptance. Do not let that specific caveat negate the whole reply.
 static readonly Regex DeferredAcceptance=new Regex(@"(?:关机重启|真实重启|第二台(?:电脑)?|独立[^。！？!?\r\n]{0,10}验收)(?:(?!任务)[^。！？!?\r\n]){0,60}?(?:尚未完成|还没完成|未完成)",RegexOptions.Compiled);
 public static bool IsReactionOnly(string prompt){return !String.IsNullOrWhiteSpace(prompt)&&PromptReaction.ShouldPlay(prompt)&&!ExplicitRequest.IsMatch(prompt);}
 static Dictionary<string,object> ReadPayload(){char[] buffer=new char[131072];int total=0,read;using(var reader=new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false),true)){while(total<buffer.Length&&(read=reader.Read(buffer,total,buffer.Length-total))>0)total+=read;}if(total==buffer.Length)return null;return new JavaScriptSerializer().DeserializeObject(new string(buffer,0,total)) as Dictionary<string,object>;}
 static string Value(Dictionary<string,object> payload,string key){object raw;return payload!=null&&payload.TryGetValue(key,out raw)?raw as string:null;}
 static string Key(Dictionary<string,object> payload){string turn=Value(payload,"turn_id"),session=Value(payload,"session_id");return String.IsNullOrWhiteSpace(turn)||String.IsNullOrWhiteSpace(session)?null:CompletionStateStore.Key(session,turn);}
 public static bool IsTaskRequest(string prompt){if(String.IsNullOrWhiteSpace(prompt))return false;string text=prompt.Trim();if(IsReactionOnly(text))return false;return !QuestionOnly.IsMatch(text)&&!(QuestionSignal.IsMatch(text)&&!ExplicitRequest.IsMatch(text))&&Request.IsMatch(text);}
 public static bool HasFailure(string message){if(String.IsNullOrWhiteSpace(message))return false;string text=message.Trim();string status=DeferredAcceptance.Replace(text,delegate(Match m){return m.Value.Replace("尚未完成","待验收").Replace("还没完成","待验收").Replace("未完成","待验收");});return Failure.IsMatch(status);}
 public static bool HasCompleted(string message){if(String.IsNullOrWhiteSpace(message))return false;string text=message.Trim();return !HasFailure(text)&&(Success.IsMatch(text)||NaturalSuccess.IsMatch(text));}
 public static bool IsTaskCandidate(bool promptCandidate,long durationMs){return promptCandidate||durationMs>=120000;}
 public static int OnPrompt(Dictionary<string,object> payload){try{string prompt=Value(payload,"prompt");if(Key(payload)==null){IntroLog.Write("completion-prompt-suppressed missing-turn-key");return 0;}bool reactionOnly=IsReactionOnly(prompt),candidate=!reactionOnly&&IsTaskRequest(prompt);CompletionStateStore.Record(Value(payload,"session_id"),Value(payload,"turn_id"),reactionOnly?"x":(candidate?"1":"0"));IntroLog.Write("completion-candidate="+candidate);}catch(Exception e){IntroLog.Write("completion-prompt-error type="+e.GetType().Name);}return 0;}
 public static int OnStop(){
  try{
   var payload=ReadPayload();
   IntroLog.Write("completion-stop-hook-entry payload="+(payload==null?"invalid":"ok")+" session="+!String.IsNullOrWhiteSpace(Value(payload,"session_id"))+" turn="+!String.IsNullOrWhiteSpace(Value(payload,"turn_id"))+" answer="+!String.IsNullOrWhiteSpace(Value(payload,"last_assistant_message")));
   return ProcessCompletion(payload,"stop-hook",0,0);
  }catch(Exception e){IntroLog.Write("completion-stop-error type="+e.GetType().Name);return 0;}
 }
 public static int OnJournal(string session,string turn,string message,long durationMs){return OnJournal(session,turn,message,durationMs,0);}
 public static int OnJournal(string session,string turn,string message,long durationMs,int toolCalls){
  try{IntroLog.Write("completion-journal-entry tools="+toolCalls+" duration-ms="+durationMs);return ProcessCompletion(new Dictionary<string,object>{{"session_id",session},{"turn_id",turn},{"last_assistant_message",message}},"journal",durationMs,toolCalls);}
  catch(Exception e){IntroLog.Write("completion-journal-error type="+e.GetType().Name);return 0;}
 }
 internal static int ReplayJournal(){try{var payload=ReadPayload();object duration,tools;long ms=payload!=null&&payload.TryGetValue("duration_ms",out duration)?Convert.ToInt64(duration):0;int toolCount=payload!=null&&payload.TryGetValue("tool_calls",out tools)?Convert.ToInt32(tools):0;return OnJournal(Value(payload,"session_id"),Value(payload,"turn_id"),Value(payload,"last_assistant_message"),ms,toolCount);}catch(Exception e){IntroLog.Write("completion-replay-error type="+e.GetType().Name);return 2;}}
 internal static Func<string,bool> TestDelivery;
 internal static Func<bool> TestForeground = null;
 internal static int ProcessCompletion(Dictionary<string,object> payload,string source,long durationMs){return ProcessCompletion(payload,source,durationMs,0);}
 internal static int ProcessCompletion(Dictionary<string,object> payload,string source,long durationMs,int toolCalls){
   string key=Key(payload),session=Value(payload,"session_id");
   if(key==null){IntroLog.Write("completion-suppressed missing-turn-key");return 0;}
   // Serialize hook/journal state consumption across processes. A durable scope
   // survives automatic continuation and watcher restarts; explicit input replaces it.
   CompletionStateStore.Resolution deliveryState=null;string deliveryKey=null;
   using(new CompletionStateStore.ScopeLock(session)){
    if(CompletionStateStore.Claimed(key)){IntroLog.Write("completion-suppressed already-claimed source="+source);return 0;}
    string previousOrigin=CompletionDeliveryStore.Origin(key);
    if(previousOrigin!=null){
     if(CompletionDeliveryStore.Receipt(previousOrigin)!=null){CompletionDeliveryStore.Finish(previousOrigin);CompletionStateStore.Claim(key);CompletionStateStore.Consume(session,key,new CompletionStateStore.Resolution{OriginKey=previousOrigin});IntroLog.Write("completion-suppressed receipt-recovered source="+source);}
     else{if(CompletionDeliveryStore.Begin(previousOrigin))CompletionDeliveryStore.Finish(previousOrigin);IntroLog.Write("completion-delivery-pending-or-failed task="+previousOrigin.Substring(0,12));}
     return 0;
    }
    CompletionStateStore.Resolution state=CompletionStateStore.Resolve(session,key);
    string message=Value(payload,"last_assistant_message");
    bool hasFailure=HasFailure(message);
    bool hasSuccess=!hasFailure&&HasCompleted(message);
    IntPtr fgHwnd;
    bool codexForeground=TestForeground!=null?TestForeground():(TestDelivery==null&&CodexVideoWindow.TryGetForegroundCodex(out fgHwnd));
    bool inCooldown=TestDelivery==null&&CompletionGate.InCooldown(session,DateTime.UtcNow);
    var decision=CompletionGate.Evaluate(source,state!=null,state!=null&&state.Kind=="1",state!=null&&state.Kind=="x",durationMs,toolCalls,codexForeground,hasSuccess,hasFailure,message,inCooldown);
    if(!decision.Allow){
     if(decision.Reason!="no-task-state"){
      if(decision.Reason!="non-task"||source=="journal")CompletionStateStore.Claim(key);
      CompletionStateStore.Consume(session,key,state);
     }
     IntroLog.Write("completion-suppressed "+decision.Reason+" source="+source+" tools="+toolCalls+" duration-ms="+durationMs+" fg="+codexForeground);
     return 0;
    }
    if(File.Exists(Path.Combine(IntroLog.Data,"paused"))){CompletionStateStore.Claim(key);CompletionStateStore.Consume(session,key,state);IntroLog.Write("completion-suppressed paused source="+source);return 0;}
    deliveryState=state;deliveryKey=state==null?key:state.OriginKey;
    if(CompletionDeliveryStore.Receipt(deliveryKey)!=null){CompletionStateStore.Claim(key);CompletionStateStore.Consume(session,key,state);IntroLog.Write("completion-suppressed already-delivered source="+source);return 0;}
    CompletionDeliveryStore.Link(key,deliveryKey);
    if(!CompletionDeliveryStore.Begin(deliveryKey)){IntroLog.Write("completion-delivery-pending-or-failed task="+deliveryKey.Substring(0,12));return 0;}
    IntroLog.Write("completion-confirmed source="+source+" gate="+decision.Reason+" state="+(state==null?"workload-fallback":state.Source)+" tools="+toolCalls+" duration-ms="+durationMs+" fg="+codexForeground+" task="+key.Substring(0,12));
   }
   bool delivered=CompletionDelivery.Send(deliveryKey,TestDelivery,key)||CompletionDeliveryStore.Receipt(deliveryKey)!=null;
   if(delivered){
    if(TestDelivery==null)CompletionGate.MarkDelivered(session,DateTime.UtcNow);
    using(new CompletionStateStore.ScopeLock(session)){CompletionStateStore.Claim(key);CompletionStateStore.Consume(session,key,deliveryState);}
   }
   IntroLog.Write("completion-delivery-result task="+key.Substring(0,12)+" request="+deliveryKey.Substring(0,12)+" status="+(delivered?CompletionDeliveryStore.Receipt(deliveryKey):"failed"));
  return 0;
 }
}
internal sealed class CodexVideoWindow:Window {
 [StructLayout(LayoutKind.Sequential)]struct WindowRect{public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out WindowRect rect);
 [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr window,int index);
 [DllImport("user32.dll",EntryPoint="SetWindowLongW")]static extern int SetWindowLong(IntPtr window,int index,int value);
 readonly IntPtr target;
 readonly MediaElement media;
 readonly Image cover;
 readonly DispatcherTimer timer;
 readonly OwnedWindowPlacement ownedPlacement=new OwnedWindowPlacement();
 OwnerLocationSubscription ownerLocation;
 readonly Stopwatch elapsed=Stopwatch.StartNew();

 readonly string logPrefix;
 readonly ReactionTrace reactionTrace;
 readonly bool closeOnFocusLoss;
 readonly bool idleWindow;
 readonly bool passiveIdle;
 readonly bool completionNotice;
 readonly CompletionLayoutPreference noticeLayout;
 readonly IntPtr noticeForeground;
 uint idleStartTick;
 readonly bool idleInputAvailable;
 bool ready,closing,firstFrameLogged;
 double deadline=15;
 internal static bool ClosesOnFocusLoss(VideoScene scene){return scene==VideoScene.Completion;}
 internal bool CancelsOnInput {get{return passiveIdle;}}
 internal bool IsCompletionNotice {get{return completionNotice;}}
 internal string CompletionCloseReason {get;private set;}
 internal event Action CompletionStarted;
 internal CodexVideoWindow(IntPtr owner,string path,VideoScene scene,bool manualPreview=false,bool completionNotice=false,CompletionLayoutPreference layout=null,ReactionTrace trace=null){reactionTrace=scene==VideoScene.Anger?trace:null;target=owner;this.completionNotice=scene==VideoScene.Completion&&completionNotice;noticeLayout=layout??CompletionLayoutSettings.Current;idleWindow=scene==VideoScene.Idle;passiveIdle=idleWindow&&!manualPreview;logPrefix=scene==VideoScene.Anger?"anger-window":idleWindow?"idle-window":"completion";closeOnFocusLoss=!this.completionNotice&&ClosesOnFocusLoss(scene);Title=scene==VideoScene.Anger?"Codex 生气回应":idleWindow?"Codex 闲置互动":"Codex 任务完成";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;WindowState=WindowState.Normal;ShowActivated=idleWindow&&manualPreview;ShowInTaskbar=false;Topmost=this.completionNotice;Background=Brushes.Black;new WindowInteropHelper(this).Owner=owner;
  if(this.completionNotice){noticeForeground=GetForegroundWindow();Width=640;Height=360;}
  uint initialIdleMs,initialNowTick;idleInputAvailable=passiveIdle&&IdleInput.TrySnapshot(out idleStartTick,out initialIdleMs,out initialNowTick);
  if(passiveIdle&&!idleInputAvailable)throw new InvalidOperationException("Idle input monitor unavailable");
  var grid=new Grid{Background=Brushes.Black,ClipToBounds=true};
  cover=new Image{Stretch=Stretch.Uniform,IsHitTestVisible=false};
  string first=MediaLibrary.FirstFrame(path);
  if(File.Exists(first))try{
   var frame=new BitmapImage();frame.BeginInit();frame.CacheOption=BitmapCacheOption.OnLoad;frame.UriSource=new Uri(first);frame.EndInit();frame.Freeze();
   var background=new Image{Source=frame,Stretch=Stretch.UniformToFill,Opacity=0.6,IsHitTestVisible=false,Effect=new BlurEffect{Radius=28},RenderTransform=new ScaleTransform(1.08,1.08),RenderTransformOrigin=new Point(0.5,0.5)};
   grid.Children.Add(background);
   grid.Children.Add(new Border{Background=new SolidColorBrush(Color.FromArgb(65,0,0,0)),IsHitTestVisible=false});
   cover.Source=frame;
  }catch(Exception e){IntroLog.Write(logPrefix+"-cover-failed type="+e.GetType().Name);}
  ScenePreference preference=SceneSettings.Current.Get(scene);
  media=new MediaElement{Source=new Uri(path),LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Manual,Stretch=Stretch.Uniform,Volume=preference.Volume,IsMuted=preference.Muted,SpeedRatio=1.0,ScrubbingEnabled=true};
  grid.Children.Add(media);
  grid.Children.Add(cover);
  if(this.completionNotice)grid.Children.Add(new Border{Background=new SolidColorBrush(Color.FromArgb(200,35,35,35)),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(12),Padding=new Thickness(10,6,10,6),IsHitTestVisible=false,Child=new TextBlock{Text="Codex 任务已完成",Foreground=Brushes.White,FontSize=14}});
  if(!passiveIdle){var skip=new Button{Content=this.completionNotice?"跳过":"跳过  Esc",Focusable=!this.completionNotice,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(16),Padding=new Thickness(16,8,16,8),Background=new SolidColorBrush(Color.FromArgb(200,35,35,35)),Foreground=Brushes.White,FontSize=15};skip.Click+=delegate{Finish("skip-button");};grid.Children.Add(skip);}Content=grid;
  PreviewKeyDown+=delegate(object s,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;Finish("escape");}};
  media.MediaOpened+=delegate{if(closing||ready)return;ready=true;double duration=media.NaturalDuration.HasTimeSpan?media.NaturalDuration.TimeSpan.TotalSeconds:5;deadline=Math.Min(duration/media.SpeedRatio+7,30);media.Position=TimeSpan.Zero;media.Play();Trace("media-opened",media.HasAudio?"audio":"no-audio");IntroLog.Write(logPrefix+"-media-opened file="+Path.GetFileName(path)+" audio="+media.HasAudio+" duration="+duration+" speed="+media.SpeedRatio+" volume="+media.Volume+" muted="+media.IsMuted);};
  media.MediaFailed+=delegate(object s,ExceptionRoutedEventArgs e){if(closing)return;IntroLog.Write(logPrefix+"-media-failed "+e.ErrorException.Message);Finish("media-failed");};media.MediaEnded+=delegate{if(!closing)Finish("ended");};
  timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};timer.Tick+=delegate{IntPtr mine=new WindowInteropHelper(this).Handle;if(!this.completionNotice){if(!IsWindow(target)||IsIconic(target)){Finish("owner-gone");return;}IntPtr front=GetAncestor(GetForegroundWindow(),2);if(closeOnFocusLoss&&front!=target&&front!=mine){Finish("codex-not-foreground");return;}if(ownerLocation==null||!ownerLocation.Active)PositionInOwner(mine);}uint inputTick,idleMs,nowTick;if(CancelsOnInput){IntPtr fgNow=GetAncestor(GetForegroundWindow(),2);if(fgNow!=target&&fgNow!=mine){Finish("codex-not-foreground");return;}if(!IdleInput.TrySnapshot(out inputTick,out idleMs,out nowTick)){Finish("input-monitor-unavailable");return;}if(elapsed.ElapsedMilliseconds<1200){idleStartTick=inputTick;}else if(inputTick!=idleStartTick){Finish("user-active");return;}}if(ready&&media.Position.TotalSeconds>0.18){cover.Visibility=Visibility.Collapsed;if(!firstFrameLogged){firstFrameLogged=true;Trace("first-moving-frame");IntroLog.Write(logPrefix+"-first-moving-frame elapsed-ms="+elapsed.ElapsedMilliseconds+" file="+Path.GetFileName(path));if(scene==VideoScene.Completion&&CompletionStarted!=null)CompletionStarted();}}if(elapsed.Elapsed.TotalSeconds>deadline)Finish("timeout");};
  SourceInitialized+=delegate{if(passiveIdle||this.completionNotice){IntPtr hwnd=new WindowInteropHelper(this).Handle;int style=GetWindowLong(hwnd,-20);SetWindowLong(hwnd,-20,style|0x08000000|(passiveIdle?0x20:0));if(this.completionNotice)PositionCompletionNotice(hwnd);else PositionInOwner(hwnd);}};
  SourceInitialized+=delegate{if(!this.completionNotice&&!closing){IntPtr hwnd=new WindowInteropHelper(this).Handle;ownerLocation=new OwnerLocationSubscription(target,Dispatcher,delegate{if(!closing&&IsWindow(target)&&!IsIconic(target))PositionInOwner(hwnd);});PositionInOwner(hwnd);IntroLog.Write(logPrefix+"-follow mode="+(ownerLocation.Active?"location-event":"changed-only-poll-fallback"));}};
  ContentRendered+=delegate{Trace("content-rendered");IntroLog.Write(logPrefix+"-content-rendered elapsed-ms="+elapsed.ElapsedMilliseconds+" file="+Path.GetFileName(path));};
  Loaded+=delegate{if(this.completionNotice){PositionCompletionNotice(new WindowInteropHelper(this).Handle);IntroLog.Write("completion-notice-foreground-preserved="+(GetForegroundWindow()==noticeForeground));}else PositionInOwner(new WindowInteropHelper(this).Handle);timer.Start();media.Play();Trace("window-opened");IntroLog.Write(logPrefix+"-window-opened");};
  Closed+=delegate{closing=true;timer.Stop();if(ownerLocation!=null){ownerLocation.Dispose();ownerLocation=null;}media.Stop();media.Close();Trace("window-closed");IntroLog.Write(logPrefix+"-window-closed");};
 }
 internal void PositionInOwner(IntPtr hwnd){
  if(closing)return;
  WindowRect rect;if(!GetWindowRect(target,out rect))return;
  var source=HwndSource.FromHwnd(hwnd);
  double scale=source!=null&&source.CompositionTarget!=null?source.CompositionTarget.TransformToDevice.M22:1;
  Rect content=OwnedVideoViewport.ContentBounds(new Rect(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top),scale);
  if(content.IsEmpty){Finish("owner-content-unavailable");return;}
  ownedPlacement.Apply(content,delegate(Rect bounds,uint flags){return SetWindowPos(hwnd,IntPtr.Zero,(int)bounds.Left,(int)bounds.Top,(int)bounds.Width,(int)bounds.Height,flags);});
 }
 void PositionCompletionNotice(IntPtr hwnd){
  var work=Forms.Screen.FromHandle(GetForegroundWindow()).WorkingArea;var source=HwndSource.FromHwnd(hwnd);
  double scale=source!=null&&source.CompositionTarget!=null?source.CompositionTarget.TransformToDevice.M11:1;
  Rect bounds=CompletionPlayback.NotificationBounds(new Rect(work.Left,work.Top,work.Width,work.Height),scale,noticeLayout);
  IntroLog.Write("completion-notice-layout size="+noticeLayout.Size+" corner="+noticeLayout.Corner+" width="+(int)bounds.Width+" height="+(int)bounds.Height);
  SetWindowPos(hwnd,new IntPtr(-1),(int)bounds.Left,(int)bounds.Top,(int)bounds.Width,(int)bounds.Height,0x0010);
 }
 void Trace(string phase,string result=null){if(reactionTrace!=null){reactionTrace.Record(phase,result);reactionTrace.QueueFlush();}}
 void Finish(string reason){if(closing)return;closing=true;CompletionCloseReason=reason;Trace("closed",reason);IntroLog.Write(logPrefix+"-close reason="+reason);Close();}
 internal static bool TryGetForegroundCodex(out IntPtr hwnd){hwnd=GetAncestor(GetForegroundWindow(),2);if(hwnd==IntPtr.Zero||IsIconic(hwnd))return false;try{uint pid;GetWindowThreadProcessId(hwnd,out pid);using(Process process=Process.GetProcessById((int)pid)){return DesktopInstancePolicy.IsCodex(process);}}catch{return false;}}
 public static bool TryCreate(VideoScene scene,out CodexVideoWindow window,out string reason,bool manualPreview=false,ReactionTrace trace=null){window=null;reason="not-codex-foreground";IntPtr hwnd;if(!TryGetForegroundCodex(out hwnd))return false;try{string path=scene==VideoScene.Anger?MediaLibrary.Anger():scene==VideoScene.Completion?MediaLibrary.Completion():scene==VideoScene.Idle?MediaLibrary.Idle():null;if(path==null){reason="missing-video";return false;}window=new CodexVideoWindow(hwnd,path,scene,manualPreview,false,null,trace);reason="ready";return true;}catch(Exception e){reason="target-error-"+e.GetType().Name;return false;}}
}
internal sealed class IntroWindow:Window {
 [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr process,IntPtr min,IntPtr max);
 sealed class MediaSlot {public string Path;public MediaElement Media;public bool Ready,Failed,Played;}
 readonly Grid surface;
 readonly Dictionary<string,MediaSlot> slots=new Dictionary<string,MediaSlot>(StringComparer.OrdinalIgnoreCase);
 MediaElement media;
 readonly Grid firstFrameOverlay;
 readonly Image firstFrameImage;
 readonly DispatcherTimer timer;
 readonly DispatcherTimer idleRelease = new DispatcherTimer { Interval=TimeSpan.FromSeconds(30) };
 readonly DispatcherTimer idleRecycle = new DispatcherTimer { Interval=TimeSpan.FromSeconds(90) };
 readonly Stopwatch introDebounce = new Stopwatch();
 readonly StartupFrameTrace startupFrameTrace=StartupFrameTrace.Create();
 readonly IntroCoverGate coverGate=new IntroCoverGate();
 readonly Button skip;
 readonly Button sound;
 readonly Stopwatch elapsed=Stopwatch.StartNew();
 readonly string mode;
 readonly string playbackReason;
 readonly bool resident;
 CodexVideoWindow angerWindow,idleWindow;
 bool opened,progressed,closing,windowLoaded,reloadPending,hasLoadedMedia;
 string activeVideoPath,pendingPath,pendingReason;
 double deadline=8;
 public IntroWindow(string path,string mode,string reason){
  this.mode=mode;playbackReason=reason;resident=mode=="--resident";activeVideoPath=path;Title="Codex Intro";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;WindowState=WindowState.Maximized;Topmost=true;ShowInTaskbar=!resident;Background=Brushes.Black;if(resident){Opacity=0;if(path==null){ShowActivated=false;WindowState=WindowState.Normal;}}
  surface=new Grid();
  firstFrameOverlay=new Grid{Background=Brushes.Black,Visibility=resident?Visibility.Visible:Visibility.Collapsed,IsHitTestVisible=false};
  firstFrameImage=new Image{Stretch=Stretch.Uniform};firstFrameOverlay.Children.Add(firstFrameImage);if(!resident)SetFirstFrame(path);
  surface.Children.Add(firstFrameOverlay);
  var controls=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(24)};
  sound=new Button{Content="声音：开",Padding=new Thickness(16,8,16,8),Margin=new Thickness(0,0,10,0),FontSize=15,Background=new SolidColorBrush(Color.FromArgb(200,35,35,35)),Foreground=Brushes.White};
  sound.Click+=delegate{if(media==null)return;media.IsMuted=!media.IsMuted;sound.Content=media.IsMuted?"声音：关":"声音：开";};
  skip=new Button{Content="跳过  Esc",Padding=new Thickness(20,8,20,8),FontSize=15,Background=new SolidColorBrush(Color.FromArgb(200,35,35,35)),Foreground=Brushes.White};
  skip.Click+=delegate{Finish("skip-button");};controls.Children.Add(sound);controls.Children.Add(skip);surface.Children.Add(controls);Content=surface;
  media=path==null?null:PrepareSlot(path,!resident).Media;
  if(media!=null)sound.Content=media.IsMuted?"声音：关":"声音：开";
  if(resident)foreach(string candidate in MediaLibrary.Prepared())PrepareSlot(candidate,false);
  idleRelease.Tick += delegate { ReleaseIdleMedia(); };
  idleRecycle.Tick += delegate { TryIdleRecycle(); };
  PreviewKeyDown+=delegate(object s,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;Finish("escape");}};
  timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
  timer.Tick+=delegate{
   if(media==null)return;
   double pos=media.Position.TotalSeconds;
   if(opened)startupFrameTrace.Sample("tick-before-cover",elapsed.ElapsedMilliseconds,pos,firstFrameOverlay.Visibility==Visibility.Visible);
   if(resident&&opened&&firstFrameOverlay.Visibility==Visibility.Visible&&coverGate.ShouldDismiss(pos,elapsed.ElapsedMilliseconds)){firstFrameOverlay.Visibility=Visibility.Collapsed;timer.Interval=TimeSpan.FromMilliseconds(50);}
   if(!progressed&&opened&&pos>0.5){progressed=true;IntroLog.Write("playback-progress seconds="+pos);}
   if(elapsed.Elapsed.TotalSeconds>deadline){Environment.ExitCode=3;Finish(opened?"playback-timeout":"load-timeout");}
   if(mode=="--probe"&&progressed&&pos>2)Finish("probe-complete");
   if(mode=="--skip-test"&&progressed&&pos>2)skip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   if(mode=="--escape-test"&&progressed&&pos>2)RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
  };
  Loaded+=delegate{IntroLog.Write("window-loaded");windowLoaded=true;if(resident){Hide();try{using(var proc=Process.GetCurrentProcess())SetProcessWorkingSetSize(proc.Handle,(IntPtr)(-1),(IntPtr)(-1));}catch{}}else{foreach(MediaSlot slot in slots.Values){slot.Media.Source=new Uri(slot.Path);slot.Media.Play();}timer.Start();Activate();}};
  Closed+=delegate{timer.Stop();idleRelease.Stop();idleRecycle.Stop();foreach(MediaSlot slot in slots.Values)slot.Media.Close();IntroLog.Write("window-closed");};
 }
 void TryIdleRecycle() {
  if (!resident || !hasLoadedMedia || IsVisible || pendingPath != null || angerWindow != null || idleWindow != null || Application.Current == null) return;
  idleRecycle.Stop();
  IntroLog.Write("resident-idle-recycle");
  Application.Current.Shutdown(0);
 }
 internal void ReleaseIdleMedia() {
  if (!resident || IsVisible || pendingPath != null) return;
  idleRelease.Stop();
  var old = new List<MediaSlot>(slots.Values);
  slots.Clear(); media = null; activeVideoPath = null; firstFrameImage.Source = null;
  foreach (MediaSlot slot in old) {
   try { slot.Media.Close(); slot.Media.Source = null; } catch { }
   surface.Children.Remove(slot.Media);
  }
  if (old.Count > 0) {
   try { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); using (var proc = Process.GetCurrentProcess()) SetProcessWorkingSetSize(proc.Handle, (IntPtr)(-1), (IntPtr)(-1)); } catch { }
  }
  if (hasLoadedMedia && Application.Current != null) { idleRecycle.Stop(); idleRecycle.Start(); }
  IntroLog.Write("resident-idle-media-released count=" + old.Count);
 }
 MediaSlot EnsurePrepared(string path){ return PrepareSlot(path,true); }
 MediaSlot PrepareSlot(string path,bool startLoad){
  MediaSlot existing;
  if(slots.TryGetValue(path,out existing)){
   if(!resident||!existing.Failed){
    if(resident&&startLoad&&windowLoaded&&!existing.Ready&&existing.Media.Source==null){
     try{hasLoadedMedia=true;existing.Media.Source=new Uri(existing.Path);existing.Media.Play();}
     catch(Exception e){HandleMediaFailure(existing,"prepare-error-"+e.GetType().Name);}
    }
    return existing;
   }
   // One replacement at the next explicit trigger, never an automatic retry
   // loop. Remove identity first so late events from Close cannot poison it.
   slots.Remove(path);
   if(media==existing.Media){media=null;activeVideoPath=null;}
   try{existing.Media.Close();}catch(Exception e){IntroLog.Write("resident-media-release-error type="+e.GetType().Name);}
   surface.Children.Remove(existing.Media);
   IntroLog.Write("resident-media-reprepare file="+Path.GetFileName(path));
  }
  // New-chat intros use a separate first-frame cover and no interactive seek.
  // Do not render paused seek frames for their hidden, cached media surfaces.
  // Keep startup and other media policies unchanged.
  bool pausedFrameScrubbing=!String.Equals(Path.GetDirectoryName(path),MediaLibrary.FolderFor(VideoScene.NewChat),StringComparison.OrdinalIgnoreCase);
  ScenePreference preference=SceneSettings.ForReason(playbackReason);var slot=new MediaSlot{Path=path,Media=new MediaElement{LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Manual,Stretch=Stretch.Uniform,ScrubbingEnabled=pausedFrameScrubbing,Volume=resident?0.0:preference.Volume,IsMuted=preference.Muted,SpeedRatio=1.2,Opacity=resident?0:1,Visibility=resident?Visibility.Hidden:Visibility.Visible,IsHitTestVisible=false}};slots[path]=slot;surface.Children.Insert(0,slot.Media);
  slot.Media.MediaOpened+=delegate{MediaSlot current;if(!slots.TryGetValue(slot.Path,out current)||current!=slot)return;try{slot.Ready=true;slot.Failed=false;double duration=slot.Media.NaturalDuration.HasTimeSpan?slot.Media.NaturalDuration.TimeSpan.TotalSeconds:30;IntroLog.Write("media-opened file="+Path.GetFileName(slot.Path)+" video="+slot.Media.NaturalVideoWidth+"x"+slot.Media.NaturalVideoHeight+" audio="+slot.Media.HasAudio+" duration="+duration+" volume="+slot.Media.Volume+" speed="+slot.Media.SpeedRatio);if(resident){bool pendingNow=String.Equals(pendingPath,slot.Path,StringComparison.OrdinalIgnoreCase);if(!pendingNow){slot.Media.Pause();slot.Media.Position=TimeSpan.Zero;slot.Media.Volume=0.0;}IntroLog.Write("resident-player-ready file="+Path.GetFileName(slot.Path));if(pendingNow)StartPrepared(slot,pendingReason);}else{opened=true;deadline=mode=="--ui-test"?60:Math.Min(duration/slot.Media.SpeedRatio+10,120);}}catch(Exception e){HandleMediaFailure(slot,"open-error-"+e.GetType().Name);}};
  slot.Media.MediaFailed+=delegate(object s,ExceptionRoutedEventArgs e){HandleMediaFailure(slot,e.ErrorException==null?"unknown":e.ErrorException.Message);};
  slot.Media.MediaEnded+=delegate{MediaSlot current;if(!slots.TryGetValue(slot.Path,out current)||current!=slot)return;if(resident&&!IsVisible)return;if(!String.Equals(activeVideoPath,slot.Path,StringComparison.OrdinalIgnoreCase))return;if(mode=="--ui-test"){slot.Media.Position=TimeSpan.Zero;slot.Media.Play();}else Finish("ended");};
  if(windowLoaded&&(!resident||startLoad)){try{hasLoadedMedia=true;slot.Media.Source=new Uri(slot.Path);slot.Media.Play();}catch(Exception e){HandleMediaFailure(slot,"prepare-error-"+e.GetType().Name);}}return slot;
 }
 void HandleMediaFailure(MediaSlot slot,string error){
  MediaSlot current;if(!slots.TryGetValue(slot.Path,out current)||current!=slot)return;
  IntroLog.Write("media-failed file="+Path.GetFileName(slot.Path)+" "+error);
  slot.Ready=false;slot.Failed=true;
  bool activePending=String.Equals(pendingPath,slot.Path,StringComparison.OrdinalIgnoreCase);
  if(!resident||activePending||(IsVisible&&String.Equals(activeVideoPath,slot.Path,StringComparison.OrdinalIgnoreCase))){Environment.ExitCode=2;Finish("media-failed");}
 }
 void SetFirstFrame(string path){if(path==null){firstFrameImage.Source=null;return;}try{string cover=MediaLibrary.FirstFrame(path);if(!File.Exists(cover)){firstFrameImage.Source=null;return;}var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(cover);bitmap.EndInit();bitmap.Freeze();firstFrameImage.Source=bitmap;}catch(Exception e){firstFrameImage.Source=null;IntroLog.Write("first-frame-cover-failed "+e.Message);}}
 void SelectActiveSlot(MediaSlot selected){foreach(MediaSlot slot in slots.Values){if(slot!=selected){slot.Media.Pause();slot.Media.Volume=0;slot.Media.Opacity=0;slot.Media.Visibility=Visibility.Hidden;}}selected.Media.Visibility=Visibility.Visible;selected.Media.Opacity=1;media=selected.Media;activeVideoPath=selected.Path;}
 internal static bool CanStartAnger(bool introVisible,bool reactionVisible,bool paused,bool automatic){return !introVisible&&!reactionVisible&&(!automatic||!paused);}
 internal static bool CanStartIdle(bool introVisible,bool angerVisible,bool idleVisible,bool completionPending,bool paused){return !introVisible&&!angerVisible&&!idleVisible&&!completionPending&&!paused;}
 static bool CompletionPending(){try{using(Mutex.OpenExisting("Local\\CodexCompletionPendingV1"))return true;}catch(WaitHandleCannotBeOpenedException){return false;}catch{return true;}}
 public void CloseIdle(){if(idleWindow!=null){CodexVideoWindow old=idleWindow;idleWindow=null;old.Close();}}
 void CloseWindowScenesForIntro(string reason){
  CloseIdle();
  if(angerWindow==null)return;
  // Clear ownership before Close invokes callbacks. Closed stops and releases
  // the old media synchronously, before the intro can show or start its audio.
  CodexVideoWindow old=angerWindow;angerWindow=null;old.Close();
  IntroLog.Write("anger-window-interrupted reason="+reason);
 }
 public void ReloadSelected(){if(!resident)return;if(IsVisible||pendingPath!=null){reloadPending=true;return;}reloadPending=false;pendingPath=null;pendingReason=null;activeVideoPath=null;foreach(MediaSlot slot in slots.Values){slot.Media.Close();surface.Children.Remove(slot.Media);}slots.Clear();media=null;foreach(string path in MediaLibrary.Prepared())PrepareSlot(path,false);idleRelease.Stop();IntroLog.Write("resident-video-choices-reloaded count="+slots.Count);}
 public void PlayResident(string reason,ReactionTrace trace=null){
  if(trace!=null){trace.Record("dispatch-enter");trace.QueueFlush();}
  if(!resident)return;
  if(!SceneSettings.Allows(reason)){if(trace!=null)trace.Reject("scene-disabled");IntroLog.Write("scene-disabled reason="+reason);return;}
  if(reason=="idle-return"||reason=="preview-idle"){
   TryPlayIdle(reason);return;
  }
  CloseIdle();
  if(reason=="reaction-angry"||reason=="preview-anger"){
   if(!CanStartAnger(IsVisible||pendingPath!=null,angerWindow!=null&&angerWindow.IsVisible,File.Exists(Path.Combine(IntroLog.Data,"paused")),reason=="reaction-angry")){if(trace!=null)trace.Reject("busy-or-paused");IntroLog.Write("anger-suppressed busy-or-paused");return;}
   CodexVideoWindow window;string why;
   if(!CodexVideoWindow.TryCreate(VideoScene.Anger,out window,out why,false,trace)){if(trace!=null)trace.Reject(why);IntroLog.Write("anger-window-suppressed reason="+why);return;}
   idleRecycle.Stop();hasLoadedMedia=true;angerWindow=window;window.Closed+=delegate{if(angerWindow==window){angerWindow=null;if(!IsVisible&&pendingPath==null&&idleWindow==null){idleRelease.Stop();idleRelease.Start();}}};window.Show();IntroLog.Write("anger-window-start reason="+reason);return;
  }
  if ((IsVisible || pendingPath != null) && introDebounce.IsRunning && introDebounce.ElapsedMilliseconds < 600) return;
  string path=MediaLibrary.ForReason(reason);if(path==null){IntroLog.Write("missing-video reason="+reason);return;}
  idleRelease.Stop(); idleRecycle.Stop(); introDebounce.Restart();
  MediaSlot slot=EnsurePrepared(path);if(slot.Failed){IntroLog.Write("resident-video-unavailable reason="+reason+" file="+Path.GetFileName(path));return;}
  CloseWindowScenesForIntro(reason);
  if(!slot.Ready){pendingPath=path;pendingReason=reason;SelectActiveSlot(slot);opened=false;progressed=false;elapsed.Restart();deadline=15;firstFrameOverlay.Visibility=Visibility.Visible;SetFirstFrame(path);coverGate.Reset();timer.Interval=TimeSpan.FromMilliseconds(50);timer.Start();IntroLog.Write("resident-trigger-queued reason="+reason+" file="+Path.GetFileName(path));return;}
  StartPrepared(slot,reason);
 }
 string acceptedIdleToken;
 public bool TryPlayIdleOnce(string token){if(token==acceptedIdleToken)return true;if(!TryPlayIdle("idle-return"))return false;acceptedIdleToken=token;return true;}
 public bool TryPlayIdle(string reason){string why;return TryPlayIdle(reason,out why);}
 public bool TryPlayIdle(string reason,out string why){
  bool automatic=reason=="idle-return";
  bool paused=automatic&&(File.Exists(Path.Combine(IntroLog.Data,"paused"))||!SceneSettings.Allows("idle-return"));
  why="busy-or-paused";if(!CanStartIdle(IsVisible||pendingPath!=null,angerWindow!=null&&angerWindow.IsVisible,idleWindow!=null&&idleWindow.IsVisible,CompletionPending(),paused))return false;
  uint input,idle,now;why="not-quiet";if(automatic&&(!IdleInput.TrySnapshot(out input,out idle,out now)||idle<700))return false;
  CodexVideoWindow window;if(!CodexVideoWindow.TryCreate(VideoScene.Idle,out window,out why,reason=="preview-idle"))return false;
  idleRecycle.Stop();hasLoadedMedia=true;idleWindow=window;window.Closed+=delegate{if(idleWindow==window){idleWindow=null;if(!IsVisible&&pendingPath==null&&angerWindow==null){idleRelease.Stop();idleRelease.Start();}}};window.Show();IntroLog.Write("idle-window-start reason="+reason);why="accepted";return true;
 }
 void StartPrepared(MediaSlot slot,string reason){
  try{CloseWindowScenesForIntro(reason);pendingPath=null;pendingReason=null;closing=false;opened=true;progressed=false;elapsed.Restart();startupFrameTrace.Begin(reason=="desktop-start"||reason=="preview-startup");startupFrameTrace.Sample("entry",elapsed.ElapsedMilliseconds,slot.Media.Position.TotalSeconds,true);SelectActiveSlot(slot);SetFirstFrame(activeVideoPath);firstFrameOverlay.Visibility=Visibility.Visible;coverGate.Begin();if(slot.Played||media.Position>TimeSpan.Zero){media.Pause();media.Position=TimeSpan.Zero;}slot.Played=true;startupFrameTrace.Sample("after-seek",elapsed.ElapsedMilliseconds,media.Position.TotalSeconds,true);double duration=media.NaturalDuration.HasTimeSpan?media.NaturalDuration.TimeSpan.TotalSeconds:30;deadline=Math.Min(duration/media.SpeedRatio+10,120);Opacity=1;ShowInTaskbar=true;ShowActivated=true;WindowState=WindowState.Maximized;Topmost=true;ScenePreference preference=SceneSettings.ForReason(reason);media.Volume=preference.Volume;media.IsMuted=preference.Muted;sound.Content=media.IsMuted?"声音：关":"声音：开";if(!IsVisible)Show();startupFrameTrace.Sample("after-show",elapsed.ElapsedMilliseconds,media.Position.TotalSeconds,true);media.Play();startupFrameTrace.Sample("after-play",elapsed.ElapsedMilliseconds,media.Position.TotalSeconds,true);timer.Interval=TimeSpan.FromMilliseconds(16);timer.Start();Activate();IntroLog.Write("resident-playback-start reason="+reason+" file="+Path.GetFileName(activeVideoPath)+" volume="+media.Volume+" muted="+media.IsMuted);}
  catch(Exception e){HandleMediaFailure(slot,"start-error-"+e.GetType().Name);if(IsVisible)Finish("media-failed");}
 }
 void Finish(string reason){
  if(closing)return;closing=true;
  double position=0;try{if(media!=null)position=media.Position.TotalSeconds;}catch{}
  IntroLog.Write("close reason="+reason+" position="+position);
  if(!resident){Close();return;}
  timer.Stop();timer.Interval=TimeSpan.FromMilliseconds(50);coverGate.Reset();pendingPath=null;pendingReason=null;
  // Remove the covering window before a reset/seek can fail. Failed media is
  // rebuilt on a later trigger; never seek it again while handling its error.
  Hide();ShowInTaskbar=false;Opacity=1;firstFrameOverlay.Visibility=Visibility.Visible;
  MediaSlot active;slots.TryGetValue(activeVideoPath??"",out active);
  if(active!=null&&(reason=="load-timeout"||reason=="playback-timeout")){active.Ready=false;active.Failed=true;}
  try{
   if(media!=null){media.Volume=0;media.Opacity=0;media.Visibility=Visibility.Hidden;if(active==null||!active.Failed){media.Pause();media.Position=TimeSpan.Zero;}}
  }catch(Exception e){if(active!=null){active.Ready=false;active.Failed=true;}IntroLog.Write("resident-media-reset-error type="+e.GetType().Name);}
  finally{closing=false;}
  if(reloadPending)ReloadSelected();
  idleRelease.Stop(); idleRelease.Start();
 }
 public bool SceneVisibilitySelfTest(){if(!resident)return false;string[] prepared=MediaLibrary.Prepared();if(prepared.Length<2)return false;ReloadSelected();foreach(MediaSlot slot in slots.Values)if(slot.Media.Visibility!=Visibility.Hidden||slot.Media.Opacity!=0)return false;foreach(string path in new[]{prepared[0],prepared[1]}){MediaSlot selected=slots[path];SelectActiveSlot(selected);int visible=0;foreach(MediaSlot slot in slots.Values){if(slot.Media.Visibility==Visibility.Visible&&slot.Media.Opacity==1)visible++;else if(slot!=selected&&(slot.Media.Visibility!=Visibility.Hidden||slot.Media.Opacity!=0))return false;}if(visible!=1||selected.Media.Visibility!=Visibility.Visible)return false;}ReloadSelected();foreach(MediaSlot slot in slots.Values)if(slot.Media.Visibility!=Visibility.Hidden||slot.Media.Opacity!=0)return false;return true;}
 internal bool EmptyLibrarySelfTest(){if(!resident||slots.Count!=0)return false;PlayResident("desktop-start");PlayResident("new-dialog-click");ReloadSelected();skip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));return slots.Count==0&&media==null&&!IsVisible&&!ShowInTaskbar&&!timer.IsEnabled&&firstFrameImage.Source==null;}
 internal bool IntroPrioritySelfTest(){
  string[] files=MediaLibrary.Prepared();if(!resident||files.Length==0)return false;
  int angerClosed=0,idleClosed=0;
  // Unshown WPF windows exercise the real Closed/media cleanup without playback
  // or attaching a test window to the user's Codex window.
  try{
   angerWindow=new CodexVideoWindow(IntPtr.Zero,files[0],VideoScene.Anger);
   angerWindow.Closed+=delegate{angerClosed++;};
   PlayResident("priority-missing-test");
   // Failed slots are now retryable on the next trigger; their recovery is
   // covered independently rather than treating them as permanently missing.
   if(angerWindow==null||angerClosed!=0||IsVisible||timer.IsEnabled)return false;
   foreach(string reason in new[]{"desktop-start","new-dialog-click","preview-new-chat"}){
    if(angerWindow==null){angerWindow=new CodexVideoWindow(IntPtr.Zero,files[0],VideoScene.Anger);angerWindow.Closed+=delegate{angerClosed++;};}
    idleWindow=new CodexVideoWindow(IntPtr.Zero,files[0],VideoScene.Idle);
    idleWindow.Closed+=delegate{idleClosed++;};
    int oldAnger=angerClosed,oldIdle=idleClosed;
    CloseWindowScenesForIntro(reason);
    if(angerWindow!=null||idleWindow!=null||angerClosed!=oldAnger+1||idleClosed!=oldIdle+1)return false;
    CloseWindowScenesForIntro(reason);
    if(angerClosed!=oldAnger+1||idleClosed!=oldIdle+1)return false;
   }
   return !IsVisible&&!timer.IsEnabled;
  }finally{if(angerWindow!=null){angerWindow.Close();angerWindow=null;}CloseIdle();}
 }
}
internal sealed class IntroWatcher:Forms.ApplicationContext {
 [StructLayout(LayoutKind.Sequential)]struct MousePoint{public int X,Y;}
 [StructLayout(LayoutKind.Sequential)]struct MouseEvent{public MousePoint Point;public uint MouseData,Flags,Time;public IntPtr ExtraInfo;}
 [StructLayout(LayoutKind.Sequential)]struct WindowRect{public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(MousePoint point);
 [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out WindowRect rect);
 readonly Forms.Timer poll=new Forms.Timer();readonly Forms.NotifyIcon tray;readonly IdleReturnPolicy idlePolicy=new IdleReturnPolicy();readonly Dictionary<string,long> logOffsets=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);readonly HashSet<string> identities=new HashSet<string>(StringComparer.OrdinalIgnoreCase);readonly Dictionary<IntPtr,WindowRect> windowBounds=new Dictionary<IntPtr,WindowRect>();readonly ConcurrentDictionary<string,bool> readyProcesses=new ConcurrentDictionary<string,bool>();readonly object dialogRouteLock=new object();readonly RestartBackoff residentBackoff=new RestartBackoff();RawMouseInput mouseInput;long nextClickRead;long nextProcessScan;volatile bool paused;bool idleDisabled;readonly List<FileSystemWatcher> logWatchers=new List<FileSystemWatcher>();FileSystemWatcher videoWatcher;CompletionJournal completionJournal;long lastVideoChangeTicks;int idleRequestActive,idleAccepted;string lastIdleState="Watching";DateTime nextIdleAttemptUtc=DateTime.MinValue,lastLogReconcileUtc=DateTime.MinValue;
 SceneSettingsForm settingsWindow;
 CompletionLayoutForm completionLayoutWindow;
 readonly System.Drawing.Icon trayIcon;
 readonly ConcurrentDictionary<string,NavigationCursor> navigationCursors=new ConcurrentDictionary<string,NavigationCursor>(StringComparer.OrdinalIgnoreCase);
 readonly ConcurrentDictionary<string,bool> navigationHistoryPending=new ConcurrentDictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
 readonly DateTime navigationStartedUtc=DateTime.UtcNow;
 readonly WatcherReconcile reconcile=new WatcherReconcile();
 readonly WatcherReconcile navigationWorker=new WatcherReconcile();
 readonly NavigationTailPoll navigationTail=new NavigationTailPoll();
 readonly Stopwatch navigationClock=Stopwatch.StartNew();
 public IntroWatcher(){
  paused=File.Exists(Path.Combine(IntroLog.Data,"paused"));idleDisabled=File.Exists(Path.Combine(IntroLog.Data,"idle-disabled"));var menu=new Forms.ContextMenuStrip();
  menu.Items.Add("预览冷启动片头",null,delegate{Launch("preview-startup");});
  menu.Items.Add("预览新聊天片头",null,delegate{Launch("preview-new-chat");});
  menu.Items.Add("预览生气回应",null,delegate{PreviewFocus.Queue(menu,PreviewFocus.Prepare,delegate{IntroLog.Write("tray-preview-ready scene=anger");Launch("preview-anger");},PreviewFailed);});
  menu.Items.Add("预览闲置互动（仅 Codex 窗口）",null,delegate{PreviewFocus.Queue(menu,PreviewFocus.Prepare,delegate{StartIdlePreview(menu);},PreviewFailed);});
  menu.Items.Add("预览任务完成（仅 Codex 窗口）",null,delegate{PreviewFocus.Queue(menu,PreviewFocus.Prepare,delegate{StartCompletionPreview(menu);},PreviewFailed);});
  var videos=new Forms.ToolStripMenuItem("打开场景视频文件夹");
  foreach(VideoScene role in new[]{VideoScene.Startup,VideoScene.NewChat,VideoScene.Anger,VideoScene.Completion,VideoScene.Idle}){
   VideoScene scene=role;string folder=MediaLibrary.FolderFor(scene);
   videos.DropDownItems.Add(Path.GetFileName(folder),null,delegate{Directory.CreateDirectory(folder);using(Process.Start(new ProcessStartInfo{FileName=folder,UseShellExecute=true})){} });
  }
  menu.Items.Add(videos);
  menu.Items.Add("场景设置（音量 / 静音 / 开关）",null,delegate{if(settingsWindow==null||settingsWindow.IsDisposed)settingsWindow=new SceneSettingsForm(SceneSettings.Store,SceneSettings.PublishSaved);settingsWindow.Show();settingsWindow.BringToFront();});
  menu.Items.Add("完成提醒设置（位置 / 大小）",null,delegate{if(completionLayoutWindow==null||completionLayoutWindow.IsDisposed)completionLayoutWindow=new CompletionLayoutForm(CompletionLayoutSettings.Store,CompletionLayoutForm.LaunchPreview);completionLayoutWindow.Show();completionLayoutWindow.BringToFront();});
  menu.Items.Add("重新扫描视频",null,delegate{SignalResident("__reload__");IntroLog.Write("video-reload-manual");});
  menu.Items.Add("打开视频文件夹",null,delegate{Directory.CreateDirectory(MediaLibrary.Folder);using(Process.Start(new ProcessStartInfo{FileName=MediaLibrary.Folder,UseShellExecute=true})){} });
  var toggle=new Forms.ToolStripMenuItem("暂停自动片头"){Checked=paused};
  toggle.Click+=delegate{paused=!paused;toggle.Checked=paused;idlePolicy.Reset();Directory.CreateDirectory(IntroLog.Data);if(paused){File.WriteAllText(Path.Combine(IntroLog.Data,"paused"),"paused");SignalResident("__close_idle__");}else if(File.Exists(Path.Combine(IntroLog.Data,"paused")))File.Delete(Path.Combine(IntroLog.Data,"paused"));};
  menu.Items.Add(toggle);
  var idleToggle=new Forms.ToolStripMenuItem("启用闲置互动"){Checked=SceneSettings.Current.Get(VideoScene.Idle).Enabled};
  menu.Opening+=delegate{idleToggle.Checked=SceneSettings.Current.Get(VideoScene.Idle).Enabled;if(SceneSettings.LastError!=null)tray.ShowBalloonTip(5000,"场景设置暂时无法读取","已保留上次有效设置；请打开场景设置查看。首次读取失败时自动动画停用，手动预览仍可用。",Forms.ToolTipIcon.Warning);};
  idleToggle.Click+=delegate{try{string revision;var current=SceneSettings.Store.Read(out revision);var old=current.Get(VideoScene.Idle);SceneSettings.Store.Save(current.With(VideoScene.Idle,new ScenePreference(!old.Enabled,old.Muted,old.VolumePercent)),revision);SceneSettings.PublishSaved();idleToggle.Checked=!old.Enabled;idlePolicy.Reset();IntroLog.Write("idle-enabled="+!old.Enabled);}catch(Exception e){IntroLog.Write("idle-toggle-save-failed type="+e.GetType().Name);tray.ShowBalloonTip(5000,"闲置设置没有保存","请重新打开场景设置，检查文件占用或权限后重试。",Forms.ToolTipIcon.Warning);}};
  menu.Items.Add(idleToggle);
  bool embeddedIcon;trayIcon=TrayIconResource.Load(out embeddedIcon);
  tray=new Forms.NotifyIcon{Text="Codex 片头助手（不是 Codex 主程序）",Icon=trayIcon,ContextMenuStrip=menu,Visible=true};
  IntroLog.Write("tray-icon-loaded embedded="+embeddedIcon+" size="+trayIcon.Width+"x"+trayIcon.Height);
  // Keep desktop-start detection responsive without recursively enumerating every Codex log 10 times per second.
  // Notifications remain the fast path. Known live logs get a bounded tail
  // read without waiting for directory/completion reconciliation or doing I/O here.
  poll.Interval=100;poll.Tick+=delegate{try{Check();CheckIdle();CheckVideoReload();if(navigationTail.Take(navigationClock.ElapsedMilliseconds))navigationWorker.TryQueue(PollNavigationTails,delegate(Exception e){IntroLog.Write("navigation-tail-failed type="+e.GetType().Name);});if((DateTime.UtcNow-lastLogReconcileUtc).TotalSeconds>=5){lastLogReconcileUtc=DateTime.UtcNow;reconcile.TryQueue(delegate{PollLogFiles();if(!reconcile.Stopped&&completionJournal!=null)completionJournal.Reconcile();},delegate(Exception e){IntroLog.Write("watcher-reconcile-failed type="+e.GetType().Name);});}}catch(Exception e){IntroLog.Write("watcher-tick-failed "+e.Message);}};poll.Start();IntroLog.Write("watcher-started process-poll-ms=1000 log-reconcile-seconds=5 reconcile-background=True navigation-tail-ms=250 navigation-tail-max-files=16");
  StartNewDialogWatch();completionJournal=new CompletionJournal();StartVideoWatch();EnsureResidentPlayer();InstallClickHook("watcher-start");ThreadPool.QueueUserWorkItem(delegate{RuntimeCleanup.Prune(IntroLog.Data);});
 }
 void InstallClickHook(string reason) {
  if (mouseInput != null) mouseInput.Dispose();
  try { mouseInput = new RawMouseInput(OnMouseRelease); IntroLog.Write("raw-mouse-input-ready"); }
  catch (Exception error) { mouseInput = null; IntroLog.Write("raw-mouse-input-failed type=" + error.GetType().Name); }
 }
 void PreviewFailed(string reason){IntroLog.Write("tray-preview-rejected reason="+reason);tray.ShowBalloonTip(5000,"片头预览没有播放","请先打开并点击 Codex 窗口，再重试预览。",Forms.ToolTipIcon.Info);}
 void StartIdlePreview(Forms.ContextMenuStrip menu){
  IntroLog.Write("tray-preview-request scene=idle");
  ThreadPool.QueueUserWorkItem(delegate{
   string status=IdlePreview.RequestStatus();
   try{menu.BeginInvoke(new Action(delegate{
    if(status=="accepted"){IntroLog.Write("tray-preview-ready scene=idle");return;}
    IntroLog.Write("tray-preview-rejected scene=idle reason="+(status??"resident-unavailable"));
    tray.ShowBalloonTip(5000,"闲置预览没有播放","请确认 Codex 已打开、闲置文件夹有视频，并等其他动画结束后重试。",Forms.ToolTipIcon.Info);
   }));}catch(Exception e){IntroLog.Write("idle-preview-feedback-failed type="+e.GetType().Name);}
  });
 }
 void StartCompletionPreview(Forms.ContextMenuStrip menu){
  SignalResident("__close_idle__");
  var player=new Process{StartInfo=new ProcessStartInfo{FileName=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BootPlayer.exe"),Arguments="--task-preview",UseShellExecute=false,CreateNoWindow=true}};
  player.Exited+=delegate{
   try{if(player.ExitCode!=0)menu.BeginInvoke(new Action(delegate{IntroLog.Write("tray-preview-rejected reason=completion-unavailable");tray.ShowBalloonTip(5000,"完成预览没有播放","可能正在播放其他完成视频，或 Codex 已不在前台。请稍后重试。",Forms.ToolTipIcon.Info);}));}
   catch(Exception e){IntroLog.Write("completion-preview-feedback-failed type="+e.GetType().Name);}
   finally{player.Dispose();}
  };
  try{player.Start();int pid=player.Id;IntroLog.Write("tray-preview-ready scene=completion player-pid="+pid);player.EnableRaisingEvents=true;}
  catch{player.Dispose();throw;}
 }
 void StartVideoWatch(){try{Directory.CreateDirectory(MediaLibrary.Folder);foreach(VideoScene scene in new[]{VideoScene.Startup,VideoScene.NewChat,VideoScene.Anger,VideoScene.Completion,VideoScene.Idle})Directory.CreateDirectory(MediaLibrary.FolderFor(scene));videoWatcher=new FileSystemWatcher(MediaLibrary.Folder,"*.mp4"){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size};FileSystemEventHandler changed=delegate{Interlocked.Exchange(ref lastVideoChangeTicks,DateTime.UtcNow.Ticks);};videoWatcher.Created+=changed;videoWatcher.Changed+=changed;videoWatcher.Deleted+=changed;videoWatcher.Renamed+=delegate{Interlocked.Exchange(ref lastVideoChangeTicks,DateTime.UtcNow.Ticks);};videoWatcher.EnableRaisingEvents=true;IntroLog.Write("video-directory-watch-ready");}catch(Exception e){IntroLog.Write("video-directory-watch-failed "+e.Message);}}
 void CheckIdle(){
  idleDisabled=!SceneSettings.Current.Get(VideoScene.Idle).Enabled;
  uint inputTick,idleMs,nowTick;if(!IdleInput.TrySnapshot(out inputTick,out idleMs,out nowTick))return;
  if(Interlocked.Exchange(ref idleAccepted,0)==1){idlePolicy.Complete(nowTick);IntroLog.Write("idle-return-accepted");}
  IntPtr fg=GetAncestor(GetForegroundWindow(),2),hwnd;
  bool codexFg=fg!=IntPtr.Zero&&!IsIconic(fg)&&(windowBounds.ContainsKey(fg)||(idlePolicy.Pending&&CodexVideoWindow.TryGetForegroundCodex(out hwnd)));
  bool attempt=idlePolicy.Advance(inputTick,idleMs,nowTick,!paused&&!idleDisabled&&identities.Count>0,codexFg);
  if(lastIdleState!=idlePolicy.State){lastIdleState=idlePolicy.State;if(idlePolicy.Pending)idleAttemptToken=Guid.NewGuid().ToString("N");IntroLog.Write("idle-policy-state="+lastIdleState);}
  if(!attempt||DateTime.UtcNow<nextIdleAttemptUtc||Interlocked.CompareExchange(ref idleRequestActive,1,0)!=0)return;
  nextIdleAttemptUtc=DateTime.UtcNow.AddSeconds(1);
  // Never block the UI/mouse fast path while awaiting the resident's acceptance.
  string token=idleAttemptToken;ThreadPool.QueueUserWorkItem(delegate{try{if(RequestIdlePlay(token))Interlocked.Exchange(ref idleAccepted,1);}finally{Interlocked.Exchange(ref idleRequestActive,0);}});
 }
 string idleAttemptToken;
 void CheckVideoReload(){long changed=Interlocked.Read(ref lastVideoChangeTicks);if(changed==0||DateTime.UtcNow.Ticks-changed<TimeSpan.FromMilliseconds(1500).Ticks)return;if(Interlocked.CompareExchange(ref lastVideoChangeTicks,0,changed)!=changed)return;EnsureResidentPlayer();SignalResident("__reload__");IntroLog.Write("video-directory-reloaded");}
 void Check() {
  // Only process enumeration is throttled; navigation and idle retain their cadence.
  if (navigationClock.ElapsedMilliseconds < nextProcessScan) return;
  nextProcessScan = navigationClock.ElapsedMilliseconds + 1000;
  var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  var routeProcesses = new HashSet<string>(StringComparer.Ordinal);
  var windows = new HashSet<IntPtr>();
  foreach (var process in Process.GetProcessesByName("ChatGPT")) using (process) {
   try {
    if (!DesktopInstancePolicy.IsCodex(process)) continue;
    string identity = DesktopInstancePolicy.Identity(process);
    routeProcesses.Add(identity);
    IntPtr window = process.MainWindowHandle;
    if (window == IntPtr.Zero) { if (identities.Contains(identity)) current.Add(identity); continue; }
    current.Add(identity); windows.Add(window);
    WindowRect bounds; if (GetWindowRect(window, out bounds)) windowBounds[window] = bounds;
    if (identities.Add(identity) && DesktopInstancePolicy.Claim(identity) && !paused) {
     lock (dialogRouteLock) { dialogRoutes.MarkDesktopStarted(identity); }
     ThreadPool.QueueUserWorkItem(delegate { Launch("desktop-start"); });
     IntroLog.Write("desktop-instance-detected");
    }
   } catch { }
  }
  identities.IntersectWith(current);
  lock (dialogRouteLock) { dialogRoutes.Retain(routeProcesses); }
  foreach (IntPtr window in new List<IntPtr>(windowBounds.Keys)) if (!windows.Contains(window)) windowBounds.Remove(window);
  EnsureResidentPlayer();
 }
 void OnMouseRelease(int x, int y) {
  if (paused) return;
  IntPtr window = GetAncestor(WindowFromPoint(new MousePoint { X=x, Y=y }), 2);
  WindowRect bounds;
  if (!windowBounds.ContainsKey(window) || !GetWindowRect(window, out bounds) || IsIconic(window)) return;
  if (x > bounds.Left + (bounds.Right-bounds.Left)*0.55 || y > bounds.Top + (bounds.Bottom-bounds.Top)*0.55) return;
  QueueNavigationReadHint();
 }
 internal void QueueNavigationReadHint() {
  if(paused || navigationTail.Stopped)return;
  long now=navigationClock.ElapsedMilliseconds;
  if(now<nextClickRead)return;
  nextClickRead=now+NavigationTailPoll.IntervalMs;
  // A click has no application event ID. It can accelerate reading, but only
  // a confirmed log event may dispatch. No UIA child or guessed HWND mapping.
  navigationWorker.TryQueue(PollNavigationTails,delegate(Exception error){IntroLog.Write("navigation-click-read-failed type="+error.GetType().Name);});
 }
 static IEnumerable<string> FindLogRoots(){
  var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string local=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),@"AppData\Local");
  string physical=Path.Combine(local,"Codex","Logs");if(Directory.Exists(physical))roots.Add(physical);
  try{foreach(string candidate in Directory.GetDirectories(Path.Combine(local,"Packages"),"OpenAI.Codex_*")){string logs=Path.Combine(candidate,"LocalCache","Local","Codex","Logs");if(Directory.Exists(logs))roots.Add(logs);}}catch{}
  return roots;
 }
 void StartNewDialogWatch(){
  foreach(string root in FindLogRoots())try{
   var watcher=NavigationLogStartup.Start(root,SeedNavigationLog,delegate(string path){ReadNewLogLines(path,"startup-or-event");},delegate(string stage,Exception error){IntroLog.Write("new-dialog-initialization-failed stage="+stage+" type="+error.GetType().Name);});
   logWatchers.Add(watcher);
  }catch(Exception e){IntroLog.Write("new-dialog-poll-setup-failed type="+e.GetType().Name);}
  IntroLog.Write("new-dialog-watch-ready roots="+logWatchers.Count+" event-driven=true fallback-poll-seconds=5 navigation-tail-ms=250 navigation-tail-files="+navigationTail.Snapshot().Length);
 }
 void SeedNavigationLog(string path){
  navigationHistoryPending[path]=true;
  using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
   NavigationCursor cursor=NavigationLogStartup.Seed(stream);
   navigationCursors[path]=cursor;
   lock(logOffsets)logOffsets[path]=cursor.Position;
  }
  bool ignored;navigationHistoryPending.TryRemove(path,out ignored);
  SeedReady(path);TrackNavigationLog(path);
 }
 void ObserveReady(string path,string line){
  string identity;DateTime started;int pid;
  if(line.IndexOf("IAB_LIFECYCLE received browser sidebar owner sync",StringComparison.Ordinal)<0||!NewChatDiscovery.TryIdentity(path,out identity,out started,out pid))return;
  if(NewChatDiscovery.ReadyLine(path,line,pid,started)&&readyProcesses.TryAdd(identity,true))IntroLog.Write("codex-ui-ready-observed pid="+pid);
 }
 void SeedReady(string path){
  string identity;DateTime started;int pid;if(!NewChatDiscovery.TryIdentity(path,out identity,out started,out pid)||readyProcesses.ContainsKey(identity))return;
  using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
   if(NavigationLogStartup.HasReadyLine(stream,delegate(string line){return NewChatDiscovery.ReadyLine(path,line,pid,started);})&&readyProcesses.TryAdd(identity,true))IntroLog.Write("codex-ui-ready-seeded pid="+pid);
  }
 }
 void PollLogFiles(){
  foreach(string root in FindLogRoots())try{foreach(string file in Directory.GetFiles(root,"*.log",SearchOption.AllDirectories))ReadNewLogLines(file,"reconcile");}catch(Exception e){IntroLog.Write("new-dialog-poll-failed "+e.Message);}
  navigationTail.Retain(delegate(string path,string expected){string identity;DateTime started;int pid;return File.Exists(path)&&NewChatDiscovery.TryIdentity(path,out identity,out started,out pid)&&identity==expected;});
 }
 void TrackNavigationLog(string path){if(navigationTail.Contains(path))return;string identity;DateTime started;int pid;if(NewChatDiscovery.TryIdentity(path,out identity,out started,out pid)&&File.GetLastWriteTimeUtc(path)>=started)navigationTail.Track(path,identity);}
 void PollNavigationTails(){foreach(var pair in navigationTail.Snapshot()){if(navigationWorker.Stopped)return;ReadNewLogLines(pair.Key,"tail");}}
 void ReadNewLogLines(string path,string source="event") {
  if (navigationTail.Stopped) return;
  try {
   long seed; lock (logOffsets) { logOffsets.TryGetValue(path,out seed); }
   NavigationCursor cursor=navigationCursors.GetOrAdd(path,delegate(string key){
    // Failed startup files and previously unseen historical files must be
    // seeded on recovery, rather than replayed from byte zero.
    if(navigationHistoryPending.ContainsKey(key)||File.GetCreationTimeUtc(key)<navigationStartedUtc){
     NavigationCursor recovered;
     using(var stream=new FileStream(key,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))recovered=NavigationLogStartup.Seed(stream);
     try{SeedReady(key);}catch(Exception error){IntroLog.Write("new-dialog-initialization-failed stage=ready-retry type="+error.GetType().Name);}
     return recovered;
    }
    return new NavigationCursor(seed);
   });
   bool seededHistory;navigationHistoryPending.TryRemove(path,out seededHistory);
   // An event for one file cannot hold up another file's tail polling.
   lock (cursor) {
    if(navigationTail.Stopped)return;
    var elapsed=Stopwatch.StartNew(); string appended;
    using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) appended=cursor.Read(stream);
    long readMs=elapsed.ElapsedMilliseconds;
    TrackNavigationLog(path);
    using(var reader=new StringReader(appended)) {
     string line; while((line=reader.ReadLine())!=null) {
      if(navigationTail.Stopped)return;
      ObserveReady(path,line); HandleLogLine(path,line,source,readMs);
     }
    }
   }
  } catch(IOException) { } catch(Exception error) { IntroLog.Write("new-dialog-watch-read-failed type="+error.GetType().Name); }
 }
 internal static bool TryGetDialogRoute(string line,out bool blank){
  string token;return DialogRouteState.TryRoute(line,out blank,out token);
 }
 internal static bool AdvanceDialogRoute(ref bool currentlyBlank,bool blank){
  if(!blank){currentlyBlank=false;return false;}if(currentlyBlank)return false;currentlyBlank=true;return true;
 }
 readonly DialogRouteState dialogRoutes = new DialogRouteState();
 Func<string,string,string> dialogIdentity = ResolveDialogIdentity;
 static string ResolveDialogIdentity(string path,string line){
  string identity;DateTime started;int pid;
  if(!NewChatDiscovery.TryIdentity(path,out identity,out started,out pid))return null;
  // A PID in an old filename may now belong to a different process. Require
  // the event timestamp to belong to this particular process lifetime.
  return NewChatDiscovery.ReadyLine(path,line,pid,started)?identity:null;
 }
 Action<string> dialogDispatch = delegate(string reason){Launch(reason);};
 void HandleLogLine(string path,string line,string source,long readMs){
  bool blank;string token;if(!DialogRouteState.TryRoute(line,out blank,out token))return;
  int windowId;if(!DialogRouteState.TryWindowId(line,out windowId))return;
  string identity=dialogIdentity(path,line);if(String.IsNullOrEmpty(identity))return;
  int end=line.IndexOf(' ');DateTime recorded=DateTime.MinValue;
  bool dated=end>0&&DateTime.TryParse(line.Substring(0,end),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AdjustToUniversal|System.Globalization.DateTimeStyles.AssumeUniversal,out recorded);
  lock(dialogRouteLock){if(!dialogRoutes.Advance(identity,windowId,blank,token,dated?(DateTime?)recorded:null))return;}
  if(paused)return;
  long age=dated?(long)(DateTime.UtcNow-recorded).TotalMilliseconds:-1;
  IntroLog.Write("new-dialog-navigation-observed source="+source+" source-age-ms="+age+" read-ms="+readMs+" duplicate=False");
  dialogDispatch("new-dialog-log-fallback");
  ThreadPool.QueueUserWorkItem(delegate{IntroLog.Write("new-dialog-log-fallback-detected");});
 }
 static readonly string ResidentPipe=ResidentProtocol.Name;
 internal static string RequestIdleStatus(string token){try{using(var client=new NamedPipeClientStream(".",ResidentPipe,PipeDirection.InOut)){client.Connect(80);var writer=new StreamWriter(client){AutoFlush=true};var reader=new StreamReader(client);writer.WriteLine("__idle_return_request__:"+token);var response=reader.ReadLineAsync();return response.Wait(500)?response.Result:null;}}catch{return null;}}
 internal static bool RequestIdlePlay(string token){return RequestIdleStatus(token)=="accepted";}
 void EnsureResidentPlayer(){
  long now=navigationClock.ElapsedMilliseconds;
  try{using(Mutex.OpenExisting("Local\\CodexIntroResidentV1")){residentBackoff.MarkAlive(now);return;}}
  catch(WaitHandleCannotBeOpenedException){}
  catch(Exception e){IntroLog.Write("resident-check-failed "+e.Message);return;}
  if(File.Exists(Path.Combine(RuntimeData.Root,"disabled")))return;
  if(!residentBackoff.CanAttempt(now))return;
  long backoffMs=residentBackoff.RecordAttempt(now,false);
  try{using(Process.Start(new ProcessStartInfo{FileName=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BootPlayer.exe"),Arguments="--resident",UseShellExecute=false,CreateNoWindow=true})){}IntroLog.Write("resident-player-launch-requested backoff-ms="+backoffMs);}
  catch(Exception e){IntroLog.Write("resident-player-launch-failed "+e.Message);}
 }
 internal static bool SignalResident(string reason,ReactionTrace trace=null,string testPipe=null){try{using(NamedPipeClientStream client=new NamedPipeClientStream(".",testPipe??ResidentPipe,PipeDirection.Out)){if(trace!=null)trace.Record("pipe-connect-begin");client.Connect(reason=="desktop-start"?2000:80);if(trace!=null)trace.Record("pipe-connected");using(StreamWriter writer=new StreamWriter(client)){writer.WriteLine(reason);writer.Flush();}if(trace!=null)trace.Record("pipe-written");return true;}}catch(Exception e){if(trace!=null)trace.Record("pipe-failed",e.GetType().Name);return false;}}
 internal static void StartResidentPipe(IntroWindow window,string pipeName=null){
  string serverName=pipeName??ResidentPipe;
  for (int listener = 0; listener < 4; listener++) {
  Thread server=new Thread(new ThreadStart(delegate{
   while(true){
    try{
     using(NamedPipeServerStream pipe=ResidentProtocol.CreateServer(serverName)){
      pipe.WaitForConnection();
      using(var peer=ReactionPeer.Capture(pipe)){
       string reason=ResidentProtocol.ReadCommand(pipe,ResidentProtocol.ReadDeadlineMs);
       if(String.IsNullOrEmpty(reason))continue;
       ReactionTrace trace=reason=="reaction-angry"?new ReactionTrace(peer.Id,"resident"):null;
       if(trace!=null)trace.Record("pipe-received");
       if(reason.StartsWith("__idle_return_request__:",StringComparison.Ordinal)){
        string token=reason.Substring("__idle_return_request__:".Length);Guid parsed;
        object result=Guid.TryParseExact(token,"N",out parsed)?window.Dispatcher.Invoke(new Func<bool>(delegate{return window.TryPlayIdleOnce(token);}),TimeSpan.FromSeconds(2)):null;
        bool accepted=result is bool&&(bool)result;
        using(var response=new StreamWriter(pipe){AutoFlush=true}){response.WriteLine(accepted?"accepted":"deferred");}continue;
       }
       if(reason=="__idle_preview_request__"){
        object result=window.Dispatcher.Invoke(new Func<string>(delegate{string why;bool accepted=window.TryPlayIdle("preview-idle",out why);if(!accepted)IntroLog.Write("idle-preview-rejected reason="+why);return accepted?"accepted":why;}),TimeSpan.FromSeconds(2));
        string status=(result as string)??"ui-timeout";
        using(var response=new StreamWriter(pipe){AutoFlush=true}){response.WriteLine(status);}continue;
       }
       if(reason=="__task_complete__"||reason.StartsWith("__task_complete__:",StringComparison.Ordinal)){
        if(!SceneSettings.Allows("task-complete")){IntroLog.Write("completion-suppressed scene-disabled");continue;}
        try{
         string task=reason=="__task_complete__"?null:reason.Substring("__task_complete__:".Length);
         if(task!=null&&!Regex.IsMatch(task,"^[0-9a-f]{64}$"))continue;
         window.Dispatcher.Invoke(new Action(window.CloseIdle),TimeSpan.FromSeconds(2));
         using(Process player=Process.Start(new ProcessStartInfo{FileName=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BootPlayer.exe"),Arguments="--task-play",UseShellExecute=false,CreateNoWindow=true})){
          IntroLog.Write("completion-player-launched task="+(task==null?"legacy":task.Substring(0,12))+" player-pid="+player.Id+" delivery=resident-process");
         }
        }catch(Exception e){IntroLog.Write("completion-resident-launch-error type="+e.GetType().Name);}
        continue;
       }
       if(reason=="__settings_reload__"){SceneSettings.Refresh();continue;}
       if(trace!=null)trace.Record("dispatch-queued");
       window.Dispatcher.BeginInvoke(new Action(delegate{
        if(reason=="__reload__")window.ReloadSelected();
        else if(reason=="__close_idle__")window.CloseIdle();
        else window.PlayResident(reason,trace);
       }));
       if(trace!=null)trace.QueueFlush();
      }
     }
    }catch(Exception e){IntroLog.Write("resident-pipe-failed "+e.Message);Thread.Sleep(250);}
   }
  }));
  server.IsBackground=true;
  server.Name="Codex animation resident pipe";
  server.Start();
  }
 }
 internal static void Launch(string reason,ReactionTrace trace=null){if(!SceneSettings.Allows(reason)){if(trace!=null)trace.Record("rejected","scene-disabled");return;}if(SignalResident(reason,trace)){ThreadPool.QueueUserWorkItem(delegate{IntroLog.Write("trigger="+reason);IntroLog.Write("trigger-delivered=resident reason="+reason);});return;}try{using(Mutex.OpenExisting("Local\\CodexIntroResidentV1")){IntroLog.Write("resident-busy-no-overlap reason="+reason);return;}}catch(WaitHandleCannotBeOpenedException){}try{string extra=trace!=null&&ReactionTrace.ValidId(trace.Id)?" --reaction-trace "+trace.Id:"";using(Process.Start(new ProcessStartInfo{FileName=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BootPlayer.exe"),Arguments="--play "+reason+extra,UseShellExecute=false,CreateNoWindow=true})){}if(trace!=null)trace.Record("fallback-launched");ThreadPool.QueueUserWorkItem(delegate{IntroLog.Write("trigger="+reason);IntroLog.Write("resident-unavailable-cold-fallback reason="+reason);});}catch(Exception e){if(trace!=null)trace.Record("rejected","fallback-launch-failed");ThreadPool.QueueUserWorkItem(delegate{IntroLog.Write("launch-failed "+e.Message);});}}
 protected override void ExitThreadCore(){poll.Stop();navigationTail.Stop();navigationWorker.Stop();reconcile.Stop();if(mouseInput!=null)mouseInput.Dispose();foreach(var watcher in logWatchers){watcher.EnableRaisingEvents=false;watcher.Dispose();}if(completionJournal!=null)completionJournal.Dispose();if(videoWatcher!=null){videoWatcher.EnableRaisingEvents=false;videoWatcher.Dispose();}tray.Visible=false;tray.Dispose();trayIcon.Dispose();IntroLog.Write("watcher-exit");base.ExitThreadCore();}
}
internal static class BootPlayer {
 static bool EnsureWatcher(bool verbose){
  try{using(Mutex.OpenExisting("Local\\CodexIntroWatcherV2")){if(verbose)IntroLog.Write("ensure-watch-already-running");return true;}}
  catch(WaitHandleCannotBeOpenedException){}
  catch(Exception e){IntroLog.Write("ensure-watch-check-failed "+e.Message);return false;}
  try{using(Process.Start(new ProcessStartInfo{FileName=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BootPlayer.exe"),Arguments="--watch",UseShellExecute=true,CreateNoWindow=true})){}IntroLog.Write("ensure-watch-launched");return true;}
  catch(Exception e){IntroLog.Write("ensure-watch-launch-failed "+e.Message);return false;}
 }
 [STAThread]static int Main(string[] args){
  // Bind (or refuse) an isolated runtime root before any state type is touched.
  try{args=RuntimePaths.Apply(args);}
  catch(Exception e){Console.Error.WriteLine("runtime-root-rejected: "+e.Message);return RuntimePaths.RejectedExitCode;}
  string mode=args.Length>0?args[0]:"--play";bool watcher=mode=="--watch",resident=mode=="--resident";
  if(RuntimePaths.RequiresIsolatedRoot(mode)&&!RuntimePaths.IsBound){Console.Error.WriteLine("test-requires-isolated-runtime-root: "+mode);return RuntimePaths.RejectedExitCode;}
  if (mode=="--input-probe") return InputProbe.Run();
  if (mode=="--new-chat-hit") return NewChatHitTest.Probe(args);
  if (mode=="--repair-regression-test") return RepairRegressionTests.Run()?0:43;
  if (mode=="--repair-log-child" && args.Length==3) return RepairRegressionTests.LogChild(args[1],args[2]);
  if (mode=="--test-fail") return 47;
  if (mode=="--test-hang") { Thread.Sleep(30000); return 0; }
  if ((mode=="--hook"||mode=="--prompt-hook"||mode=="--stop-hook"||mode=="--supervise"||watcher||resident) && File.Exists(Path.Combine(RuntimeData.Root,"disabled"))) return 0;
  ReactionTrace fallbackTrace=mode=="--play"&&args.Length==4&&args[1]=="reaction-angry"&&args[2]=="--reaction-trace"&&ReactionTrace.ValidId(args[3])?new ReactionTrace(args[3],"fallback"):null;
  if(fallbackTrace!=null){fallbackTrace.Record("fallback-entry");fallbackTrace.QueueFlush();}
  if(mode=="--reaction-trace-test")return ReactionTraceTests.Run()?0:35;
  if(mode=="--reaction-flow-test"){bool pass=PromptReactionFlowTests.Run();IntroLog.Write("reaction-flow-test pass="+pass);return pass?0:37;}
  if(mode=="--watcher-reconcile-test"){bool pass=WatcherReconcile.SelfTest();IntroLog.Write("watcher-reconcile-test pass="+pass);return pass?0:36;}
  if(mode=="--reaction-trace-peer-test"&&args.Length==2)return ReactionTraceTests.PeerChild(args[1]);
  if(mode=="--scene-settings-test")return SceneSettingsTests.Run()?0:30;
  if(mode=="--completion-layout-test")return CompletionLayoutTests.Run()?0:31;
  if(mode=="--tray-icon-test")return TrayIconTests.Run()?0:32;
  if(mode=="--owned-viewport-test")return OwnedVideoViewportTests.Run()?0:33;
  if(mode=="--owner-follow-test")return OwnerWindowFollowTests.Run()?0:34;
  if(mode=="--completion-delivery-test")return CompletionDeliveryTests.Run()?0:42;
  if(mode=="--completion-delivery-peer-test")return CompletionDeliveryTests.Peer(args);
  if(mode=="--task-play"||mode=="--completion-host")CompletionLayoutSettings.Initialize();
  if(mode=="--notice-preview"){
   try{
    if(args.Length!=3)return 2;
    var layout=CompletionLayoutStore.Parse(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{schemaVersion=1,size=args[1],corner=args[2]}));
    SceneSettings.Initialize(false);
    bool created;using(var pending=new Mutex(true,"Local\\CodexCompletionPendingV1",out created)){
     if(!created){IntroLog.Write("completion-notice-preview-rejected reason=already-pending");return 2;}
     IntroLog.Write("completion-notice-preview-start size="+layout.Size+" corner="+layout.Corner);
     return CompletionPreview.Run(delegate(out CodexVideoWindow window,out string why){window=null;string path=MediaLibrary.Completion();if(path==null){why="missing-video";return false;}window=new CodexVideoWindow(IntPtr.Zero,path,VideoScene.Completion,true,true,layout);why="ready";return true;},delegate(CodexVideoWindow window){var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};app.Run(window);});
    }
   }catch(Exception e){IntroLog.Write("completion-notice-preview-failed type="+e.GetType().Name);return 2;}
  }
  if(mode=="--play"||mode=="--watch"||mode=="--resident"||mode=="--task-play"||mode=="--task-preview"||mode=="--completion-host"||mode=="--idle-window-play"||mode=="--anger-window-play"||mode=="--settings"||mode=="--probe")SceneSettings.Initialize(watcher||resident);
  if(mode=="--completion-host"){try{return args.Length==2?CompletionDeliveryHost.Run(args[1]):2;}catch(Exception e){IntroLog.Write("completion-delivery-host-failed type="+e.GetType().Name);return 2;}}
  if(mode=="--settings"){Forms.Application.Run(new SceneSettingsForm(SceneSettings.Store,SceneSettings.PublishSaved));return 0;}
  if((mode=="--task-play"&&!SceneSettings.Allows("task-complete"))||(mode=="--idle-window-play"&&!SceneSettings.Allows("idle-return"))||(mode=="--anger-window-play"&&!SceneSettings.Allows("reaction-angry"))||(mode=="--play"&&args.Length>1&&!SceneSettings.Allows(args[1]))){if(fallbackTrace!=null){fallbackTrace.Record("rejected","scene-disabled");fallbackTrace.Flush();}IntroLog.Write("scene-disabled mode="+mode);return 0;}
  if(mode=="--ensure-watch")return EnsureWatcher(true)?0:2;
  if(mode=="--button-probe")return NewChatDiscovery.Probe(args);
  if(mode=="--button-probe-hang-test"){Thread.Sleep(30000);return 0;}
  if(mode=="--resident-empty-test"){bool pass=EmptyResidentTests.Run();IntroLog.Write("resident-empty-test pass="+pass);return pass?0:24;}
  if(mode=="--resident-protocol-test"){bool pass=ResidentProtocolTests.SelfTest();IntroLog.Write("resident-protocol-test pass="+pass);return pass?0:23;}
  if(mode=="--navigation-log-test"){bool pass=NavigationLogReader.SelfTest();IntroLog.Write("navigation-log-test pass="+pass);return pass?0:22;}
  if(mode=="--navigation-tail-test")return NavigationTailPollTests.Run()?0:45;
  if(mode=="--button-discovery-test"){bool pass=NewChatDiscovery.SelfTest();IntroLog.Write("button-discovery-test pass="+pass);return pass?0:21;}
  if(mode.StartsWith("--runtime-",StringComparison.Ordinal)){
   try{
    if(mode=="--runtime-export"){RuntimeData.ExportLegacy();return 0;}
    if(mode=="--runtime-merge"){RuntimeData.MergeLegacy(args.Length>1&&args[1]=="--apply");return 0;}
    if(mode=="--runtime-probe"&&args.Length==2)return RuntimeData.Probe(args[1])?0:19;
    if(mode=="--runtime-data-test")return RuntimeData.SelfTest()?0:20;
   }catch(Exception e){IntroLog.Write("runtime-operation-failed type="+e.GetType().Name+" error="+e.Message);return 2;}
   return 2;
  }
  if(mode=="--lifetime-probe"&&args.Length==2)return CompanionLifetimeTests.Probe(args[1]);
  if(mode=="--lifetime-parent"&&args.Length==2)return CompanionLifetimeTests.Parent(args[1]);
  if(mode=="--lifetime-test")return CompanionLifetimeTests.Run()?0:17;
  if(mode=="--lifetime-check"&&args.Length==2){int id;return Int32.TryParse(args[1],out id)&&CompanionLifetime.CheckSupervisor(id)?0:18;}
  if(mode=="--adopt-running"){DesktopInstancePolicy.AdoptRunning();IntroLog.Write("desktop-existing-instances-adopted");return 0;}
  if(mode=="--desktop-instance-test"){bool pass=DesktopInstancePolicy.SelfTest();IntroLog.Write("desktop-instance-test pass="+pass);return pass?0:14;}
  if(mode=="--idle-pipe-probe"){bool pass=IntroWatcher.RequestIdleStatus("invalid-token")=="deferred";IntroLog.Write("idle-pipe-invalid-token-rejected="+pass);return pass?0:15;}
  // SessionStart is too late to represent a click. Recover the independent,
  // registered supervisor, never a cache-local watcher attached to the host.
  if(mode=="--hook")return CompanionLifetime.EnsureSupervisor()?0:2;
  if(mode=="--prompt-hook")return PromptReaction.RunHook();
  if(mode=="--stop-hook")return TaskCompletion.OnStop();
  if(mode=="--completion-journal-replay")return TaskCompletion.ReplayJournal();
  if(mode=="--completion-flow-test"){bool pass=CompletionFlowTests.Run();IntroLog.Write("completion-flow-test pass="+pass);return pass?0:16;}
  if(mode=="--idle-window-play"||(mode=="--play"&&args.Length>1&&args[1]=="preview-idle")){CodexVideoWindow idle;string why;if(!CodexVideoWindow.TryCreate(VideoScene.Idle,out idle,out why,mode=="--play")){IntroLog.Write("idle-suppressed reason="+why);return 0;}var idleApp=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};idleApp.Run(idle);return 0;}
  if(mode=="--anger-window-play"||(mode=="--play"&&args.Length>1&&(args[1]=="reaction-angry"||args[1]=="preview-anger"))){CodexVideoWindow anger;string why;if(!CodexVideoWindow.TryCreate(VideoScene.Anger,out anger,out why,false,fallbackTrace)){if(fallbackTrace!=null){fallbackTrace.Record("rejected",why);fallbackTrace.Flush();}IntroLog.Write("anger-window-suppressed reason="+why);return 0;}var angerApp=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};angerApp.Run(anger);if(fallbackTrace!=null)fallbackTrace.Flush();return 0;}
  if(mode=="--task-preview"){
   try{bool created;using(var pending=new Mutex(true,"Local\\CodexCompletionPendingV1",out created)){
    if(!created){IntroLog.Write("completion-preview-rejected reason=already-pending");return 2;}
    IntroLog.Write("completion-preview-player-start");
    return CompletionPreview.Run(delegate(out CodexVideoWindow window,out string reason){return CodexVideoWindow.TryCreate(VideoScene.Completion,out window,out reason);},delegate(CodexVideoWindow window){var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};app.Run(window);});
   }}catch(Exception e){IntroLog.Write("completion-preview-fatal type="+e.GetType().Name);return 2;}
  }
  if(mode=="--task-play"){
   try{
    if(MediaLibrary.Completion()==null){IntroLog.Write("completion-suppressed missing-video");return 0;}
    bool created;using(var pending=new Mutex(true,"Local\\CodexCompletionPendingV1",out created)){
     if(!created){IntroLog.Write("completion-suppressed already-pending");return 0;}
     IntroLog.Write("completion-player-start immediate=True");
     return CompletionPlayback.Run(CompletionPlayback.TryCreate,delegate(CodexVideoWindow taskWindow){var taskApp=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};taskApp.Run(taskWindow);});
    }
   }catch(Exception e){IntroLog.Write("completion-player-fatal type="+e.GetType().Name);return 2;}
  }
  if(mode=="--supervise"){
   try{if(args.Length>1&&args[1]=="--outside-host"&&CompanionLifetime.InJob())throw new InvalidOperationException("Broker launch is still host-bound; start from normal Windows PowerShell.");if(CompanionLifetime.DetachSupervisor())return 0;bool created;using(var supervisor=new Mutex(true,"Local\\CodexIntroSupervisorV1",out created)){
    if(!created){IntroLog.Write("duplicate-suppressed mode=--supervise");return 0;}
    IntroLog.Write("supervisor-started poll-ms=1000 host-job=False");
    var watchBackoff=new RestartBackoff();var watchClock=Stopwatch.StartNew();
    while(true){
     long now=watchClock.ElapsedMilliseconds;
     bool alive=false;
     try{using(Mutex.OpenExisting("Local\\CodexIntroWatcherV2"))alive=true;}catch(WaitHandleCannotBeOpenedException){}catch{alive=true;}
     if(alive){watchBackoff.MarkAlive(now);}
     else if(File.Exists(Path.Combine(RuntimeData.Root,"disabled"))){break;}
     else if(watchBackoff.CanAttempt(now)){watchBackoff.RecordAttempt(now,false);EnsureWatcher(false);}
     Thread.Sleep(1000);
    }
    return 0;
   }}catch(Exception e){IntroLog.Write("supervisor-failed "+e);return 2;}
  }
  if(mode=="--new-dialog-parser-test"){bool blank,old;bool blankFound=IntroWatcher.TryGetDialogRoute("info IAB_LIFECYCLE received browser sidebar owner sync conversationId=client-new-thread:test-token ownerRoutePath=/ windowId=1",out blank);bool oldFound=IntroWatcher.TryGetDialogRoute("info IAB_LIFECYCLE received browser sidebar owner sync conversationId=client-new-thread:old-token ownerRoutePath=/local/old windowId=1",out old);IntroLog.Write("new-dialog-parser-test blank="+(blankFound&&blank)+" oldThread="+(oldFound&&!old));return blankFound&&blank&&oldFound&&!old?0:4;}
  if(mode=="--route-transition-test"){bool state=false;bool oldOne=IntroWatcher.AdvanceDialogRoute(ref state,false);bool first=IntroWatcher.AdvanceDialogRoute(ref state,true);bool duplicate=IntroWatcher.AdvanceDialogRoute(ref state,true);bool oldTwo=IntroWatcher.AdvanceDialogRoute(ref state,false);bool second=IntroWatcher.AdvanceDialogRoute(ref state,true);bool pass=!oldOne&&first&&!duplicate&&!oldTwo&&second;IntroLog.Write("route-transition-test pass="+pass+" first="+first+" duplicate="+duplicate+" second="+second);return pass?0:5;}
  if(mode=="--anger-filter-test"){
   string[] positives={"你特么","你他妈到底会不会","Codex，tm又给我整错了","你这个傻逼，真是废物","你这什么破玩意儿","你连测试都没跑就交差","你又搞砸了","你到底行不行啊","怎么又不行了","越改越烂","浪费我时间","你在放屁","傻逼","傻比！","煞笔。","SB","废物！","我操你妈","草你妈的！","cnm","妈的","妈的傻逼","妈卖批","他妈的，又弄错了","卧槽，又搞错了","这点事都做不好","又给我搞砸了","这也能做错","写的什么破玩意儿","搞什么鬼","什么狗屁"};
   string[] negatives={"我特么太开心了","卧槽，这也太漂亮了","我操，中大奖了","垃圾分类怎么做","妈妈的生日","怎么解释傻逼这个词","请翻译‘我操你妈’这句话","比如有人说‘你是傻逼’，能触发动画吗？","测试：你特么会触发吗","你能不能帮我写句骂人台词：你特么","这个视频里有人说你特么","你帮我给视频取个名字","你帮我测试代码","这段代码有点问题，请修复","又报错了，请检查一下","<send_user_message_question_reply>\n[{\"question\":\"请发一次你是傻逼（测试）\",\"answer\":\"这次很快\"}]\n</send_user_message_question_reply>"};
   bool pass=true;foreach(string sample in positives)if(!PromptReaction.ShouldPlay(sample)){IntroLog.Write("anger-filter-false-negative sample="+Array.IndexOf(positives,sample));pass=false;}
   foreach(string sample in negatives)if(PromptReaction.ShouldPlay(sample)){IntroLog.Write("anger-filter-false-positive sample="+Array.IndexOf(negatives,sample));pass=false;}
   pass=AngerSensitivityTests.Run()&&pass;
   IntroLog.Write("anger-filter-test pass="+pass);return pass?0:7;
  }
  if(mode=="--anger-gate-test"){bool pass=IntroWindow.CanStartAnger(false,false,false,true)&&IntroWindow.CanStartAnger(false,false,false,true)&&!IntroWindow.CanStartAnger(true,false,false,true)&&!IntroWindow.CanStartAnger(false,true,false,true)&&!IntroWindow.CanStartAnger(false,false,true,true)&&IntroWindow.CanStartAnger(false,false,true,false);IntroLog.Write("anger-gate-test pass="+pass);return pass?0:11;}
  if(mode=="--window-focus-test"){bool pass=!CodexVideoWindow.ClosesOnFocusLoss(VideoScene.Anger)&&CodexVideoWindow.ClosesOnFocusLoss(VideoScene.Completion)&&!CodexVideoWindow.ClosesOnFocusLoss(VideoScene.Idle);IntroLog.Write("window-focus-test pass="+pass);return pass?0:12;}
  if(mode=="--idle-policy-test"){bool pass=IdleReturnPolicy.SelfTest()&&IntroWindow.CanStartIdle(false,false,false,false,false)&&!IntroWindow.CanStartIdle(true,false,false,false,false)&&!IntroWindow.CanStartIdle(false,true,false,false,false)&&!IntroWindow.CanStartIdle(false,false,true,false,false)&&!IntroWindow.CanStartIdle(false,false,false,true,false)&&!IntroWindow.CanStartIdle(false,false,false,false,true);IntroLog.Write("idle-policy-test pass="+pass);return pass?0:13;}
  if(mode=="--resident-scene-test"){string[] files=MediaLibrary.Prepared();if(files.Length<2)return 10;var testWindow=new IntroWindow(files[0],"--resident","desktop-start");bool pass=testWindow.SceneVisibilitySelfTest();testWindow.Close();IntroLog.Write("resident-scene-test pass="+pass);return pass?0:10;}
  if(mode=="--intro-media-recovery-test")return IntroMediaRecoveryTests.Run()?0:40;
  if(mode=="--intro-media-decoder-probe")return IntroMediaRecoveryTests.RunDecoderProbe()?0:41;
  if(mode=="--intro-priority-test"){var testWindow=new IntroWindow(null,"--resident","desktop-start");bool pass;try{pass=testWindow.IntroPrioritySelfTest();}finally{testWindow.Close();}IntroLog.Write("intro-priority-test pass="+pass);return pass?0:25;}
  if(mode=="--preview-focus-test"){bool pass=PreviewFocus.SelfTest();IntroLog.Write("preview-focus-test pass="+pass);return pass?0:26;}
  if(mode=="--completion-preview-test"){bool pass=CompletionPreview.SelfTest();IntroLog.Write("completion-preview-test pass="+pass);return pass?0:27;}
  if(mode=="--idle-preview-test"){bool pass=IdlePreview.SelfTest();IntroLog.Write("idle-preview-test pass="+pass);return pass?0:28;}
  if(mode=="--completion-playback-test"){bool pass=CompletionPlayback.SelfTest();IntroLog.Write("completion-playback-test pass="+pass);return pass?0:29;}
  if(mode=="--completion-filter-test"){
   string[] tasks={"帮我修复这个插件", "把音量调高一点", "请创建项目文件夹", "继续解决这个问题", "继续", "继续吧", "解决吧", "验证一下", "那你得验证一下结束任务触发得视频啊，现在也没触发啊", "Install this plugin", "给我完善啊", "能不能帮我修复这个问题"};
   string[] chats={"你好", "说个笑话", "这个能完成吗", "谢谢", "你是谁", "可是这个长任务结束为什么没触发那个完成任务动画啊", "这个插件现在完成了吗？", "如何使用这个功能", "你是傻逼（测试）", "你连测试都没跑就交差"};
   string[] done={"已修复并测试通过。", "搞定，视频已经设置好。", "完成了。", "## 已完成\n所有测试通过。", "Done. The patch is installed.", "理解，而且已经按这个规则改好：每种触发只读取自己的文件夹。", "项目已保存为本地 Git 提交，尚未上传 GitHub。", "已修复并更新你电脑上的助手。真实关机重启和第二台电脑验收仍未完成，暂不宣称稳定版。", "检查结果如下：\n1. 当前目录共 5 个文件。", "写好了，可以直接运行。", "已经优化并全部生效。", "All done. Everything is ready."};
   string[] failed={"无法完成，需要你先提供文件。", "还没完成。", "测试失败，我不能确认。", "已了解，我来处理。", "I could not complete this task.", "我已经看过了，之后会修复。", "已修复一部分，但任务仍未完成。", "已修复。第二台电脑测试失败，验收未完成。"};
   bool pass=true;foreach(string s in tasks)if(!TaskCompletion.IsTaskRequest(s))pass=false;foreach(string s in chats)if(TaskCompletion.IsTaskRequest(s))pass=false;foreach(string s in done)if(!TaskCompletion.HasCompleted(s))pass=false;foreach(string s in failed)if(TaskCompletion.HasCompleted(s))pass=false;if(!TaskCompletion.IsReactionOnly("你是傻逼（测试）")||!TaskCompletion.IsReactionOnly("傻逼")||TaskCompletion.IsReactionOnly("你特么，帮我修复这个错误")||!TaskCompletion.IsTaskRequest("妈的，修复这个错误"))pass=false;if(!TaskCompletion.IsTaskCandidate(false,120000)||TaskCompletion.IsTaskCandidate(false,119999)||!TaskCompletion.IsTaskCandidate(true,0))pass=false;IntroLog.Write("completion-filter-test pass="+pass);return pass?0:8;
  }
  if(mode=="--completion-journal-test"){bool pass=CompletionJournal.SelfTest(IntroLog.Data);IntroLog.Write("completion-journal-test pass="+pass);return pass?0:9;}
  if(mode=="--media-library-test"){bool pass=MediaLibrary.SelfTest();IntroLog.Write("media-library-test pass="+pass);return pass?0:6;}
  try{bool created;string mutexName=watcher?"Local\\CodexIntroWatcherV2":(resident?"Local\\CodexIntroResidentV1":"Local\\CodexIntroPlayerV2");using(var mutex=new Mutex(true,mutexName,out created)){
   if(!created){IntroLog.Write("duplicate-suppressed mode="+mode);return 0;}
   if(watcher){Forms.Application.Run(new IntroWatcher());return 0;}
   string reason=mode=="--play"&&args.Length>1?args[1]:mode=="--probe"&&args.Length>1?args[1]:"preview-new-chat";
   string[] prepared=resident?MediaLibrary.Prepared():null;
   string video=resident?(prepared.Length>0?prepared[0]:null):MediaLibrary.ForReason(reason);
   if(video==null&&!resident){IntroLog.Write("missing-video");return 2;}
   if(resident){IntroLog.Write("resident-player-start");var residentApp=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var residentWindow=new IntroWindow(video,mode,reason);IntroWatcher.StartResidentPipe(residentWindow);residentWindow.Show();residentApp.Run();IntroLog.Write("resident-player-exit code="+Environment.ExitCode);return Environment.ExitCode;}
   IntroLog.Write("player-start mode="+mode+" reason="+reason);var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};app.Run(new IntroWindow(video,mode,reason));IntroLog.Write("player-exit code="+Environment.ExitCode);return Environment.ExitCode;
  }}catch(Exception e){IntroLog.Write("fatal "+e);return 2;}
 }
}
