using System;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace OneCConfigExporter {
 public sealed class BatchBase {
  public string Name="",Connection="",FilePath="",Server="",Database="",DbUser="",DbPassword="",RepoUser="",RepoPassword="",Output="",GithubRepo="";
  public string FullCommitMessage="",IncrementalCommitMessage="";
  public static string DefaultCommitMessage(bool incremental){return incremental?"Инкрементальная выгрузка конфигурации 1С":"Полная выгрузка конфигурации 1С";}
  public void EnsureCommitMessages(string legacy){bool custom=!String.IsNullOrWhiteSpace(legacy)&&legacy!="Update 1C configuration";if(String.IsNullOrWhiteSpace(FullCommitMessage))FullCommitMessage=custom?legacy+" — полная выгрузка":DefaultCommitMessage(false);if(String.IsNullOrWhiteSpace(IncrementalCommitMessage))IncrementalCommitMessage=custom?legacy+" — инкрементальная выгрузка":DefaultCommitMessage(true);}
  public string MessageFor(bool incremental){string text=incremental?IncrementalCommitMessage:FullCommitMessage;return String.IsNullOrWhiteSpace(text)?DefaultCommitMessage(incremental):text;}
  public bool Enabled=true,FileMode=true,Incremental=false,Commit=false,Push=false;
  public BatchBase Copy(){return (BatchBase)MemberwiseClone();}
 }
 internal static class ConnectionParser {
  static string Value(string text,string key){if(text==null||text.Length>4096)return null;var match=Regex.Match(text,@"(?:^|;)\s*"+key+@"\s*=\s*""([^""]*)""\s*(?:;|$)",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));return match.Success?match.Groups[1].Value:null;}
  public static bool Apply(string text,BatchBase item){
   string file=Value(text,"File"),server=Value(text,"Srvr"),database=Value(text,"Ref");
   if(file!=null&&!String.IsNullOrWhiteSpace(file)){item.FileMode=true;item.FilePath=file;item.Connection=text;return true;}
   if(server!=null&&database!=null&&!String.IsNullOrWhiteSpace(server)&&!String.IsNullOrWhiteSpace(database)){item.FileMode=false;item.Server=server;item.Database=database;item.Connection=text;return true;}
   return false;
  }
 }
 internal sealed class BatchBaseForm : Form {
  public BatchBase Result;readonly ToolTip integrationTip=new ToolTip();bool resourcesReleased;
  TextBox message=new TextBox();bool loadingMessage=true,messageMode;
  ComboBox registered=new ComboBox(),github=new ComboBox();TextBox name=new TextBox(),connection=new TextBox(),path=new TextBox(),server=new TextBox(),database=new TextBox(),user=new TextBox(),password=new TextBox(),storageUser=new TextBox(),storagePassword=new TextBox(),output=new TextBox();Button repositoryList=new Button{Text="Обновить…",AutoSize=true};Func<Task<string[]>> loadRepositories;
  RadioButton file=new RadioButton{Text="Файловая",AutoSize=true},remote=new RadioButton{Text="Серверная",AutoSize=true},full=new RadioButton{Text="Начальная (полная)",AutoSize=true},incremental=new RadioButton{Text="Инкрементальная",AutoSize=true};
  CheckBox commit=new CheckBox{Text="Коммит в локальный Git",AutoSize=true},push=new CheckBox{Text="Отправить на GitHub",AutoSize=true};
  Control fileLabel,serverLabel,databaseLabel;Button fileBrowse;bool gitAvailable,githubAvailable,canLoadRepositories;
  public BatchBaseForm(BatchBase original,object[] bases,bool gitReady,bool githubReady):this(original,bases,gitReady,githubReady,null){}
  public BatchBaseForm(BatchBase original,object[] bases,bool gitReady,bool githubReady,Func<Task<string[]>> repositories):this(original,bases,gitReady,githubReady,repositories,new string[0]){}
  public BatchBaseForm(BatchBase original,object[] bases,bool gitReady,bool githubReady,Func<Task<string[]>> repositories,string[] cachedRepositories){
   loadRepositories=repositories;
   gitAvailable=gitReady;githubAvailable=githubReady&&gitReady;canLoadRepositories=githubReady;Result=original.Copy();Result.EnsureCommitMessages(null);Text="Параметры базы";Font=new Font("Segoe UI",9);ClientSize=new Size(790,600);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;
   var grid=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=4};grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));Controls.Add(grid);
   Add(grid,"Название:",name,0,0);grid.SetColumnSpan(name,3);
   foreach(var field in new[]{name,connection,path,server,database,user,password,storageUser,storagePassword,output,message})field.MaxLength=4096;github.MaxLength=256;
   registered.DropDownStyle=ComboBoxStyle.DropDownList;registered.Items.AddRange(bases);Add(grid,"Зарегистрированная база:",registered,0,1);grid.SetColumnSpan(registered,3);registered.SelectedIndexChanged+=(s,e)=>{var b=registered.SelectedItem as Infobase;if(b!=null&&!String.IsNullOrWhiteSpace(b.Connection)){var parsed=Result.Copy();if(ConnectionParser.Apply(b.Connection,parsed)){SetConnection(parsed);name.Text=b.Name;connection.Text=b.Connection;}}};
   Add(grid,"Заполнить параметры подключения\r\nиз строки подключения:",connection,0,2);grid.SetColumnSpan(connection,2);var parse=new Button{Text="Заполнить",AutoSize=true};grid.Controls.Add(parse,3,2);parse.Click+=(s,e)=>ParseConnection();
   var modes=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};modes.Controls.Add(file);modes.Controls.Add(remote);grid.Controls.Add(modes,1,3);grid.SetColumnSpan(modes,3);
   fileLabel=Add(grid,"Каталог базы:",path,0,4);grid.SetColumnSpan(path,2);fileBrowse=new Button{Text="Обзор…",AutoSize=true};fileBrowse.Click+=(s,e)=>Folder(path);grid.Controls.Add(fileBrowse,3,4);
   serverLabel=Add(grid,"Сервер:",server,0,5);databaseLabel=Add(grid,"Имя базы:",database,2,5);
   Add(grid,"Пользователь 1С:",user,0,6);Add(grid,"Пароль 1С:",password,2,6);password.UseSystemPasswordChar=true;
   Add(grid,"Пользователь хранилища:",storageUser,0,7);Add(grid,"Пароль хранилища:",storagePassword,2,7);storagePassword.UseSystemPasswordChar=true;
   Add(grid,"Каталог файлов / Git:",output,0,8);grid.SetColumnSpan(output,2);var browse=new Button{Text="Обзор…",AutoSize=true};browse.Click+=(s,e)=>Folder(output);grid.Controls.Add(browse,3,8);
   full.Font=incremental.Font=new Font(Font,FontStyle.Bold);var exportMode=new FlowLayoutPanel{AutoSize=true,FlowDirection=FlowDirection.TopDown,Dock=DockStyle.Fill,BackColor=Color.LightGoldenrodYellow,Padding=new Padding(5)};exportMode.Controls.Add(full);exportMode.Controls.Add(incremental);Add(grid,"Режим выгрузки:",exportMode,0,9);grid.SetColumnSpan(exportMode,3);
   var git=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};git.Controls.Add(TooltipContainer(commit,"Укажите расположение файла git.exe в настройках"));git.Controls.Add(TooltipContainer(push,githubReady?"Укажите расположение файла git.exe в настройках":"Заполните параметры соединения с Github"));grid.Controls.Add(git,1,10);grid.SetColumnSpan(git,3);commit.Enabled=gitAvailable;push.Enabled=githubAvailable;push.CheckedChanged+=(s,e)=>{if(push.Checked)commit.Checked=true;};commit.CheckedChanged+=(s,e)=>{if(!commit.Checked)push.Checked=false;};
   github.DropDownStyle=ComboBoxStyle.DropDown;github.Items.AddRange(cachedRepositories??new string[0]);repositoryList.Image=RefreshImage();repositoryList.ImageAlign=ContentAlignment.MiddleLeft;repositoryList.TextImageRelation=TextImageRelation.ImageBeforeText;Add(grid,"Репозиторий GitHub:",github,0,11);grid.SetColumnSpan(github,2);repositoryList.Enabled=canLoadRepositories&&loadRepositories!=null;repositoryList.Click+=async(s,e)=>await LoadRepositories();grid.Controls.Add(repositoryList,3,11);
   var note=new Label{Text="Сообщение коммита сохраняется отдельно для полной и инкрементальной выгрузки.\r\nGitHub: локальные файлы имеют приоритет; удалённая история сохраняется.\r\nТокен GitHub должен иметь доступ ко всем репозиториям, используемым в списке баз.",AutoSize=true,MaximumSize=new Size(740,0),ForeColor=Color.DimGray};Add(grid,"Сообщение коммита:",message,0,12);grid.SetColumnSpan(message,3);full.CheckedChanged+=(s,e)=>{if(full.Checked)SwitchCommitMode(false);};incremental.CheckedChanged+=(s,e)=>{if(incremental.Checked)SwitchCommitMode(true);};
   integrationTip.SetToolTip(push,"При расхождении историй локальный снимок имеет приоритет. Утилита сохраняет обе истории и повторяет отправку один раз, без force-push.");
   grid.Controls.Add(note,0,13);grid.SetColumnSpan(note,4);
   var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};var ok=new Button{Text="Сохранить",AutoSize=true};var cancel=new Button{Text="Отмена",AutoSize=true,DialogResult=DialogResult.Cancel};buttons.Controls.Add(ok);buttons.Controls.Add(cancel);grid.Controls.Add(buttons,0,14);grid.SetColumnSpan(buttons,4);CancelButton=cancel;AcceptButton=ok;ok.Click+=(s,e)=>Save();
   file.CheckedChanged+=(s,e)=>UpdateMode();remote.CheckedChanged+=(s,e)=>UpdateMode();name.Text=Result.Name;SetConnection(Result);connection.Text=Result.Connection;user.Text=Result.DbUser;password.Text=Result.DbPassword;storageUser.Text=Result.RepoUser;storagePassword.Text=Result.RepoPassword;output.Text=Result.Output;github.Text=Result.GithubRepo;incremental.Checked=Result.Incremental;full.Checked=!Result.Incremental;commit.Checked=gitAvailable&&Result.Commit;push.Checked=githubAvailable&&Result.Push;messageMode=Result.Incremental;message.Text=Result.MessageFor(messageMode);loadingMessage=false;
  }
  static Control Add(TableLayoutPanel grid,string label,Control control,int col,int row){var text=new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left};grid.Controls.Add(text,col,row);control.Dock=DockStyle.Fill;grid.Controls.Add(control,col+1,row);return text;}
  static Image RefreshImage(){var image=new Bitmap(16,16);using(var g=Graphics.FromImage(image))using(var pen=new Pen(Color.RoyalBlue,2)){g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.DrawArc(pen,3,3,10,10,45,285);g.DrawLines(pen,new[]{new Point(10,1),new Point(13,4),new Point(9,4)});}return image;}
  Control TooltipContainer(CheckBox control,string message){var panel=new Panel{AutoSize=true};panel.Controls.Add(control);control.EnabledChanged+=(s,e)=>integrationTip.SetToolTip(panel,control.Enabled?"":message);integrationTip.SetToolTip(panel,control.Enabled?"":message);return panel;}
  protected override void Dispose(bool disposing){bool first=disposing&&!resourcesReleased;var root=Font;var mode=full.Font;if(first){resourcesReleased=true;integrationTip.Dispose();if(repositoryList.Image!=null)repositoryList.Image.Dispose();}base.Dispose(disposing);if(first){root.Dispose();if(mode!=root)mode.Dispose();}}
  void SetConnection(BatchBase b){file.Checked=b.FileMode;remote.Checked=!b.FileMode;path.Text=b.FilePath;server.Text=b.Server;database.Text=b.Database;UpdateMode();}
  void UpdateMode(){if(fileLabel==null)return;fileLabel.Visible=path.Visible=fileBrowse.Visible=file.Checked;serverLabel.Visible=server.Visible=databaseLabel.Visible=database.Visible=remote.Checked;}
  void Folder(TextBox box){using(var dialog=new FolderBrowserDialog{SelectedPath=Directory.Exists(box.Text)?box.Text:""})if(dialog.ShowDialog(this)==DialogResult.OK)box.Text=dialog.SelectedPath;}
  void ParseConnection(){var b=Result.Copy();if(ConnectionParser.Apply(connection.Text,b))SetConnection(b);else MessageBox.Show(this,"Укажите строку File=\"каталог\"; или Srvr=\"сервер\";Ref=\"база\";");}
  async Task LoadRepositories(){
   if(loadRepositories==null)return;repositoryList.Enabled=false;
   try{string selected=github.Text;var names=await loadRepositories();if(IsDisposed)return;github.Items.Clear();github.Items.AddRange(names);if(!String.IsNullOrWhiteSpace(selected))github.Text=selected;else if(names.Length>0)github.SelectedIndex=0;}
   catch(Exception ex){if(!IsDisposed)MessageBox.Show(this,ex.Message,"GitHub",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
   finally{if(!IsDisposed)repositoryList.Enabled=canLoadRepositories&&loadRepositories!=null;}
  }
  void StoreCommitMessage(){if(messageMode)Result.IncrementalCommitMessage=message.Text;else Result.FullCommitMessage=message.Text;}
  void SwitchCommitMode(bool mode){if(loadingMessage)return;StoreCommitMessage();messageMode=mode;message.Text=Result.MessageFor(mode);}
  void Save(){
   if(commit.Checked&&String.IsNullOrWhiteSpace(message.Text)){MessageBox.Show(this,"Укажите сообщение коммита.");return;}
   StoreCommitMessage();
   if(String.IsNullOrWhiteSpace(name.Text)||String.IsNullOrWhiteSpace(output.Text)||(file.Checked?String.IsNullOrWhiteSpace(path.Text):String.IsNullOrWhiteSpace(server.Text)||String.IsNullOrWhiteSpace(database.Text))){MessageBox.Show(this,"Заполните название, подключение и каталог выгрузки.");return;}
   if(push.Checked&&!Regex.IsMatch(github.Text.Trim(),@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")){MessageBox.Show(this,"Укажите репозиторий GitHub: владелец/имя.");return;}
   Result.Name=name.Text.Trim();Result.Connection=connection.Text;Result.FileMode=file.Checked;Result.FilePath=path.Text.Trim();Result.Server=server.Text.Trim();Result.Database=database.Text.Trim();Result.DbUser=user.Text;Result.DbPassword=password.Text;Result.RepoUser=storageUser.Text;Result.RepoPassword=storagePassword.Text;Result.Output=output.Text.Trim();Result.GithubRepo=github.Text.Trim();Result.Incremental=incremental.Checked;Result.Commit=commit.Checked;Result.Push=push.Checked;DialogResult=DialogResult.OK;Close();
  }
 }
}
