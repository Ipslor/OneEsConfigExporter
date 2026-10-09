using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;
using System.Net.Http;
using System.Web.Script.Serialization;
using System.Xml;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using Timer=System.Windows.Forms.Timer;

[assembly: AssemblyVersion("2.5.0.0")]
[assembly: AssemblyFileVersion("2.5.0.0")]

namespace OneCConfigExporter {
internal static class Program {
 [STAThread] static int Main(string[] args) {
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  if(args.Length==0){Application.Run(new MainForm());return 0;}
  string settings=null,result=null;bool automatic=false;
  for(int i=0;i<args.Length;i++){if(args[i]=="--run-batch")automatic=true;else if(args[i]=="--settings"&&i+1<args.Length)settings=args[++i];else if(args[i]=="--result"&&i+1<args.Length)result=args[++i];else return 2;}
  if(!automatic||String.IsNullOrWhiteSpace(settings))return 2;
  int code=2;using(var form=new MainForm())using(var context=new ApplicationContext()){
   var handle=form.Handle;form.BeginInvoke(new Action(async()=>{try{code=await form.RunAutomatic(settings,result);}catch{code=2;}finally{context.ExitThread();}}));Application.Run(context);
  }return code;
 }
}
internal sealed class Infobase { public string Name, Connection; public override string ToString() { return Name; } }

internal sealed class MainForm : Form {
 ComboBox bases=new ComboBox(), platform=new ComboBox();
 RadioButton fileMode=new RadioButton(), serverMode=new RadioButton(), fullMode=new RadioButton(), incrementalMode=new RadioButton();
 TextBox filePath=new TextBox(), server=new TextBox(), database=new TextBox(), output=new TextBox();
 TextBox dbUser=new TextBox(), dbPassword=new TextBox(), repoUser=new TextBox(), repoPassword=new TextBox();
 CheckBox commitGit=new CheckBox(); TextBox gitPath=new TextBox(), commitMessage=new TextBox();
 CheckBox pushGit=new CheckBox();TextBox githubUser=new TextBox(),githubToken=new TextBox();ComboBox githubRepo=new ComboBox();TabControl mainTabs;
 TextBox logBox=new TextBox(), preview=new TextBox();string currentCommand="",activeBaseName="";
 ProgressBar progress=new ProgressBar(); Label status=new Label(); Button run=new Button(), cancel=new Button();
 Process activeProcess,activeGitProcess; string activeLog; Timer logTimer; TabControl logTabs; string diagnostic="", platformLog="";
 string settingsPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings.dat");string settingsSignature="missing";bool settingsLoaded,settingsLoadFailed;Button saveSettings=new Button();
 CheckBox ignoreLargeFiles=new CheckBox();NumericUpDown maxFileMegabytes=new NumericUpDown{Minimum=1,Maximum=100000,Value=30};
 Button appSettings=new Button();Label platformIndicator=new Label(),gitIndicator=new Label(),githubIndicator=new Label();ToolTip indicatorsTip=new ToolTip();
 bool githubReady,requestedPush;int connectionVersion;readonly CancellationTokenSource lifetimeCancellation=new CancellationTokenSource();CancellationTokenSource batchCancellation;Task<string[]> repositoryRequest;string repositoryRequestKey="";
 bool gitReady,operationBusy,initializing;ListBox currentFiles=new ListBox();FileSystemWatcher exportWatcher;
 int monitorVersion;bool resourcesReleased;ConcurrentQueue<string> fileEvents=new ConcurrentQueue<string>();HashSet<string> displayedFiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
 HelpForm helpWindow;
 Panel batchPanel=new Panel{Dock=DockStyle.Fill};
 DataGridView batchGrid=new DataGridView();List<BatchBase> batchBases=new List<BatchBase>();bool batchRunning,batchStopRequested;string lastExportError;bool lastExportSuccess;
 List<Button> batchButtons=new List<Button>();List<string> completedBatchLogs=new List<string>();
 sealed class OperationSummary{public List<string> Done=new List<string>();public string Current="Ожидает запуска";}
 Dictionary<BatchBase,OperationSummary> operationSummaries=new Dictionary<BatchBase,OperationSummary>();BatchBase activeBatchBase;
 CheckBox batchIgnoreLarge=new CheckBox{Text="Игнорировать файлы больше",AutoSize=true};NumericUpDown batchMaxSize=new NumericUpDown{Minimum=1,Maximum=100000,Value=30,Width=70};
 string[] githubRepositoryCache=new string[0];int dateVersion;bool headless;int lastBatchFailures;Button scenarioButton=new Button{Text="Сохранить как сценарий",AutoSize=true};

