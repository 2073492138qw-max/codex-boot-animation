using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

internal class CompletionLayoutForm:Form {
 readonly CompletionLayoutStore store;
 readonly Action<CompletionLayoutPreference,Action<string>> preview;
 readonly ComboBox size=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=160,AccessibleName="完成提醒大小"};
 readonly ComboBox corner=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=160,AccessibleName="完成提醒位置"};
 readonly Label status=new Label{Dock=DockStyle.Fill,AutoEllipsis=true};
 readonly Button save=new Button{Text="保存设置",AutoSize=true},previewButton=new Button{Text="预览当前选择",AutoSize=true};
 string revision;
 internal CompletionLayoutForm(CompletionLayoutStore store,Action<CompletionLayoutPreference,Action<string>> preview){
  this.store=store;this.preview=preview;Text="Codex 片头助手 · 完成提醒设置";Font=new Font("Microsoft YaHei UI",9F);AutoScaleMode=AutoScaleMode.Dpi;
  ClientSize=new Size(630,310);MinimumSize=new Size(630,340);StartPosition=FormStartPosition.CenterScreen;
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=5};
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,64));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));Controls.Add(layout);
  layout.Controls.Add(new Label{Text="只调整 Codex 在后台或最小化时的任务完成提醒。\nCodex 前台仍在窗口内播放；不改触发、音量、速度或视频。",Dock=DockStyle.Fill},0,0);
  size.Items.AddRange(new object[]{"小（屏幕工作区宽约 30%）","中（约 40%）","大（约 50%，原默认）"});
  corner.Items.AddRange(new object[]{"右下角（原默认）","左下角"});size.Width=270;
  var fields=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2};fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,84));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
  fields.Controls.Add(new Label{Text="提醒大小",AutoSize=true,Anchor=AnchorStyles.Left},0,0);fields.Controls.Add(size,1,0);
  fields.Controls.Add(new Label{Text="提醒位置",AutoSize=true,Anchor=AnchorStyles.Left},0,1);fields.Controls.Add(corner,1,1);layout.Controls.Add(fields,0,1);
  layout.Controls.Add(new Label{Text="预览不保存；满意后再保存，下次完成提醒生效。\n提醒避开任务栏、不切回 Codex，可点“跳过”；尺寸按屏幕自适应。",Dock=DockStyle.Fill},0,2);
  var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
  save.Click+=delegate{SaveSettings();};previewButton.Click+=delegate{PreviewSelection();};
  var close=new Button{Text="关闭",AutoSize=true};close.Click+=delegate{Close();};
  var defaults=new Button{Text="填入默认值",AutoSize=true};defaults.Click+=delegate{Fill(CompletionLayoutPreference.Default);status.Text="默认值已填入，保存后才生效。";};
  buttons.Controls.Add(save);buttons.Controls.Add(close);buttons.Controls.Add(previewButton);buttons.Controls.Add(defaults);layout.Controls.Add(buttons,0,3);layout.Controls.Add(status,0,4);
  try{Fill(store.Read(out revision));status.Text="修改后请保存；直接关闭不保存。";}
  catch(Exception e){Fill(CompletionLayoutPreference.Default);save.Enabled=false;status.Text="旧设置无法读取，未覆盖；可预览，恢复文件需先确认。";IntroLog.Write("completion-layout-editor-read-failed type="+e.GetType().Name);}
 }
 void Fill(CompletionLayoutPreference value){size.SelectedIndex=(int)value.Size;corner.SelectedIndex=(int)value.Corner;}
 CompletionLayoutPreference Values(){return new CompletionLayoutPreference((CompletionNoticeSize)size.SelectedIndex,(CompletionNoticeCorner)corner.SelectedIndex);}
 void SaveSettings(){try{revision=store.Save(Values(),revision);status.Text="已保存：下次后台完成提醒生效。";IntroLog.Write("completion-layout-saved");}catch(InvalidOperationException){status.Text="其他窗口已修改，请关闭重开；未覆盖新设置。";}catch(Exception e){status.Text="保存失败，旧设置保留；请检查占用或权限。";IntroLog.Write("completion-layout-save-failed type="+e.GetType().Name);}}
 void PreviewSelection(){previewButton.Enabled=false;status.Text="正在预览当前选择（不保存）…";try{preview(Values(),PreviewResult);}catch(Exception e){PreviewResult("预览未启动，请重试。错误类型："+e.GetType().Name);}}
 void PreviewResult(string result){if(IsDisposed||Disposing)return;if(InvokeRequired){try{BeginInvoke(new Action<string>(PreviewResult),result);}catch(InvalidOperationException){}return;}previewButton.Enabled=true;status.Text=result;}
 internal static void LaunchPreview(CompletionLayoutPreference value,Action<string> completed){
  ThreadPool.QueueUserWorkItem(delegate{
   string result;
   try{using(var process=Process.Start(new ProcessStartInfo{FileName=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BootPlayer.exe"),Arguments="--notice-preview "+value.Size+" "+value.Corner,UseShellExecute=false,CreateNoWindow=true})){
    if(!process.WaitForExit(35000)){process.Kill();result="预览超时已停止；没有排队，请重试。";}
    else result=process.ExitCode==0?"预览已结束；满意后请保存，关闭不保存。":"预览未播放或中途失败；可能已有完成提醒或缺少视频。";
   }}catch(Exception e){IntroLog.Write("completion-layout-preview-launch-failed type="+e.GetType().Name);result="预览未启动，请检查助手后重试。";}
   completed(result);
  });
 }
 internal static bool SelfTest(CompletionLayoutStore store){
  int previews=0;
  using(var form=new SilentCompletionLayoutForm(store,delegate(CompletionLayoutPreference value,Action<string> done){if(value.Size==CompletionNoticeSize.Small&&value.Corner==CompletionNoticeCorner.BottomLeft)previews++;done("preview-test");})){
   form.size.SelectedIndex=0;form.corner.SelectedIndex=1;form.PreviewSelection();if(previews!=1||File.Exists(store.FilePath)||!form.previewButton.Enabled)return false;
   form.SaveSettings();string revision;var saved=store.Read(out revision);if(saved.Size!=CompletionNoticeSize.Small||saved.Corner!=CompletionNoticeCorner.BottomLeft)return false;
   form.Show();form.PerformLayout();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(Path.GetDirectoryName(store.FilePath),"completion-layout-ui.png"));}form.Hide();
   form.size.SelectedIndex=2; // Closing unsaved must not change Small.
  }
  string ignored;return store.Read(out ignored).Size==CompletionNoticeSize.Small;
 }
}

internal sealed class SilentCompletionLayoutForm:CompletionLayoutForm {
 internal SilentCompletionLayoutForm(CompletionLayoutStore store,Action<CompletionLayoutPreference,Action<string>> preview):base(store,preview){Opacity=0;ShowInTaskbar=false;}
 protected override bool ShowWithoutActivation{get{return true;}}
 protected override CreateParams CreateParams{get{var parameters=base.CreateParams;parameters.ExStyle|=0x08000000;return parameters;}}
}
