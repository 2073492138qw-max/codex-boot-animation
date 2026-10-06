using System;
using System.Drawing;
using System.Windows.Forms;

// A non-modal editor: opening settings never blocks the watcher or plays a clip.
internal class SceneSettingsForm:Form {
 readonly SceneSettingsStore store;
 readonly Action notify;
 readonly CheckBox[] enabled=new CheckBox[5],muted=new CheckBox[5];
 readonly NumericUpDown[] volume=new NumericUpDown[5];
 readonly Label status;
 readonly Button save;
 string revision;
 internal SceneSettingsForm(SceneSettingsStore store,Action notify){
  this.store=store;this.notify=notify;
  Text="Codex 片头助手 · 场景设置";Font=new Font("Microsoft YaHei UI",9F);AutoScaleMode=AutoScaleMode.Dpi;
  ClientSize=new Size(660,390);MinimumSize=new Size(650,420);StartPosition=FormStartPosition.CenterScreen;
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=5};
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,26));Controls.Add(layout);
  layout.Controls.Add(new Label{Text="分别控制五类动画。关闭自动触发后，手动预览仍可用。\n保存后从下一次播放生效；不会打断正在播放的视频。",Dock=DockStyle.Fill,AutoSize=false},0,0);
  var table=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=6};
  foreach(float width in new[]{34F,24F,18F,24F})table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,width));
  foreach(string text in new[]{"场景","自动触发","静音","音量（%）"})table.Controls.Add(new Label{Text=text,AutoSize=true,Anchor=AnchorStyles.Left});
  for(int i=0;i<5;i++){
   VideoScene scene=(VideoScene)i;
   table.Controls.Add(new Label{Text=NameFor(scene),AutoSize=true,Anchor=AnchorStyles.Left},0,i+1);
   enabled[i]=new CheckBox{AutoSize=true,Anchor=AnchorStyles.Left,AccessibleName=NameFor(scene)+"自动触发"};
   muted[i]=new CheckBox{AutoSize=true,Anchor=AnchorStyles.Left,AccessibleName=NameFor(scene)+"静音"};
   volume[i]=new NumericUpDown{Minimum=0,Maximum=100,Width=90,Anchor=AnchorStyles.Left,AccessibleName=NameFor(scene)+"音量"};
   table.Controls.Add(enabled[i],1,i+1);table.Controls.Add(muted[i],2,i+1);table.Controls.Add(volume[i],3,i+1);
  }
  layout.Controls.Add(table,0,1);
  layout.Controls.Add(new Label{Text="总暂停仍优先。音量不改变系统音量，也不会修改原视频。\n闲置开关与托盘使用同一设置；旧停用标记会保留，不删除。",Dock=DockStyle.Fill},0,2);
  var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
  save=new Button{Text="保存设置",AutoSize=true};save.Click+=delegate{SaveSettings();};
  var close=new Button{Text="关闭",AutoSize=true};close.Click+=delegate{Close();};
  var defaults=new Button{Text="填入默认值",AutoSize=true};defaults.Click+=delegate{Fill(SceneSettingsSnapshot.Defaults());status.Text="默认值已填入，点击保存才会生效。";};
  buttons.Controls.Add(save);buttons.Controls.Add(close);buttons.Controls.Add(defaults);layout.Controls.Add(buttons,0,3);
  status=new Label{Dock=DockStyle.Fill,AutoEllipsis=true};layout.Controls.Add(status,0,4);
  try{Fill(store.Read(out revision));status.Text="修改后请点击保存；直接关闭不会保存。";}
  catch(Exception e){Fill(SceneSettingsSnapshot.Defaults());save.Enabled=false;defaults.Enabled=false;status.Text="设置无法读取，未改旧文件。请查看备份或重新打开。";IntroLog.Write("scene-settings-editor-read-failed type="+e.GetType().Name);}
 }
 static string NameFor(VideoScene scene){switch(scene){case VideoScene.Startup:return "冷启动";case VideoScene.NewChat:return "新聊天";case VideoScene.Anger:return "生气回应";case VideoScene.Completion:return "任务完成";default:return "闲置互动";}}
 void Fill(SceneSettingsSnapshot snapshot){for(int i=0;i<5;i++){var value=snapshot.Get((VideoScene)i);enabled[i].Checked=value.Enabled;muted[i].Checked=value.Muted;volume[i].Value=value.VolumePercent;}}
 SceneSettingsSnapshot Values(){var values=new ScenePreference[5];for(int i=0;i<5;i++)values[i]=new ScenePreference(enabled[i].Checked,muted[i].Checked,(int)volume[i].Value);return new SceneSettingsSnapshot(values);}
 internal void SaveSettings(){
  try{revision=store.Save(Values(),revision);}
  catch(InvalidOperationException){status.Text="其他窗口已修改设置；请关闭并重新打开，未覆盖新设置。";return;}
  catch(Exception e){status.Text="保存失败，未宣称生效；请检查权限或文件占用后重试。";IntroLog.Write("scene-settings-editor-save-failed type="+e.GetType().Name);return;}
  try{notify();status.Text="已保存：从下一次播放生效。";}
  catch(Exception e){status.Text="设置已保存，但助手通知失败；请重新打开助手后检查。";IntroLog.Write("scene-settings-notify-failed type="+e.GetType().Name);}
 }
 internal static bool SelfTest(SceneSettingsStore store){
  int notifications=0;
  using(var form=new SilentSettingsTestForm(store,delegate{notifications++;})){
   if(!form.save.Enabled||form.volume.Length!=5||form.enabled.Length!=5)return false;
   form.enabled[1].Checked=false;form.muted[2].Checked=true;form.volume[3].Value=22;
   form.SaveSettings();string revision;var saved=store.Read(out revision);
   if(notifications!=1||saved.Get(VideoScene.NewChat).Enabled||!saved.Get(VideoScene.Anger).Muted||saved.Get(VideoScene.Completion).VolumePercent!=22)return false;
   form.Show();form.PerformLayout();Application.DoEvents();
   using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(store.FilePath),"scene-settings-ui.png"));}
   form.Hide();
   form.volume[3].Value=99; // Closing without Save must retain 22.
  }
  string ignored;return store.Read(out ignored).Get(VideoScene.Completion).VolumePercent==22;
 }
}