 public MainForm() {
  Text="Выгрузка конфигурации 1С v.2.5"; Font=new Font("Segoe UI",9); MinimumSize=new Size(900,680); Size=new Size(1060,780); StartPosition=FormStartPosition.CenterScreen;
  var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(14),ColumnCount=1,RowCount=8};
  root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
  root.RowStyles.Clear();root.RowCount=3;root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
  root.Controls.Add(BuildSettingsHeader(),0,0);
  mainTabs=new TabControl{Dock=DockStyle.Fill};var exportTab=new TabPage("Выгрузка");var journalTab=new TabPage("Журнал");
  var settings=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};settings.RowStyles.Add(new RowStyle(SizeType.Percent,65));settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));settings.RowStyles.Add(new RowStyle(SizeType.Percent,35));
  var information=new GroupBox{Text="Информационная база",Dock=DockStyle.Fill,Padding=new Padding(6)};BuildBatchPanel();information.Controls.Add(batchPanel);settings.Controls.Add(information,0,0);
  settings.Controls.Add(BuildCommonExportSettings(),0,1);
  var filesGroup=new GroupBox{Text="Создаваемые и изменяемые файлы",Dock=DockStyle.Fill,Padding=new Padding(6),MinimumSize=new Size(0,30)};currentFiles.Dock=DockStyle.Fill;currentFiles.HorizontalScrollbar=true;filesGroup.Controls.Add(currentFiles);settings.Controls.Add(filesGroup,0,2);exportTab.Controls.Add(settings);
  journalTab.Controls.Add(BuildLogGroup());mainTabs.TabPages.AddRange(new[]{exportTab,journalTab});root.Controls.Add(mainTabs,0,1);root.Controls.Add(BuildFooter(),0,2);
  fileMode.Checked=true; fullMode.Checked=true; dbPassword.UseSystemPasswordChar=true; repoPassword.UseSystemPasswordChar=true; bases.DropDownStyle=ComboBoxStyle.DropDownList; platform.DropDownStyle=ComboBoxStyle.DropDown;
  bases.SelectedIndexChanged+=(s,e)=>ApplySelectedBase(); fileMode.CheckedChanged+=(s,e)=>{UpdatePreview();}; serverMode.CheckedChanged+=(s,e)=>{UpdatePreview();}; fullMode.CheckedChanged+=(s,e)=>UpdatePreview();
  foreach(Control c in new Control[]{filePath,server,database,platform,output,dbUser,dbPassword,repoUser,repoPassword}) c.TextChanged+=(s,e)=>UpdatePreview();
  UpdateRunAvailability();Shown+=async(s,e)=>await SafeUiAction(async()=>{LoadDefaults();initializing=true;run.Enabled=appSettings.Enabled=false;try{await RefreshConnections();}finally{if(!IsDisposed){initializing=false;appSettings.Enabled=true;UpdateRunAvailability();}}}); FormClosing+=OnFormClosing;
 }
 GroupBox Group(string text) { return new GroupBox{Text=text,Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(6),Margin=new Padding(0,2,0,3)}; }
 TableLayoutPanel Grid(int n) { var grid=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=n,Padding=new Padding(2)};grid.ControlAdded+=(s,e)=>e.Control.Margin=new Padding(3,1,3,1);return grid; }
 void AddLabel(TableLayoutPanel g,string text,Control c,int col,int row) { g.Controls.Add(new Label{Text=text,AutoSize=true,Anchor=AnchorStyles.Left},col,row); c.Dock=DockStyle.Fill; g.Controls.Add(c,col+1,row); }
 Control BuildSettingsHeader(){
  var header=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=3};header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
  var buttons=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,4,20,4)};
  appSettings.Text="Настройки";appSettings.AutoSize=true;appSettings.Click+=async(s,e)=>await SafeUiAction(OpenAppSettings);buttons.Controls.Add(appSettings);
  var help=new Button{Text="Справка",AutoSize=true};help.Click+=(s,e)=>OpenHelp();buttons.Controls.Add(help);header.Controls.Add(buttons,0,0);header.SetRowSpan(buttons,3);
  string[] names={"Установленная платформа 1С","Локальный Git","Параметры подключения к GitHub"};Label[] icons={platformIndicator,gitIndicator,githubIndicator};
  for(int i=0;i<3;i++){icons[i].AutoSize=true;icons[i].Margin=new Padding(3,0,3,0);icons[i].Font=new Font("Segoe UI Symbol",12,FontStyle.Bold);header.Controls.Add(icons[i],1,i);header.Controls.Add(new Label{Text=names[i],AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(3,0,3,0)},2,i);}
  SetIndicator(platformIndicator,0,"Платформа 1С не выбрана.");SetIndicator(gitIndicator,1,"Git не настроен.");SetIndicator(githubIndicator,1,"Параметры GitHub не заполнены.");return header;
 }
 void SetIndicator(Label icon,int state,string text){icon.Text=state==2?"✓":state==1?"●":"✕";icon.ForeColor=state==2?Color.Green:state==1?Color.RoyalBlue:Color.Red;indicatorsTip.SetToolTip(icon,text);}
 void BuildBatchPanel(){
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(3)};layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));batchPanel.Controls.Add(layout);
  layout.Controls.Add(new Label{Text="Отметьте базы для обработки. Выгрузка → фильтр размера → Git → GitHub выполняются последовательно для каждой базы.",AutoSize=true,MaximumSize=new Size(900,0)},0,0);
  batchGrid.Dock=DockStyle.Fill;batchGrid.AllowUserToAddRows=false;batchGrid.AllowUserToDeleteRows=false;batchGrid.RowHeadersVisible=false;batchGrid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;batchGrid.MultiSelect=false;batchGrid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;batchGrid.BackgroundColor=SystemColors.Window;batchGrid.ShowCellToolTips=true;
  batchGrid.Columns.Add(new DataGridViewCheckBoxColumn{Name="Enabled",HeaderText="Выбор",FillWeight=35});foreach(var column in new[]{"База","Подключение","Каталог","Режим","GitHub","Последнее изменение","Статус","Пояснение"})batchGrid.Columns.Add(new DataGridViewTextBoxColumn{Name=column,HeaderText=column,ReadOnly=true});
  batchGrid.Columns["Последнее изменение"].ToolTipText="Дата последнего Git-коммита, затрагивающего каталог. Это не точная дата выгрузки файлов.";
  batchGrid.Columns["Последнее изменение"].MinimumWidth=125;batchGrid.Columns["Последнее изменение"].FillWeight=135;batchGrid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;
  batchGrid.Columns["Пояснение"].FillWeight=150;batchGrid.Columns["Режим"].FillWeight=75;batchGrid.Columns["Статус"].FillWeight=70;
  batchGrid.CurrentCellDirtyStateChanged+=(s,e)=>{if(batchGrid.IsCurrentCellDirty)batchGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
  batchGrid.CellValueChanged+=(s,e)=>{if(e.RowIndex>=0&&e.ColumnIndex==0&&batchGrid.Rows[e.RowIndex].Tag is BatchBase){((BatchBase)batchGrid.Rows[e.RowIndex].Tag).Enabled=Convert.ToBoolean(batchGrid.Rows[e.RowIndex].Cells[0].Value);UpdateRunAvailability();}};
  batchGrid.CellBeginEdit+=(s,e)=>{if(batchRunning||initializing)e.Cancel=true;};
  batchGrid.CellDoubleClick+=(s,e)=>{if(e.RowIndex<0)return;string column=e.ColumnIndex>=0?batchGrid.Columns[e.ColumnIndex].Name:"";if(batchRunning||initializing||column=="Статус"||column=="Пояснение")ShowBatchDetails(batchGrid.Rows[e.RowIndex]);else EditBatchBase();};
  batchGrid.CellToolTipTextNeeded+=(s,e)=>{if(e.RowIndex>=0&&e.ColumnIndex>=0){var cell=batchGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];string column=batchGrid.Columns[e.ColumnIndex].Name;e.ToolTipText=column=="Последнее изменение"?cell.ToolTipText:(column=="Статус"||column=="Пояснение"?BatchDetailsText(batchGrid.Rows[e.RowIndex]):Convert.ToString(cell.Value));}};
  layout.Controls.Add(batchGrid,0,1);var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};
  foreach(var label in new[]{"Добавить","Редактировать","Удалить","Копировать"}){var button=new Button{Text=label,AutoSize=true};batchButtons.Add(button);buttons.Controls.Add(button);}
  batchButtons[0].Click+=(s,e)=>AddBatchBase();batchButtons[1].Click+=(s,e)=>EditBatchBase();batchButtons[2].Click+=(s,e)=>DeleteBatchBase();batchButtons[3].Click+=(s,e)=>CopyBatchBase();scenarioButton.Click+=(s,e)=>CreateAutomaticScenario();buttons.Controls.Add(scenarioButton);layout.Controls.Add(buttons,0,2);
 }
 void ReloadBatchRows(){batchGrid.Rows.Clear();foreach(var b in batchBases){int index=batchGrid.Rows.Add(b.Enabled,b.Name,b.FileMode?b.FilePath:b.Server+"\\"+b.Database,b.Output,b.Incremental?"Инкрементальная":"Полная",b.GithubRepo,"—","Ожидает","");batchGrid.Rows[index].Tag=b;}UpdateRunAvailability();var pending=RefreshLastChanges();}
 void AddBatchBase(){if(batchBases.Count>=1000){MessageBox.Show(this,"Допускается не более 1000 записей.");return;}using(var d=new BatchBaseForm(new BatchBase(),bases.Items.Cast<object>().ToArray(),gitReady,githubReady,RefreshGithubRepositoryCache,githubRepositoryCache))if(d.ShowDialog(this)==DialogResult.OK){batchBases.Add(d.Result);ReloadBatchRows();SaveUserSettings(true);}}
 void EditBatchBase(){if(batchGrid.SelectedRows.Count==0)return;var b=batchGrid.SelectedRows[0].Tag as BatchBase;if(b==null)return;using(var d=new BatchBaseForm(b,bases.Items.Cast<object>().ToArray(),gitReady,githubReady,RefreshGithubRepositoryCache,githubRepositoryCache))if(d.ShowDialog(this)==DialogResult.OK){batchBases[batchBases.IndexOf(b)]=d.Result;ReloadBatchRows();SaveUserSettings(true);}}
 void DeleteBatchBase(){if(batchGrid.SelectedRows.Count==0)return;var b=batchGrid.SelectedRows[0].Tag as BatchBase;if(b!=null&&MessageBox.Show(this,"Удалить запись «"+b.Name+"»? База и выгруженные файлы останутся на месте.","Удаление записи",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes){batchBases.Remove(b);ReloadBatchRows();SaveUserSettings(true);}}
 BatchBase MakeCopy(BatchBase source,bool incremental){var copy=source.Copy();copy.Incremental=incremental||source.Incremental;copy.Enabled=incremental;string prefix=source.Name+(incremental?" — инкрементная":" — копия"),name=prefix;int number=2;while(batchBases.Any(b=>String.Equals(b.Name,name,StringComparison.OrdinalIgnoreCase)))name=prefix+" ("+(number++)+")";copy.Name=name;return copy;}
 void CopyBatchBase(){
  if(batchBases.Count>=1000){MessageBox.Show(this,"Допускается не более 1000 записей.");return;}
  if(batchGrid.SelectedRows.Count==0)return;var original=batchGrid.SelectedRows[0].Tag as BatchBase;if(original==null)return;
  var answer=MessageBox.Show(this,"Создать копию для инкрементной выгрузки?\r\n\r\nДа — включить инкрементный режим и снять флажок исходной записи после сохранения.\r\nНет — обычная копия без отметки для запуска.","Копирование записи",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);if(answer==DialogResult.Cancel)return;
  bool incremental=answer==DialogResult.Yes;
  using(var d=new BatchBaseForm(MakeCopy(original,incremental),bases.Items.Cast<object>().ToArray(),gitReady,githubReady,RefreshGithubRepositoryCache,githubRepositoryCache))if(d.ShowDialog(this)==DialogResult.OK){if(incremental)original.Enabled=false;batchBases.Insert(batchBases.IndexOf(original)+1,d.Result);ReloadBatchRows();SaveUserSettings(true);}
 }
 async Task RefreshLastChanges(){
  if(headless||batchRunning)return;
  int version=++dateVersion;DateTime budget=DateTime.UtcNow.AddSeconds(60);string exe=gitPath.Text;var items=batchBases.ToArray();
  try{foreach(var b in items){
   if(IsDisposed||version!=dateVersion||DateTime.UtcNow>budget)return;
   string shown="—",hint="Дата последнего Git-коммита по каталогу. Без Git получить её нельзя.";
   if(gitReady&&Directory.Exists(b.Output)){
    var result=await GitDiagnosticCommand(exe,"-C "+Quote(b.Output)+" log -1 --format=%cI -- .");
    DateTimeOffset time;if(result.Item1==0&&DateTimeOffset.TryParse(result.Item2,out time)){shown=time.ToLocalTime().ToString("dd.MM.yyyy HH:mm");hint="Дата коммита Git: "+result.Item2+". Не является точной датой выгрузки.";}
    else hint=result.Item1==0?"Нет коммитов по этому каталогу.":Redact(result.Item2);
   }
   if(IsDisposed||version!=dateVersion)return;foreach(DataGridViewRow row in batchGrid.Rows)if(row.Tag==b){row.Cells["Последнее изменение"].Value=shown;row.Cells["Последнее изменение"].ToolTipText=hint;break;}
  }}catch{}
 }
 UserSettings CaptureSettings(){return new UserSettings{Platform=platform.Text,GitPath=gitPath.Text,GithubUser=githubUser.Text,GithubToken=githubToken.Text,IgnoreLargeFiles=ignoreLargeFiles.Checked,MaxFileMegabytes=maxFileMegabytes.Value,BatchBases=batchBases.Select(b=>b.Copy()).ToList(),GithubRepositories=githubRepositoryCache};}
 void CreateAutomaticScenario(){
  batchGrid.EndEdit();if(!batchBases.Any(b=>b.Enabled)){MessageBox.Show(this,"Отметьте базы, которые должен выполнять сценарий.");return;}
  using(var dialog=new SaveFileDialog{Filter="Сценарий PowerShell (*.ps1)|*.ps1",FileName="Export-1C.ps1",DefaultExt="ps1",AddExtension=true}){
   if(dialog.ShowDialog(this)!=DialogResult.OK)return;
   try{string config=AutomaticScenario.Write(dialog.FileName,Application.ExecutablePath,CaptureSettings());MessageBox.Show(this,"Создан сценарий:\r\n"+dialog.FileName+"\r\n\r\nНастройки: "+config+"\r\nЗапускайте под тем же пользователем Windows на этом компьютере. Для другого сервера создайте сценарий на нём.\r\nИнструкция по планировщику находится в справке.","Автоматический сценарий",MessageBoxButtons.OK,MessageBoxIcon.Information);}
   catch(Exception ex){MessageBox.Show(this,Redact(ex.Message),"Сценарий не создан",MessageBoxButtons.OK,MessageBoxIcon.Error);}
  }
 }
 public async Task<int> RunAutomatic(string settings,string resultPath){
  headless=true;string error=null;int code=2;
  try{
   settingsPath=Path.GetFullPath(settings);
   if(!String.IsNullOrWhiteSpace(resultPath)){resultPath=Path.GetFullPath(resultPath);if(!resultPath.EndsWith(".json",StringComparison.OrdinalIgnoreCase)||resultPath.Equals(settingsPath,StringComparison.OrdinalIgnoreCase)||resultPath.Equals(Application.ExecutablePath,StringComparison.OrdinalIgnoreCase))throw new IOException("Отчёт должен быть отдельным файлом .json.");ExecutionSafety.AtomicWrite(resultPath,ExecutionSafety.Utf8WithBom(new JavaScriptSerializer().Serialize(new{State="Running",Started=DateTimeOffset.UtcNow.ToString("o"),ExitCode=2})));}
   if(!File.Exists(settingsPath))throw new FileNotFoundException("Не найден файл настроек сценария.");
   LoadDefaults();if(settingsLoadFailed)throw new InvalidDataException("Настройки не расшифрованы. Запустите сценарий под пользователем, который его создал, на том же компьютере.");
   await RefreshConnections();await RunBatchAsync(false);code=lastBatchFailures==0&&!batchStopRequested?0:1;
  }catch(Exception ex){error=Redact(ex.Message);try{RecordEvent("Ошибка автоматического запуска: "+error);}catch{}}
  if(!String.IsNullOrWhiteSpace(resultPath)){
   try{var rows=new List<object>();foreach(DataGridViewRow row in batchGrid.Rows){bool selected=Convert.ToBoolean(row.Cells[0].Value);rows.Add(new{Base=Convert.ToString(row.Cells["База"].Value),Selected=selected,Status=selected?Convert.ToString(row.Cells["Статус"].Value):"Не выбрана",Reason=Convert.ToString(row.Cells["Пояснение"].Value)});}
    if(!resultPath.EndsWith(".json",StringComparison.OrdinalIgnoreCase)||resultPath.Equals(settingsPath,StringComparison.OrdinalIgnoreCase))throw new IOException("Недопустимый путь отчёта.");ExecutionSafety.AtomicWrite(resultPath,ExecutionSafety.Utf8WithBom(new JavaScriptSerializer().Serialize(new{State="Completed",Completed=DateTimeOffset.UtcNow.ToString("o"),ExitCode=code,Error=error,Bases=rows})));
   }catch{code=2;}
  }return code;
 }
 void BatchInputError(string message){if(headless)throw new InvalidOperationException(message);MessageBox.Show(this,message);}
 void SelectBatchProfile(BatchBase b){activeBatchBase=b;activeBaseName=b.Name;fileMode.Checked=b.FileMode;serverMode.Checked=!b.FileMode;filePath.Text=b.FilePath;server.Text=b.Server;database.Text=b.Database;dbUser.Text=b.DbUser;dbPassword.Text=b.DbPassword;repoUser.Text=b.RepoUser;repoPassword.Text=b.RepoPassword;output.Text=b.Output;githubRepo.Text=b.GithubRepo;fullMode.Checked=!b.Incremental;incrementalMode.Checked=b.Incremental;commitGit.Checked=b.Commit;pushGit.Checked=b.Push;commitMessage.Text=b.MessageFor(b.Incremental);UpdatePreview();}
 void SetBatchResult(BatchBase b,string result,string reason,Color color){var summary=SummaryFor(b);summary.Current=result.Contains("Успех")?"Обработка завершена":result.Contains("Ошибка")?"Ошибка на этапе: "+summary.Current:result=="Пропущена"?"Обработка пропущена":result.Contains("Выполняется")?"Подготовка к выгрузке":"Ожидает запуска";foreach(DataGridViewRow row in batchGrid.Rows)if(row.Tag==b){row.Cells["Статус"].Value=result;row.Cells["Пояснение"].Value=reason;row.Cells["Статус"].Style.ForeColor=color;row.Cells["Пояснение"].ToolTipText=reason;break;}}
 static string[] PrepareDestinations(List<BatchBase> selected,CancellationToken cancellation){
  var destinations=new List<string>();string application=ExecutionSafety.CanonicalDirectory(AppDomain.CurrentDomain.BaseDirectory);
  foreach(var b in selected){cancellation.ThrowIfCancellationRequested();string directory;
   try{directory=ExecutionSafety.DirectoryPath(b.Output);if(RuntimeSafety.ContainsPath(directory,application))throw new IOException("Каталог выгрузки не должен содержать каталог утилиты.");if(b.FileMode){if(String.IsNullOrWhiteSpace(b.FilePath)||!Path.IsPathRooted(b.FilePath)||b.FilePath.IndexOfAny(new[]{'\r','\n','\0','"'})>=0)throw new IOException("Укажите абсолютный путь файловой базы.");string source=ExecutionSafety.CanonicalDirectory(b.FilePath);if(RuntimeSafety.ContainsPath(directory,source)||RuntimeSafety.ContainsPath(source,directory))throw new IOException("Каталоги файловой базы и выгрузки не должны пересекаться.");}}
   catch(Exception ex){throw new IOException("База «"+b.Name+"»: "+ex.Message,ex);}
   if(destinations.Any(d=>RuntimeSafety.ContainsPath(d,directory)||RuntimeSafety.ContainsPath(directory,d)))throw new IOException("Каталоги выбранных баз совпадают или вложены друг в друга. Укажите отдельные каталоги.");destinations.Add(directory);
  }return destinations.ToArray();
 }
 async Task RunBatchAsync(bool confirm=true){
  if(batchRunning||operationBusy||initializing)return;batchGrid.EndEdit();var selected=batchBases.Where(b=>b.Enabled).ToList();if(selected.Count==0){BatchInputError("Отметьте хотя бы одну базу.");return;}
  if(confirm&&MessageBox.Show(this,"Начать выгрузку? Убедитесь, что конфигураторы всех баз будут доступны для запуска!","Пакетная выгрузка",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
  initializing=true;UpdateRunAvailability();batchGrid.ReadOnly=true;foreach(var button in batchButtons)button.Enabled=false;scenarioButton.Enabled=appSettings.Enabled=saveSettings.Enabled=false;status.Text="Проверка каталогов выгрузки…";
  try{var token=lifetimeCancellation.Token;string[] destinations=await Task.Run(()=>PrepareDestinations(selected,token));if(IsDisposed)return;for(int i=0;i<selected.Count;i++){selected[i].Output=destinations[i];foreach(DataGridViewRow row in batchGrid.Rows)if(row.Tag==selected[i])row.Cells["Каталог"].Value=destinations[i];}}
  catch(Exception ex){if(IsDisposed)return;BatchInputError(Redact(ex.Message));return;}
  finally{initializing=false;if(!IsDisposed){batchGrid.ReadOnly=false;foreach(var button in batchButtons)button.Enabled=true;scenarioButton.Enabled=appSettings.Enabled=saveSettings.Enabled=true;UpdateRunAvailability();}}
  if(IsDisposed)return;
  SaveUserSettings(false);batchCancellation=new CancellationTokenSource();batchRunning=true;dateVersion++;batchStopRequested=false;cancel.Enabled=true;completedBatchLogs.Clear();operationSummaries.Clear();preview.Clear();logTabs.SelectedIndex=1;batchGrid.ReadOnly=true;batchIgnoreLarge.Enabled=batchMaxSize.Enabled=false;scenarioButton.Enabled=false;foreach(var button in batchButtons)button.Enabled=false;appSettings.Enabled=saveSettings.Enabled=false;UpdateRunAvailability();int success=0,failed=0;
  foreach(var b in selected)SetBatchResult(b,"Ожидает","",SystemColors.ControlText);
  try{
   foreach(var b in selected){
    if(batchStopRequested)break;logBox.Clear();diagnostic=platformLog="";SetBatchResult(b,"● Выполняется","",Color.RoyalBlue);SelectBatchProfile(b);
    OutputLease lease=null;try{
    string preparation=ValidateInput();if(b.Commit&&!gitReady)preparation="Git не настроен или недоступен.";if(b.Push&&(!githubReady||!gitReady))preparation="Подключение к GitHub не настроено или не проверено.";
    if(preparation==null){CurrentOperation("Проверка каталога и получение блокировки");var token=batchCancellation.Token;lease=await Task.Run(()=>OutputLease.AcquireCore(b.Output,token));}
    if(preparation==null&&b.Commit){CurrentOperation("Проверка и подготовка локального Git-репозитория");try{Directory.CreateDirectory(b.Output);var check=await DiagnoseGitDirectory(gitPath.Text,b.Output);if(!check.Item1){if(!check.Item2)preparation=check.Item3;else{var init=await RunGitAsync("init --initial-branch=main");if(init.Item1!=0)preparation=init.Item2;else{var config=await RunGitAsync("config --local onecexporter.repositoryName "+Quote(b.Name));if(config.Item1!=0)preparation=config.Item2;}}}if(preparation==null)preparation=await GitPreflight();}catch(Exception ex){preparation=Redact(ex.Message);}}
    if(preparation==null){await RunExportAsync();if(lastExportSuccess){success++;SetBatchResult(b,"✓ Успех","Все выбранные этапы завершены.",Color.Green);}else{failed++;SetBatchResult(b,"✕ Ошибка",lastExportError??"Операция не завершена.",Color.Red);}}
    else{failed++;SetBatchResult(b,"✕ Ошибка",preparation,Color.Red);RecordEvent("Ошибка подготовки: "+preparation);}
    }catch(Exception ex){failed++;SetBatchResult(b,"✕ Ошибка",Redact(ex.Message),Color.Red);RecordEvent("Ошибка обработки базы: "+ex.Message);}finally{if(lease!=null)lease.Dispose();}
    completedBatchLogs.Add(RuntimeSafety.Tail("=== "+b.Name+" ===\r\n"+(logBox.Text.Length==0?Convert.ToString(batchGrid.Rows.Cast<DataGridViewRow>().First(r=>r.Tag==b).Cells["Пояснение"].Value):logBox.Text)));while(completedBatchLogs.Count>1&&completedBatchLogs.Sum(x=>x.Length)>RuntimeSafety.TextLimit)completedBatchLogs.RemoveAt(0);logBox.Text=RuntimeSafety.Tail(String.Join("\r\n\r\n",completedBatchLogs));
   }
   if(batchStopRequested)foreach(DataGridViewRow row in batchGrid.Rows)if(row.Tag is BatchBase&&((BatchBase)row.Tag).Enabled&&Convert.ToString(row.Cells["Статус"].Value)=="Ожидает")SetBatchResult((BatchBase)row.Tag,"Пропущена","Пакет остановлен пользователем.",Color.Gray);
  }finally{lastBatchFailures=failed;activeBatchBase=null;if(batchCancellation!=null){batchCancellation.Dispose();batchCancellation=null;}batchRunning=false;batchGrid.ReadOnly=false;batchIgnoreLarge.Enabled=true;batchMaxSize.Enabled=batchIgnoreLarge.Checked;scenarioButton.Enabled=true;foreach(var button in batchButtons)button.Enabled=true;appSettings.Enabled=saveSettings.Enabled=true;cancel.Enabled=false;UpdateRunAvailability();status.Text="Пакет: успешно "+success+", ошибок "+failed+(batchStopRequested?", остановлен":"");SaveUserSettings(false);var dates=RefreshLastChanges();}
 }
 void ReportExport(string message,string title,MessageBoxIcon icon){if(icon==MessageBoxIcon.Error||icon==MessageBoxIcon.Warning){lastExportError=message;lastExportSuccess=false;}if(!batchRunning)MessageBox.Show(this,message,title,MessageBoxButtons.OK,icon);else RecordEvent(title+": "+message);}
 OperationSummary SummaryFor(BatchBase b){OperationSummary summary;if(!operationSummaries.TryGetValue(b,out summary)){summary=new OperationSummary();operationSummaries.Add(b,summary);}return summary;}
 void CurrentOperation(string text){if(activeBatchBase==null)return;SummaryFor(activeBatchBase).Current=text;foreach(DataGridViewRow row in batchGrid.Rows)if(row.Tag==activeBatchBase){row.Cells["Пояснение"].Value=text;break;}}
 void CompletedOperation(string text){if(activeBatchBase==null)return;var summary=SummaryFor(activeBatchBase);if(!summary.Done.Contains(text))summary.Done.Add(text);}
 string BatchDetailsText(DataGridViewRow row){
  var b=row==null?null:row.Tag as BatchBase;if(b==null)return "Запись базы больше недоступна.";var summary=SummaryFor(b);var text=new StringBuilder("База: "+b.Name+"\r\nСтатус: "+Convert.ToString(row.Cells["Статус"].Value)+"\r\n\r\nУже выполнено:\r\n");
  if(summary.Done.Count==0)text.AppendLine("Завершённых этапов пока нет.");else foreach(string done in summary.Done)text.AppendLine("✓ "+done);
  text.Append("\r\nТекущий этап: ").AppendLine(summary.Current);string reason=Convert.ToString(row.Cells["Пояснение"].Value);if(!String.IsNullOrWhiteSpace(reason)&&reason!=summary.Current)text.Append("\r\nПояснение:\r\n").Append(reason);return text.ToString();
 }
 static void CheckExportFiles(string folder){CheckExportFilesCore(folder,CancellationToken.None);}
 static void CheckExportFilesCore(string folder,CancellationToken cancellation){foreach(string name in new[]{"Configuration.xml","ConfigDumpInfo.xml"}){string path=Path.Combine(folder,name);if(!File.Exists(path))throw new IOException("После выгрузки не найден "+name+". Проверьте журнал 1С.");using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=128*1024*1024})){if(reader.MoveToContent()!=XmlNodeType.Element)throw new InvalidDataException("Файл "+name+" не содержит XML-документа.");while(reader.Read()){cancellation.ThrowIfCancellationRequested();}}}}
 void ShowBatchDetails(DataGridViewRow row){
  if(row==null||!(row.Tag is BatchBase))return;var b=(BatchBase)row.Tag;
  string text=BatchDetailsText(row);
  using(var dialog=new Form{Text="Результат обработки — "+b.Name,StartPosition=FormStartPosition.CenterParent,Size=new Size(820,430),MinimumSize=new Size(500,260),ShowInTaskbar=false,Font=Font}){
   var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(10),ColumnCount=1,RowCount=2};layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));dialog.Controls.Add(layout);
   var detailText=new TextBox{Text=text,Multiline=true,ReadOnly=true,WordWrap=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BackColor=SystemColors.Window};layout.Controls.Add(detailText,0,0);
   var close=new Button{Text="Закрыть",AutoSize=true,Anchor=AnchorStyles.Right,DialogResult=DialogResult.Cancel};layout.Controls.Add(close,0,1);dialog.CancelButton=close;using(var refresh=new Timer{Interval=400}){refresh.Tick+=(s,e)=>{string updated=BatchDetailsText(row);if(updated==detailText.Text)return;int start=detailText.SelectionStart,length=detailText.SelectionLength;detailText.Text=updated;detailText.Select(Math.Min(start,updated.Length),Math.Min(length,Math.Max(0,updated.Length-start)));};refresh.Start();try{dialog.ShowDialog(this);}finally{refresh.Stop();}}
  }
 }
 void OpenHelp(){if(helpWindow==null||helpWindow.IsDisposed){helpWindow=new HelpForm();helpWindow.Show(this);}else{if(helpWindow.WindowState==FormWindowState.Minimized)helpWindow.WindowState=FormWindowState.Normal;helpWindow.BringToFront();helpWindow.Activate();}}
 protected override bool ProcessCmdKey(ref Message msg,Keys keyData){if(keyData==Keys.F1){OpenHelp();return true;}return base.ProcessCmdKey(ref msg,keyData);}
 async Task OpenAppSettings(){
  using(var d=new AppSettingsForm(platform.Text,platform.Items.Cast<string>().ToArray(),gitPath.Text,githubUser.Text,githubToken.Text)){
   if(d.ShowDialog(this)!=DialogResult.OK)return;
   if(githubUser.Text!=d.GithubUser.Text.Trim()||githubToken.Text!=d.GithubToken.Text.Trim())githubRepositoryCache=new string[0];
   platform.Text=d.Platform.Text.Trim();gitPath.Text=d.Git.Text.Trim();githubUser.Text=d.GithubUser.Text.Trim();githubToken.Text=d.GithubToken.Text.Trim();
  }
  initializing=true;appSettings.Enabled=false;UpdateRunAvailability();try{await RefreshConnections();if(IsDisposed)return;SaveUserSettings(true);await TryRefreshRepositoryCache();}finally{initializing=false;if(!IsDisposed){appSettings.Enabled=true;UpdateRunAvailability();}}if(!IsDisposed)UpdatePreview();
 }
 async Task RefreshConnections(){
  int version=++connectionVersion;bool wanted=requestedPush||pushGit.Checked,wantedCommit=commitGit.Checked;requestedPush=false;githubReady=gitReady=false;pushGit.Checked=false;pushGit.Enabled=commitGit.Enabled=false;commitGit.Checked=false;
  string exe=platform.Text.Trim();bool platformOk=false;try{platformOk=File.Exists(exe)&&Path.GetFileName(exe).Equals("1cv8.exe",StringComparison.OrdinalIgnoreCase);}catch{}
  SetIndicator(platformIndicator,platformOk?2:0,platformOk?"Найдена платформа 1С: "+exe:"Платформа не выбрана или файл 1cv8.exe не найден по указанному пути.");
  string git=gitPath.Text.Trim();
  if(String.IsNullOrWhiteSpace(git))SetIndicator(gitIndicator,1,"Путь к Git не указан. Для выгрузки он необязателен.");
  else{SetIndicator(gitIndicator,1,"Проверяется доступность Git.");var result=await GitDiagnosticCommand(git,"--version");if(IsDisposed||version!=connectionVersion)return;gitReady=result.Item1==0&&result.Item2.StartsWith("git version ",StringComparison.OrdinalIgnoreCase);SetIndicator(gitIndicator,gitReady?2:0,gitReady?"Git доступен: "+result.Item2:Redact(result.Item2));}
  commitGit.Enabled=gitReady;if(gitReady&&wantedCommit)commitGit.Checked=true;
  if(!headless){var dates=RefreshLastChanges();}
  string user=githubUser.Text.Trim(),token=githubToken.Text.Trim();
  if(headless&&!batchBases.Any(b=>b.Enabled&&b.Push))return;
  if(String.IsNullOrWhiteSpace(user)||String.IsNullOrWhiteSpace(token)){SetIndicator(githubIndicator,1,"Укажите пользователя и токен GitHub в настройках.");return;}
  SetIndicator(githubIndicator,1,"Проверяется подключение к GitHub.");
  try{
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token))
   using(var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(10)}){
    deadline.CancelAfter(TimeSpan.FromSeconds(10));
    client.DefaultRequestHeaders.UserAgent.ParseAdd("OneCConfigExporter/2.5");client.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
    {
     var info=new JavaScriptSerializer().DeserializeObject(await GithubResponse(client,"https://api.github.com/user",deadline.Token)) as Dictionary<string,object>;
     if(info==null||!info.ContainsKey("login")||!String.Equals(Convert.ToString(info["login"]),user,StringComparison.OrdinalIgnoreCase))throw new Exception("Токен принадлежит другому пользователю GitHub.");
    }
   }
   if(IsDisposed||version!=connectionVersion)return;githubReady=true;SetIndicator(githubIndicator,2,"Пользователь и токен проверены. Права записи в репозиторий проверяются при отправке.");pushGit.Enabled=gitReady;if(wanted&&gitReady)pushGit.Checked=true;
  }catch(Exception ex){if(IsDisposed||version!=connectionVersion)return;SetIndicator(githubIndicator,0,ex is TaskCanceledException?"Не удалось подключиться к GitHub за 10 секунд.":Redact(ex.Message));}
 }
 GroupBox BuildCommonExportSettings(){
  var box=Group("Выгрузка");var grid=Grid(2);grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
  var limits=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,WrapContents=false};limits.Controls.Add(batchIgnoreLarge);limits.Controls.Add(batchMaxSize);limits.Controls.Add(new Label{Text="МБ",AutoSize=true,Margin=new Padding(3,6,3,3)});grid.Controls.Add(limits,0,0);grid.SetColumnSpan(limits,2);
  ignoreLargeFiles.Checked=true;batchIgnoreLarge.Checked=true;batchMaxSize.Value=maxFileMegabytes.Value;
  batchIgnoreLarge.CheckedChanged+=(s,e)=>{ignoreLargeFiles.Checked=batchIgnoreLarge.Checked;batchMaxSize.Enabled=batchIgnoreLarge.Checked;};ignoreLargeFiles.CheckedChanged+=(s,e)=>batchIgnoreLarge.Checked=ignoreLargeFiles.Checked;batchMaxSize.ValueChanged+=(s,e)=>maxFileMegabytes.Value=batchMaxSize.Value;maxFileMegabytes.ValueChanged+=(s,e)=>batchMaxSize.Value=maxFileMegabytes.Value;
  indicatorsTip.SetToolTip(batchIgnoreLarge,"После экспорта крупные созданные или изменённые файлы удаляются. Такая выгрузка неполная.");box.Controls.Add(grid);return box;
 }
 GroupBox BuildLogGroup() {
  var box=Group("Команда и журнал");box.AutoSize=false;box.Dock=DockStyle.Fill;var tabs=new TabControl{Dock=DockStyle.Fill};logTabs=tabs;var ct=new TabPage("Команды запуска");preview.Text="Здесь появятся команды запуска конфигуратора для всех баз текущей очереди. Пароли скрыты.\r\nИстория очищается при запуске новой очереди.";preview.Multiline=true;preview.ReadOnly=true;preview.Dock=DockStyle.Fill;preview.ScrollBars=ScrollBars.Vertical;preview.WordWrap=true;preview.Font=new Font("Consolas",9);ct.Controls.Add(preview);
  var lt=new TabPage("Журнал выполнения");logBox.Multiline=true;logBox.ReadOnly=true;logBox.Dock=DockStyle.Fill;logBox.ScrollBars=ScrollBars.Both;logBox.WordWrap=false;logBox.Font=new Font("Consolas",9);lt.Controls.Add(logBox);tabs.TabPages.Add(ct);tabs.TabPages.Add(lt);var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.Controls.Add(tabs,0,0);var details=new Button{Text="Открыть подробный журнал Git",AutoSize=true,Anchor=AnchorStyles.Left};details.Click+=(s,e)=>OpenGitDetails();layout.Controls.Add(details,0,1);box.Controls.Add(layout);return box;
 }
 static async Task<string> GithubResponse(HttpClient client,string url,CancellationToken cancellation){
  using(var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,cancellation)){
   if(!response.IsSuccessStatusCode)throw new IOException("GitHub: HTTP "+(int)response.StatusCode+". Проверьте токен, доступ к репозиториям и подключение.");
   if(response.Content.Headers.ContentLength>2*1024*1024)throw new IOException("Ответ GitHub слишком велик.");
   using(var stream=await response.Content.ReadAsStreamAsync())using(var memory=new MemoryStream()){
    var buffer=new byte[8192];int read;while((read=await stream.ReadAsync(buffer,0,buffer.Length,cancellation))>0){if(memory.Length+read>2*1024*1024)throw new IOException("Ответ GitHub слишком велик.");memory.Write(buffer,0,read);}
    return new UTF8Encoding(false,true).GetString(memory.ToArray());
   }
  }
 }
 async Task<string[]> FetchGithubRepositoryNames(){
  string user=githubUser.Text.Trim(),token=githubToken.Text.Trim();int version=connectionVersion;
  if(String.IsNullOrWhiteSpace(user)||String.IsNullOrWhiteSpace(token))throw new IOException("Заполните параметры соединения с GitHub в настройках.");
  ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
  using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token))
  using(var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(15)}){
   deadline.CancelAfter(TimeSpan.FromSeconds(60));client.DefaultRequestHeaders.UserAgent.ParseAdd("OneCConfigExporter/2.5");client.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
   var names=new List<string>();for(int page=1;page<=100;page++){
    deadline.Token.ThrowIfCancellationRequested();if(IsDisposed||version!=connectionVersion)throw new OperationCanceledException("Параметры подключения изменились.");
    var rows=new JavaScriptSerializer().DeserializeObject(await GithubResponse(client,"https://api.github.com/user/repos?per_page=100&page="+page,deadline.Token)) as object[];
    if(rows==null||rows.Length>100)throw new InvalidDataException("Некорректный ответ GitHub.");
    foreach(var row in rows){var item=row as Dictionary<string,object>;if(item!=null&&item.ContainsKey("full_name")){string name=Convert.ToString(item["full_name"]);if(name.Length>256||!Regex.IsMatch(name,@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))throw new InvalidDataException("Некорректное имя репозитория в ответе GitHub.");names.Add(name);}}
    if(rows.Length<100)break;
   }return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToArray();
  }
 }
 async Task<string[]> RefreshGithubRepositoryCache(){
  string user=githubUser.Text,token=githubToken.Text,key=connectionVersion.ToString();
  if(repositoryRequest==null||repositoryRequest.IsCompleted||repositoryRequestKey!=key){repositoryRequestKey=key;repositoryRequest=FetchGithubRepositoryNames();}
  var names=await repositoryRequest;if(IsDisposed||user!=githubUser.Text||token!=githubToken.Text)throw new OperationCanceledException("Параметры GitHub изменились. Повторите обновление.");
  githubRepositoryCache=names;SaveUserSettings(false);return names;
 }
 async Task TryRefreshRepositoryCache(){
  if(String.IsNullOrWhiteSpace(githubUser.Text)||String.IsNullOrWhiteSpace(githubToken.Text))return;
  try{var names=await RefreshGithubRepositoryCache();if(IsDisposed)return;if(!batchRunning&&!initializing)status.Text="Настройки сохранены. Репозиториев GitHub: "+names.Length;}
  catch(Exception ex){if(IsDisposed)return;string message=Redact(ex.Message);if(!batchRunning)status.Text="Настройки сохранены; список GitHub не обновлён.";logBox.Text=RuntimeSafety.Tail(logBox.Text+"\r\nПолучение списка репозиториев: "+message+"\r\n");}
 }
 Control BuildFooter() {
  var p=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=4};p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
  var left=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,AutoSize=true,Dock=DockStyle.Fill};status.Text="Готово к запуску";status.AutoSize=true;status.MaximumSize=new Size(260,0);progress.Width=260;left.Controls.Add(status);left.Controls.Add(progress);p.Controls.Add(left,0,0);
  saveSettings.Text="Сохранить настройки";saveSettings.AutoSize=true;saveSettings.Anchor=AnchorStyles.Bottom;saveSettings.Click+=async(s,e)=>{saveSettings.Enabled=false;try{if(SaveUserSettings(true)){status.Text="Настройки сохранены";await TryRefreshRepositoryCache();}}finally{if(!IsDisposed)saveSettings.Enabled=!batchRunning;}};p.Controls.Add(saveSettings,1,0);
  cancel.Text="Остановить";cancel.AutoSize=true;cancel.Enabled=false;cancel.Anchor=AnchorStyles.Bottom;cancel.Click+=(s,e)=>CancelExport();p.Controls.Add(cancel,2,0);run.Text="Выполнить";run.AutoSize=true;run.Font=new Font(Font,FontStyle.Bold);run.Anchor=AnchorStyles.Bottom;run.Click+=async(s,e)=>await SafeUiAction(()=>RunBatchAsync());p.Controls.Add(run,3,0);AcceptButton=run;return p;
 }
 void LoadDefaults() { server.Text="localhost";database.Text="testtka";commitMessage.Text="Update 1C configuration";gitPath.Text=FindGit();LoadPlatforms();LoadBases();LoadUserSettings();requestedPush=pushGit.Checked;UpdatePreview();UpdateRunAvailability(); }
 void LoadUserSettings(){
  try{var x=SettingsStore.LoadTracked(settingsPath,out settingsSignature);if(x!=null){
   platform.Text=x.Platform;gitPath.Text=x.GitPath;githubUser.Text=x.GithubUser;githubToken.Text=x.GithubToken;ignoreLargeFiles.Checked=x.IgnoreLargeFiles;maxFileMegabytes.Value=Math.Max(maxFileMegabytes.Minimum,Math.Min(maxFileMegabytes.Maximum,x.MaxFileMegabytes));
   githubRepositoryCache=x.GithubRepositories??new string[0];
   batchBases=x.BatchBases??new List<BatchBase>();ReloadBatchRows();
   status.Text="Настройки восстановлены";
  }}catch(Exception){settingsLoadFailed=true;status.Text="Настройки не прочитаны — используются значения по умолчанию";logBox.Text="Файл настроек повреждён или создан другим пользователем Windows. Его можно заменить кнопкой «Сохранить»."; }
  settingsLoaded=true;
 }
 bool SaveUserSettings(bool explicitSave){
  if(headless||!settingsLoaded||batchRunning||(!explicitSave&&settingsLoadFailed))return false;
  try{bool force=false;if(SettingsStore.Signature(settingsPath)!=settingsSignature&&explicitSave){if(MessageBox.Show(this,"Файл настроек изменился в другом экземпляре. Заменить его параметрами этого окна?","Конфликт настроек",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return false;force=true;}settingsSignature=SettingsStore.SaveChecked(settingsPath,CaptureSettings(),settingsSignature,force);settingsLoadFailed=false;return true;
  }catch(Exception ex){logBox.Text=RuntimeSafety.Tail(logBox.Text+"\r\nНастройки не сохранены: "+Redact(ex.Message));if(explicitSave)MessageBox.Show(this,"Не удалось сохранить настройки рядом с программой:\r\n"+ex.Message,"Настройки",MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;}
 }
 void Stage(int completed,string text){CurrentOperation(text);progress.Style=ProgressBarStyle.Blocks;progress.Maximum=1+(commitGit.Checked?1:0)+(pushGit.Checked?1:0);progress.Value=Math.Min(completed,progress.Maximum);status.Text=text+" (этапы: "+progress.Value+"/"+progress.Maximum+")";}
 string FindGit(){foreach(var p in new[]{@"C:\Program Files\Git\cmd\git.exe",@"C:\Program Files\Git\bin\git.exe",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Git","cmd","git.exe")})if(File.Exists(p))return p;foreach(var dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(';'))try{var p=Path.Combine(dir.Trim().Trim('"'),"git.exe");if(File.Exists(p))return Path.GetFullPath(p);}catch{}return "";}
 void UpdateRunAvailability(){run.Enabled=!operationBusy&&!batchRunning&&!initializing&&batchBases.Any(b=>b.Enabled);}
 static string ConfigurationDisplayName(string folder){
  try{var path=Path.Combine(folder,"Configuration.xml");if(!File.Exists(path))return "";
   using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
   using(var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=32*1024*1024})){
    int config=-1,properties=-1;while(reader.Read()){
     if(reader.NodeType==XmlNodeType.Element&&reader.LocalName=="Configuration"&&config<0)config=reader.Depth;
     else if(config>=0&&reader.NodeType==XmlNodeType.Element&&reader.LocalName=="Properties"&&reader.Depth==config+1)properties=reader.Depth;
     else if(properties>=0&&reader.NodeType==XmlNodeType.Element&&reader.LocalName=="Name"&&reader.Depth==properties+1)return reader.ReadElementContentAsString();
     else if(reader.NodeType==XmlNodeType.EndElement&&reader.Depth==config)break;
    }
   }
  }catch{}return "";
 }
 void StartFileMonitor(string root){
  int version=++monitorVersion;
  currentFiles.Items.Clear();displayedFiles.Clear();string discarded;lock(fileEvents)while(fileEvents.TryDequeue(out discarded)){}
  exportWatcher=new FileSystemWatcher(root){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size,InternalBufferSize=32768};
  FileSystemEventHandler changed=(s,e)=>{lock(fileEvents)if(version==monitorVersion&&fileEvents.Count<2000)fileEvents.Enqueue(e.FullPath);};
  exportWatcher.Created+=changed;exportWatcher.Changed+=changed;exportWatcher.Renamed+=(s,e)=>changed(s,e);
   exportWatcher.Error+=(s,e)=>{lock(fileEvents)if(version==monitorVersion&&fileEvents.Count<2000)fileEvents.Enqueue("[Список файлов: часть уведомлений пропущена]");};
  exportWatcher.EnableRaisingEvents=true;
 }
 void FlushFileEvents(){
  string path;currentFiles.BeginUpdate();try{int count=0;while(count++<2000){lock(fileEvents){if(!fileEvents.TryDequeue(out path))break;}
   string root=Path.GetFullPath(output.Text.Trim()).TrimEnd('\\')+"\\";string name=path.StartsWith(root,StringComparison.OrdinalIgnoreCase)?path.Substring(root.Length):path;
   if(name.Equals("1c-dump.log",StringComparison.OrdinalIgnoreCase)||name.Equals(ExecutionSafety.LockName,StringComparison.OrdinalIgnoreCase)||name.StartsWith(".git\\",StringComparison.OrdinalIgnoreCase)||!displayedFiles.Add(name))continue;
   currentFiles.Items.Insert(0,name);if(currentFiles.Items.Count>200){displayedFiles.Remove(Convert.ToString(currentFiles.Items[200]));currentFiles.Items.RemoveAt(200);}
  }}finally{currentFiles.EndUpdate();}
 }
 static string GitExclusions(){return Quote(":(exclude)1c-dump.log")+" "+Quote(":(exclude)"+ExecutionSafety.LockName);}
 static async Task<bool> IsGitDirectory(string exe,string folder){var result=await GitDiagnosticCommand(exe,"-C "+Quote(folder)+" rev-parse --is-inside-work-tree");return result.Item1==0&&result.Item2=="true";}
 static async Task<Tuple<int,string>> GitDiagnosticCommand(string exe,string args){
  if(String.IsNullOrWhiteSpace(exe))return Tuple.Create(-2,"Git.exe не найден. Укажите путь к установленному Git.");
  try{using(var p=new Process{StartInfo=new ProcessStartInfo{FileName=exe,Arguments=args,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8}}){
   ExecutionSafety.CleanGitEnvironment(p.StartInfo);p.Start();var stdout=Task.Run(()=>RuntimeSafety.ReadPipe(p.StandardOutput));var stderr=Task.Run(()=>RuntimeSafety.ReadPipe(p.StandardError));var exit=Task.Run(()=>p.WaitForExit());
   if(await Task.WhenAny(exit,Task.Delay(5000)).ConfigureAwait(false)!=exit){RuntimeSafety.KillTree(p);Observe(exit);Observe(stdout);Observe(stderr);return Tuple.Create(-1,"Git не ответил за 5 секунд.");}
   await exit.ConfigureAwait(false);var pipes=Task.WhenAll(stdout,stderr);if(await Task.WhenAny(pipes,Task.Delay(2000)).ConfigureAwait(false)!=pipes){Observe(pipes);return Tuple.Create(-1,"Дочерний процесс Git не закрыл потоки.");}
   string standard=await stdout.ConfigureAwait(false),errors=await stderr.ConfigureAwait(false);return Tuple.Create(p.ExitCode,(p.ExitCode==0?standard:standard+errors).Trim());
  }}catch(System.ComponentModel.Win32Exception ex){return Tuple.Create(ex.NativeErrorCode==2||ex.NativeErrorCode==3?-2:-1,(ex.NativeErrorCode==2||ex.NativeErrorCode==3?"Git.exe не найден. Укажите путь в настройках. ":"Не удалось запустить Git: ")+ex.Message);}catch(Exception ex){return Tuple.Create(-1,"Не удалось запустить Git: "+ex.Message);}
 }
 static void Observe(Task task){task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
 async Task<string> GitPreflight(){
  var directory=await GitDiagnosticCommand(gitPath.Text,"-C "+Quote(output.Text)+" rev-parse --absolute-git-dir");if(directory.Item1!=0)return directory.Item2;
  foreach(string name in new[]{"MERGE_HEAD","CHERRY_PICK_HEAD","REVERT_HEAD","rebase-merge","rebase-apply","index.lock"}){string path=Path.Combine(directory.Item2,name);if(File.Exists(path)||Directory.Exists(path))return "Git занят или находится в незавершённой операции ("+name+"). Завершите её вручную.";}
  var staged=await GitDiagnosticCommand(gitPath.Text,"-C "+Quote(output.Text)+" diff --cached --name-only -- . "+GitExclusions());if(staged.Item1!=0)return staged.Item2;if(!String.IsNullOrWhiteSpace(staged.Item2))return "В каталоге уже есть подготовленные к коммиту изменения. Сохраните или уберите их из индекса вручную перед выгрузкой.";
  var conflicts=await GitDiagnosticCommand(gitPath.Text,"-C "+Quote(output.Text)+" diff --name-only --diff-filter=U");if(conflicts.Item1!=0)return conflicts.Item2;if(!String.IsNullOrWhiteSpace(conflicts.Item2))return "В репозитории есть неразрешённые конфликты.";return null;
 }
 static async Task<Tuple<bool,bool,string>> DiagnoseGitDirectory(string exe,string folder){
  var version=await GitDiagnosticCommand(exe,"--version");
  if(version.Item1!=0)return Tuple.Create(false,false,version.Item2);
  if(!version.Item2.StartsWith("git version ",StringComparison.OrdinalIgnoreCase))return Tuple.Create(false,false,"Выбранная программа не является Git. Выберите git.exe.");
  try{folder=Path.GetFullPath(folder);if(File.Exists(folder))return Tuple.Create(false,false,"Указан файл вместо каталога.");}
  catch(Exception ex){return Tuple.Create(false,false,"Некорректный путь к каталогу: "+ex.Message);}
  if(!Directory.Exists(folder))return Tuple.Create(false,true,"Каталог не существует. Его можно создать вместе с репозиторием.");
  var check=await GitDiagnosticCommand(exe,"-C "+Quote(folder)+" rev-parse --is-inside-work-tree");
  if(check.Item1==0&&check.Item2=="true")return Tuple.Create(true,false,"Валидный git-репозиторий");
  if(check.Item1==0&&check.Item2=="false")return Tuple.Create(false,false,"Выбран bare-репозиторий. Для выгрузки нужна рабочая копия Git.");
  bool gitData=File.Exists(Path.Combine(folder,".git"))||Directory.Exists(Path.Combine(folder,".git"));
  if(!gitData&&check.Item1>0&&check.Item2.IndexOf("not a git repository",StringComparison.OrdinalIgnoreCase)>=0)return Tuple.Create(false,true,"Каталог не является валидным git-репозиторием");
  return Tuple.Create(false,false,"Не удалось проверить репозиторий: "+check.Item2);
 }
 protected override void Dispose(bool disposing){
  bool first=disposing&&!resourcesReleased;Font[] fonts=null;
  if(first){resourcesReleased=true;connectionVersion++;dateVersion++;monitorVersion++;lifetimeCancellation.Cancel();if(batchCancellation!=null)batchCancellation.Cancel();if(exportWatcher!=null)exportWatcher.Dispose();indicatorsTip.Dispose();if(logTimer!=null)logTimer.Dispose();
   fonts=new[]{Font,platformIndicator.Font,gitIndicator.Font,githubIndicator.Font,run.Font,preview.Font,logBox.Font};
   foreach(Control c in new Control[]{bases,platform,fileMode,serverMode,fullMode,incrementalMode,filePath,server,database,output,dbUser,dbPassword,repoUser,repoPassword,commitGit,gitPath,commitMessage,pushGit,githubUser,githubToken,githubRepo,ignoreLargeFiles,maxFileMegabytes})c.Dispose();
  }base.Dispose(disposing);if(first){foreach(Font font in fonts.Distinct())font.Dispose();lifetimeCancellation.Dispose();}
 }
 async Task SafeUiAction(Func<Task> action){try{await action();}catch(OperationCanceledException){if(!IsDisposed)status.Text="Операция отменена.";}catch(Exception ex){if(IsDisposed)return;RecordEvent("Ошибка интерфейса: "+ex.Message);MessageBox.Show(this,Redact(ex.Message),"Операция не выполнена",MessageBoxButtons.OK,MessageBoxIcon.Error);}}

 void LoadPlatforms() {
  var found=new List<string>();foreach(var root in new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"1cv8"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"1cv8")})if(Directory.Exists(root))try{found.AddRange(Directory.GetDirectories(root).Select(d=>Path.Combine(d,"bin","1cv8.exe")).Where(File.Exists));}catch{}
  found=found.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(VersionKey).ToList();platform.Items.Clear();platform.Items.AddRange(found.Cast<object>().ToArray());platform.Text=found.Count>0?found[0]:"";status.Text=found.Count>0?"Обнаружено платформ 1С: "+found.Count:"Платформа 1С не найдена — выберите 1cv8.exe вручную";
 }
 Version VersionKey(string path){Version v;return Version.TryParse(new DirectoryInfo(Path.GetDirectoryName(Path.GetDirectoryName(path))).Name,out v)?v:new Version(0,0);}
 void LoadBases() {
  var items=new List<Infobase>();foreach(var path in new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"1C","1CEStart","ibases.v8i"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"1C","1CEStart","ibases.v8i")}.Distinct())items.AddRange(ParseBaseList(path));
  bases.Items.Clear();bases.Items.Add(new Infobase{Name="— вручную —",Connection=""});foreach(var item in items.GroupBy(x=>x.Name+"\0"+x.Connection,StringComparer.OrdinalIgnoreCase).Select(x=>x.First()).OrderBy(x=>x.Name))bases.Items.Add(item);bases.SelectedIndex=0;status.Text="Найдено зарегистрированных баз: "+items.Count;
 }
 static string DecodeBaseList(byte[] data){if(data.Length>=2&&data[0]==255&&data[1]==254)return Encoding.Unicode.GetString(data,2,data.Length-2);if(data.Length>=2&&data[0]==254&&data[1]==255)return Encoding.BigEndianUnicode.GetString(data,2,data.Length-2);try{return new UTF8Encoding(false,true).GetString(data).TrimStart('\uFEFF');}catch(DecoderFallbackException){return Encoding.GetEncoding(1251).GetString(data);}}
 IEnumerable<Infobase> ParseBaseList(string path) {
  var r=new List<Infobase>();if(!File.Exists(path))return r;try{string name=null,connect=null;foreach(var raw in DecodeBaseList(ExecutionSafety.ReadBounded(path,2*1024*1024)).Split('\n')){var line=raw.Trim();if(line.Length>4096||r.Count>=10000)throw new InvalidDataException("Список баз слишком велик.");if(line.StartsWith("[")&&line.EndsWith("]")){if(!String.IsNullOrWhiteSpace(name)&&!String.IsNullOrWhiteSpace(connect))r.Add(new Infobase{Name=name,Connection=connect});name=line.Substring(1,line.Length-2);connect=null;}else if(line.StartsWith("Connect=",StringComparison.OrdinalIgnoreCase))connect=line.Substring(8).Trim().Trim('"');}if(!String.IsNullOrWhiteSpace(name)&&!String.IsNullOrWhiteSpace(connect))r.Add(new Infobase{Name=name,Connection=connect});}catch{}return r;
 }
 void ApplySelectedBase() {
  var item=bases.SelectedItem as Infobase;if(item==null||String.IsNullOrEmpty(item.Connection))return;
  var fm=Regex.Match(item.Connection,"File\\s*=\\s*\"([^\"]+)\"",RegexOptions.IgnoreCase);var sm=Regex.Match(item.Connection,"Srvr\\s*=\\s*\"([^\"]+)\"",RegexOptions.IgnoreCase);var rm=Regex.Match(item.Connection,"Ref\\s*=\\s*\"([^\"]+)\"",RegexOptions.IgnoreCase);
  if(fm.Success){fileMode.Checked=true;filePath.Text=fm.Groups[1].Value;}else if(sm.Success&&rm.Success){serverMode.Checked=true;server.Text=sm.Groups[1].Value;database.Text=rm.Groups[1].Value;}
 }
 void PickFolder(TextBox target,string description){using(var d=new FolderBrowserDialog{Description=description,SelectedPath=Directory.Exists(target.Text)?target.Text:""})if(d.ShowDialog(this)==DialogResult.OK)target.Text=d.SelectedPath;}
 List<string> BuildArguments(bool mask) {
  var a=new List<string>{"DESIGNER"};if(fileMode.Checked){a.Add("/F");a.Add(filePath.Text.Trim());}else{a.Add("/S");a.Add(server.Text.Trim()+"\\"+database.Text.Trim());}
  if(!String.IsNullOrWhiteSpace(dbUser.Text)){a.Add("/N");a.Add(dbUser.Text);}if(!String.IsNullOrEmpty(dbPassword.Text)){a.Add("/P");a.Add(mask?"••••••":dbPassword.Text);}if(!String.IsNullOrWhiteSpace(repoUser.Text)){a.Add("/ConfigurationRepositoryN");a.Add(repoUser.Text);}if(!String.IsNullOrEmpty(repoPassword.Text)){a.Add("/ConfigurationRepositoryP");a.Add(mask?"••••••":repoPassword.Text);}
  a.Add("/DisableStartupMessages");a.Add("/DisableStartupDialogs");a.Add("/DumpConfigToFiles");a.Add(output.Text.Trim());if(incrementalMode.Checked)a.Add("-update");a.Add("/Out");a.Add(Path.Combine(output.Text.Trim(),"1c-dump.log"));return a;
 }
 static string Quote(string s){
  s=s??"";if(s.Length>0&&!s.Any(c=>Char.IsWhiteSpace(c)||c=='"'))return s;
  var b=new StringBuilder("\"");int slashes=0;foreach(char c in s){if(c=='\\'){slashes++;continue;}if(c=='"'){b.Append('\\',slashes*2+1);b.Append(c);}else{b.Append('\\',slashes);b.Append(c);}slashes=0;}b.Append('\\',slashes*2);b.Append('"');return b.ToString();
 }
 void UpdatePreview(){try{currentCommand=Quote(platform.Text.Trim())+" "+String.Join(" ",BuildArguments(true).Select(Quote));}catch(Exception ex){currentCommand="Некорректные параметры: "+ex.Message;}}
 void AppendLaunchCommand(){preview.Text=RuntimeSafety.Tail(preview.Text+(preview.TextLength>0?"\r\n\r\n":"")+"=== "+activeBaseName+" — "+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" ===\r\n"+(incrementalMode.Checked?"Инкрементальная выгрузка":"Полная выгрузка")+"\r\n"+currentCommand);preview.SelectionStart=preview.TextLength;preview.ScrollToCaret();}
 string ValidateInput(){if(fileMode.Checked&&!Path.IsPathRooted(filePath.Text.Trim()))return"Укажите абсолютный путь файловой базы.";if(!File.Exists(platform.Text.Trim())||!Path.GetFileName(platform.Text.Trim()).Equals("1cv8.exe",StringComparison.OrdinalIgnoreCase))return"Не найден файл платформы 1cv8.exe.";if(fileMode.Checked&&!Directory.Exists(filePath.Text.Trim()))return"Не найден каталог файловой базы.";if(serverMode.Checked&&(String.IsNullOrWhiteSpace(server.Text)||String.IsNullOrWhiteSpace(database.Text)))return"Укажите сервер и имя информационной базы.";if(String.IsNullOrWhiteSpace(output.Text))return"Укажите каталог выгрузки.";return null;}
 async Task RunExportAsync() {
  lastExportError=null;lastExportSuccess=false;
  if(operationBusy||String.IsNullOrWhiteSpace(output.Text))return;
  if(pushGit.Checked&&(!githubReady||!Regex.IsMatch(githubRepo.Text.Trim(),@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))){ReportExport("Проверьте подключение в настройках и укажите репозиторий GitHub (владелец/имя).","GitHub",MessageBoxIcon.Warning);return;}
  var error=ValidateInput();if(error!=null){ReportExport(error,"Проверьте параметры",MessageBoxIcon.Warning);return;}try{Directory.CreateDirectory(output.Text.Trim());}catch(Exception ex){ReportExport("Не удалось создать каталог выгрузки:\r\n"+ex.Message,"Ошибка",MessageBoxIcon.Error);return;}
  if(incrementalMode.Checked&&!File.Exists(Path.Combine(output.Text.Trim(),"ConfigDumpInfo.xml"))){ReportExport("В каталоге нет ConfigDumpInfo.xml от предыдущей выгрузки. Выберите «Начальная (полная)», затем используйте инкрементальный режим.","Нужна начальная выгрузка",MessageBoxIcon.Warning);return;}
  activeLog=Path.Combine(output.Text.Trim(),"1c-dump.log");try{if(File.Exists(activeLog))File.Delete(activeLog);}catch(Exception ex){ReportExport("Не удалось подготовить новый журнал 1С: "+ex.Message,"Журнал недоступен",MessageBoxIcon.Error);return;}
  var psi=new ProcessStartInfo{FileName=platform.Text.Trim(),Arguments=String.Join(" ",BuildArguments(false).Select(Quote)),UseShellExecute=false,CreateNoWindow=true,WindowStyle=headless?ProcessWindowStyle.Hidden:ProcessWindowStyle.Normal,WorkingDirectory=output.Text.Trim()};
  string exportRoot=Path.GetFullPath(output.Text.Trim());bool filter=ignoreLargeFiles.Checked;long maxBytes=(long)(maxFileMegabytes.Value*1024*1024);Dictionary<string,ExportSizeFilter.FileStamp> before=null;var cancellation=batchCancellation==null?CancellationToken.None:batchCancellation.Token;
  operationBusy=true;SaveUserSettings(false);saveSettings.Enabled=appSettings.Enabled=false;run.Enabled=false;cancel.Enabled=true;if(!batchRunning)mainTabs.SelectedIndex=1;Stage(0,"1С выполняет выгрузку…");diagnostic="База: "+activeBaseName+"\r\nЗапуск: "+DateTime.Now+"\r\nРежим: "+(incrementalMode.Checked?"инкрементальная":"полная")+"\r\nЖурнал: "+activeLog+"\r\n";platformLog="";logBox.Text=diagnostic;logTimer=new Timer{Interval=700};logTimer.Tick+=(s,e)=>{RefreshLog();FlushFileEvents();};
  try{
   if(filter){CurrentOperation("Подготовка фильтра размера");status.Text="Подготовка фильтра размера…";before=await Task.Run(()=>ExportSizeFilter.SnapshotCore(exportRoot,cancellation));}if(batchStopRequested)throw new OperationCanceledException("Пакет остановлен пользователем.");
   CurrentOperation("Конфигуратор выполняет выгрузку файлов");try{StartFileMonitor(exportRoot);}catch(Exception ex){if(exportWatcher!=null){exportWatcher.Dispose();exportWatcher=null;}RecordEvent("Наблюдение за файлами недоступно; выгрузка продолжается: "+ex.Message);}AppendLaunchCommand();activeProcess=Process.Start(psi);RecordEvent("Запуск конфигуратора под пользователем "+(String.IsNullOrWhiteSpace(dbUser.Text)?"<не указан; авторизация по умолчанию>":dbUser.Text)+", PID "+activeProcess.Id);logTimer.Start();int code=await Task.Run(()=>{activeProcess.WaitForExit();return activeProcess.ExitCode;});monitorVersion++;if(exportWatcher!=null){exportWatcher.Dispose();exportWatcher=null;}FlushFileEvents();RecordEvent("Завершение процесса конфигуратора, PID "+activeProcess.Id+", код "+code);RefreshLog();
   if(batchStopRequested)throw new OperationCanceledException("Пакет остановлен пользователем.");if(code==0){
    logTimer.Stop();CompletedOperation("Конфигурация выгружена в файлы");CurrentOperation("Проверка основных файлов выгрузки");await Task.Run(()=>CheckExportFilesCore(exportRoot,cancellation));CompletedOperation("Выгрузка проверена: основные XML-файлы найдены и читаются");
    if(filter){CurrentOperation("Проверка размера и исключение крупных файлов");status.Text="Исключение крупных файлов…";var removed=await Task.Run(()=>ExportSizeFilter.ApplyCore(exportRoot,before,maxBytes,AppDomain.CurrentDomain.BaseDirectory,cancellation));logBox.AppendText("\r\nФильтр размера: исключено файлов "+removed.Count+"\r\n"+String.Join("\r\n",removed.Take(200))+(removed.Count>200?"\r\n[Список сокращён до 200 файлов]":"")+"\r\n");CompletedOperation("Фильтр размера выполнен: исключено файлов "+removed.Count);}
    Stage(1,"Выгрузка успешно завершена");lastExportSuccess=true;cancellation.ThrowIfCancellationRequested();
    if(commitGit.Checked){var gitResult=await CommitToGitAsync();if(gitResult==null)ReportExport(pushGit.Checked?"Выгрузка завершена, Git синхронизирован с GitHub.":"Выгрузка завершена, изменения сохранены в Git (либо новых изменений нет).","Готово",MessageBoxIcon.Information);else{status.Text="Выгрузка завершена; ошибка этапа Git / GitHub";ReportExport("Выгрузка завершена. Следующий этап не выполнен:\r\n\r\n"+gitResult,"Git / GitHub",MessageBoxIcon.Warning);}}
    else ReportExport("Конфигурация выгружена в:\r\n"+output.Text.Trim(),"Готово",MessageBoxIcon.Information);
   }else{status.Text="1С завершилась с кодом "+code;ReportExport("Выгрузка завершилась с ошибкой (код "+code+"). "+(String.IsNullOrWhiteSpace(platformLog)?"Проверьте журнал 1С.":Redact(platformLog.Length>1200?platformLog.Substring(0,1200):platformLog)),"Ошибка",MessageBoxIcon.Error);}
  }
  catch(Exception ex){status.Text="Не удалось выполнить операцию";ReportExport(Redact(ex.Message),"Ошибка",MessageBoxIcon.Error);}finally{if(exportWatcher!=null){monitorVersion++;exportWatcher.Dispose();exportWatcher=null;}if(logTimer!=null){logTimer.Stop();logTimer.Dispose();logTimer=null;}if(activeProcess!=null){try{if(!activeProcess.HasExited)RuntimeSafety.KillTree(activeProcess);}catch{}activeProcess.Dispose();activeProcess=null;}operationBusy=false;progress.Style=ProgressBarStyle.Blocks;saveSettings.Enabled=appSettings.Enabled=!batchRunning;UpdateRunAvailability();cancel.Enabled=batchRunning;}
 }
 void RecordEvent(string text){string entry=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" — "+Redact(text.Replace("\r"," ").Replace("\n"," "))+"\r\n";diagnostic=RuntimeSafety.Tail(diagnostic+entry);try{RuntimeSafety.AppendEvent(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"exporter-events.log"),entry);}catch(Exception ex){diagnostic=RuntimeSafety.Tail(diagnostic+"Не удалось сохранить журнал событий: "+ex.Message+"\r\n");}logBox.Text=RuntimeSafety.Tail(logBox.Text+entry);}
 async Task<string> CommitToGitAsync(){
  if(logTimer!=null)logTimer.Stop();cancel.Enabled=batchRunning;
  if(pushGit.Checked&&!commitGit.Checked)return "Для отправки на GitHub включите создание коммита.";
  if(String.IsNullOrWhiteSpace(commitMessage.Text))return "Не задано сообщение коммита.";
  Stage(1,"Проверка изменений Git…");
  var check=await RunGitAsync("status --porcelain -- . "+GitExclusions());if(check.Item1!=0)return check.Item2;
  if(String.IsNullOrWhiteSpace(check.Item2)){CompletedOperation("Новых изменений нет — коммит не создавался");Stage(2,"Новых изменений для Git нет");return pushGit.Checked?await PushToGithub():null;}
  Stage(1,"Создание Git-коммита…");
  var add=await RunGitAsync("add -A -- . "+GitExclusions());if(add.Item1!=0)return add.Item2;
  var commit=await RunGitAsync("commit --only -m "+Quote(commitMessage.Text)+" -- . "+GitExclusions());progress.Style=ProgressBarStyle.Blocks;
  if(commit.Item1!=0)return commit.Item2;CompletedOperation("Создан коммит в локальном Git");Stage(2,"Выгрузка и Git-коммит завершены");return pushGit.Checked?await PushToGithub():null;
 }
 async Task<string> PushToGithub(){
  if(String.IsNullOrWhiteSpace(githubUser.Text)||String.IsNullOrWhiteSpace(githubToken.Text))return "Коммит сохранён локально. Укажите пользователя и токен GitHub.";
  var repo=githubRepo.Text.Trim();if(!Regex.IsMatch(repo,@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))return "Коммит сохранён локально. Укажите репозиторий в формате владелец/имя.";
  var branch=await RunGitAsync("symbolic-ref --quiet --short HEAD");if(branch.Item1!=0)return "Для отправки нужна текущая ветка Git.";
  Stage(2,"Отправка на GitHub…");var result=await RunGitAsync("push "+Quote("https://github.com/"+repo+".git")+" "+Quote("HEAD:refs/heads/"+branch.Item2),true);
  if(result.Item1!=0){status.Text="Коммит сохранён, отправка на GitHub не выполнена";return GithubPushFailure(result.Item2);}
  CompletedOperation("Изменения отправлены на GitHub");Stage(3,"Выгрузка, Git и GitHub завершены");return null;
 }
 static string GithubPushFailure(string details){
  if(details.IndexOf("GH001",StringComparison.OrdinalIgnoreCase)>=0||details.IndexOf("exceeds GitHub's file size limit",StringComparison.OrdinalIgnoreCase)>=0){
   var names=Regex.Matches(details,@"(?m)^.*?remote:\s*error:\s*File (.+?) is ([0-9.,]+) MB;.*$").Cast<Match>().Select(m=>m.Groups[1].Value+" — "+m.Groups[2].Value+" МБ").Take(5).ToArray();
   return "Коммит сохранён локально. GitHub отклонил отправку: в отправляемой истории есть файл больше 100 МБ.\r\n\r\n"+String.Join("\r\n",names)+
    "\r\n\r\nФильтр размера применяется к файлам текущей выгрузки, но не очищает старые коммиты. Удаление файла новым коммитом не удалит его из истории.\r\n\r\nНужно отдельно убрать крупные файлы из неотправленной истории либо подготовить новый репозиторий с отфильтрованными файлами. Программа не переписывает историю автоматически.\r\n\r\nОтвет Git (с ограничением объёма) сохранён на вкладке «Журнал» и в exporter-events.log.";
  }
  if(details.IndexOf("non-fast-forward",StringComparison.OrdinalIgnoreCase)>=0||details.IndexOf("fetch first",StringComparison.OrdinalIgnoreCase)>=0)
   return "Коммит сохранён локально. GitHub отклонил отправку: на сервере есть другая история или новые коммиты. Сначала нужно согласовать локальную и удалённую ветки.\r\nОтвет — в журнале.";
  return "Коммит сохранён локально. Ошибка отправки на GitHub:\r\n"+(details.Length>1500?details.Substring(0,1500)+"\r\n… подробности находятся в журнале.":details);
 }
 async Task<Tuple<int,string>> RunGitAsync(string args,bool auth=false){
  if(batchStopRequested)return Tuple.Create(-1,"Пакет остановлен пользователем.");try{using(var p=new Process{StartInfo=new ProcessStartInfo{FileName=gitPath.Text.Trim(),Arguments="-c core.quotepath=false -c http.sslVerify=true -C "+Quote(output.Text.Trim())+" "+args,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8}}){
   ExecutionSafety.CleanGitEnvironment(p.StartInfo);
   if(auth){var rewrites=await GitDiagnosticCommand(gitPath.Text,"-C "+Quote(output.Text)+" config --get-regexp "+Quote(@"^url\..*\.insteadOf$"));if(rewrites.Item1==0&&!String.IsNullOrWhiteSpace(rewrites.Item2))return Tuple.Create(-1,"В настройках Git есть url.insteadOf. Отправка с токеном остановлена: подмена адреса репозитория недопустима.");if(rewrites.Item1!=0&&rewrites.Item1!=1)return Tuple.Create(-1,"Не удалось проверить подмену адресов Git.");p.StartInfo.EnvironmentVariables["GIT_CONFIG_COUNT"]="4";p.StartInfo.EnvironmentVariables["GIT_CONFIG_KEY_3"]="http.https://github.com/.extraHeader";p.StartInfo.EnvironmentVariables["GIT_CONFIG_VALUE_3"]="Authorization: Basic "+Convert.ToBase64String(Encoding.UTF8.GetBytes(githubUser.Text+":"+githubToken.Text));p.StartInfo.EnvironmentVariables["GIT_CONFIG_KEY_0"]="http.https://github.com/.extraHeader";p.StartInfo.EnvironmentVariables["GIT_CONFIG_VALUE_0"]="";p.StartInfo.EnvironmentVariables["GIT_CONFIG_KEY_1"]="http.followRedirects";p.StartInfo.EnvironmentVariables["GIT_CONFIG_VALUE_1"]="false";p.StartInfo.EnvironmentVariables["GIT_CONFIG_KEY_2"]="credential.helper";p.StartInfo.EnvironmentVariables["GIT_CONFIG_VALUE_2"]="";}
   string rawPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"git-details.log");string[] secrets={dbPassword.Text,repoPassword.Text,githubToken.Text,Convert.ToBase64String(Encoding.UTF8.GetBytes(githubUser.Text+":"+githubToken.Text))};string rawError=null;try{GitLog.WriteRaw(rawPath,"\r\n=== "+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" — "+GitLog.Mask(activeBaseName,secrets)+" ===\r\n[Git] "+GitLog.Mask(args,secrets)+"\r\n");}catch(Exception ex){rawError=ex.Message;}
   using(var timeoutCancellation=new CancellationTokenSource()){
   p.Start();activeGitProcess=p;bool compactCommit=args.StartsWith("commit ",StringComparison.Ordinal);var stdout=Task.Run(()=>GitLog.ReadAsync(p.StandardOutput,rawPath,"stdout",secrets,compactCommit));var stderr=Task.Run(()=>GitLog.ReadAsync(p.StandardError,rawPath,"stderr",secrets,false));var exit=Task.Run(()=>p.WaitForExit());bool timeout=await Task.WhenAny(exit,Task.Delay(TimeSpan.FromMinutes(auth?5:30),timeoutCancellation.Token))!=exit;Observe(stdout);Observe(stderr);Observe(exit);if(timeout){batchStopRequested=true;if(batchCancellation!=null)batchCancellation.Cancel();try{RuntimeSafety.KillTree(p);}catch{}return Tuple.Create(-1,"Истекло время выполнения Git. Проверьте процессы и репозиторий.");}timeoutCancellation.Cancel();await exit;var pipes=Task.WhenAll(stdout,stderr);if(await Task.WhenAny(pipes,Task.Delay(2000))!=pipes){Observe(pipes);return Tuple.Create(-1,"Git завершился, но его дочерние процессы не закрыли журнал. Проверьте процессы Git.");}await pipes;var outResult=await stdout;var errResult=await stderr;var text=Redact(outResult.Text+errResult.Text);string shown=Redact(GitLog.Summary(outResult,errResult,args.StartsWith("status --porcelain",StringComparison.Ordinal)));if(rawError!=null)shown+="\r\nПодробный журнал Git недоступен: "+Redact(rawError);if(p.ExitCode!=0&&String.IsNullOrWhiteSpace(text))text="Git завершился с кодом "+p.ExitCode+".\r\n"+shown;string entry="\r\n[Git] "+Redact(args)+"\r\n"+shown;logBox.Text=RuntimeSafety.Tail(logBox.Text+entry);try{RuntimeSafety.AppendEvent(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"exporter-events.log"),entry);}catch{}return Tuple.Create(batchStopRequested?-1:p.ExitCode,batchStopRequested?"Пакет остановлен пользователем.":(p.ExitCode==0?Redact(outResult.Text):text).Trim());}
  }}catch(Exception ex){return Tuple.Create(-1,Redact(ex.Message));}finally{activeGitProcess=null;}
 }
 void OpenGitDetails(){string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"git-details.log");try{if(!File.Exists(path)){MessageBox.Show(this,"Подробный журнал появится после первой операции Git.","Журнал Git",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}Process.Start(new ProcessStartInfo{FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"notepad.exe"),Arguments=Quote(path),UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,"Не удалось открыть журнал: "+ex.Message,"Журнал Git",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
 void RefreshLog(){
  try{
   if(activeProcess!=null&&activeProcess.HasExited&&diagnostic.IndexOf("Код завершения:")==-1)diagnostic+="\r\nКод завершения: "+activeProcess.ExitCode+"\r\n";
   if(!String.IsNullOrEmpty(activeLog)&&File.Exists(activeLog)){
    platformLog=RuntimeSafety.ReadLogCore(activeLog,activeProcess==null||activeProcess.HasExited);
   }
   var text=RuntimeSafety.Tail(diagnostic+"\r\n"+(String.IsNullOrWhiteSpace(platformLog)?"1С пока не записала сообщения в файл журнала.":Redact(platformLog)));
   if(text!=logBox.Text){logBox.Text=text;logBox.SelectionStart=logBox.TextLength;logBox.ScrollToCaret();}
  }catch(Exception ex){logBox.Text=diagnostic+"\r\nНе удалось прочитать журнал: "+ex.Message;}
 }
 string Redact(string text){foreach(var secret in new[]{dbPassword.Text,repoPassword.Text,githubToken.Text})if(!String.IsNullOrEmpty(secret))text=text.Replace(secret,"••••••");if(!String.IsNullOrEmpty(githubToken.Text))text=text.Replace(Convert.ToBase64String(Encoding.UTF8.GetBytes(githubUser.Text+":"+githubToken.Text)),"••••••");return text;}
 Process CancellationTarget(){return activeGitProcess??activeProcess;}
 async Task RequestBatchStop(){batchStopRequested=true;if(batchCancellation!=null)batchCancellation.Cancel();var p=CancellationTarget();try{if(p!=null&&!p.HasExited)await Task.Run(()=>RuntimeSafety.KillTree(p));}catch{}}
 async void CancelExport(){var p=CancellationTarget();if(batchRunning){if(MessageBox.Show(this,"Остановить текущую выгрузку и не запускать оставшиеся базы?","Остановить пакет",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;await RequestBatchStop();return;}if(p==null)return;if(MessageBox.Show(this,"Остановить текущую выгрузку?","Подтверждение",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;try{if(!p.HasExited)await Task.Run(()=>RuntimeSafety.KillTree(p));if(!IsDisposed)status.Text="Запрошена остановка выгрузки";}catch(Exception ex){MessageBox.Show(this,ex.Message,"Не удалось остановить",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
 void OpenOutput(){
  try{if(String.IsNullOrWhiteSpace(output.Text)){MessageBox.Show(this,"Укажите каталог выгрузки.");return;}var path=Path.GetFullPath(output.Text.Trim());if(File.Exists(path))throw new IOException("Указан файл вместо каталога.");
   if(!Directory.Exists(path)){Directory.CreateDirectory(path);status.Text="Каталог создан";}
   Process.Start(new ProcessStartInfo{FileName=path,UseShellExecute=true});
  }catch(Exception ex){MessageBox.Show(this,"Не удалось открыть или создать каталог:\r\n"+ex.Message,"Каталог выгрузки",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
 }
 void OnFormClosing(object s,FormClosingEventArgs e){if(batchRunning){e.Cancel=true;CancelExport();return;}if(operationBusy&&(activeProcess==null||activeProcess.HasExited)){e.Cancel=true;return;}if(activeProcess!=null&&!activeProcess.HasExited){if(MessageBox.Show(this,"Выгрузка ещё выполняется. Остановить её и закрыть приложение?","Выгрузка выполняется",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes){e.Cancel=true;return;}try{RuntimeSafety.KillTree(activeProcess);activeProcess.WaitForExit(3000);RecordEvent("Завершение процесса конфигуратора при закрытии окна, код "+activeProcess.ExitCode);}catch{}}SaveUserSettings(false);}
}
}
