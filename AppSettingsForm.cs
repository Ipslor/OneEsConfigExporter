using System;
using System.Drawing;
using System.Windows.Forms;

namespace OneCConfigExporter {
 internal sealed class AppSettingsForm : Form {
  public ComboBox Platform=new ComboBox();
  public TextBox Git=new TextBox(),GithubUser=new TextBox(),GithubToken=new TextBox();
  public AppSettingsForm(string platform,string[] versions,string git,string user,string token){
   Text="Настройки приложения";Font=new Font("Segoe UI",9);ClientSize=new Size(720,320);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;
   var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(14),ColumnCount=3,RowCount=7};root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));Controls.Add(root);
   Platform.Items.AddRange(versions);Platform.Text=platform;Git.Text=git;GithubUser.Text=user;GithubToken.Text=token;GithubToken.UseSystemPasswordChar=true;
   Add(root,"Платформа 1С (1cv8.exe):",Platform,0);Add(root,"Локальный Git (git.exe):",Git,1);Add(root,"Пользователь GitHub:",GithubUser,2);Add(root,"Токен GitHub:",GithubToken,3);
   var onec=new Button{Text="Обзор…",AutoSize=true};onec.Click+=(s,e)=>Browse(Platform,"1cv8.exe");root.Controls.Add(onec,2,0);
   var gitButton=new Button{Text="Обзор…",AutoSize=true};gitButton.Click+=(s,e)=>Browse(Git,"git.exe");root.Controls.Add(gitButton,2,1);
   var note=new Label{Text="Выберите установленную платформу из списка или укажите файл вручную.\r\nGit и GitHub необязательны для выгрузки. Токен GitHub заменяет пароль аккаунта.\r\nТокен должен иметь доступ ко ВСЕМ репозиториям, используемым утилитой,\r\nи права Contents: Read and write. Токен с доступом к одному репозиторию\r\nне позволит отправить остальные. Выберите All repositories или весь нужный список.",AutoSize=true,MaximumSize=new Size(660,0),Margin=new Padding(3,12,3,12)};root.Controls.Add(note,0,4);root.SetColumnSpan(note,3);
   root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
   var buttons=new FlowLayoutPanel{FlowDirection=FlowDirection.RightToLeft,Dock=DockStyle.Fill,AutoSize=true};var ok=new Button{Text="Применить",AutoSize=true,DialogResult=DialogResult.OK};var cancel=new Button{Text="Отмена",AutoSize=true,DialogResult=DialogResult.Cancel};buttons.Controls.Add(ok);buttons.Controls.Add(cancel);root.Controls.Add(buttons,0,6);root.SetColumnSpan(buttons,3);AcceptButton=ok;CancelButton=cancel;
  }
  bool resourcesReleased;
  protected override void Dispose(bool disposing){bool first=disposing&&!resourcesReleased;var font=Font;resourcesReleased|=disposing;base.Dispose(disposing);if(first)font.Dispose();}
  static void Add(TableLayoutPanel root,string text,Control field,int row){root.Controls.Add(new Label{Text=text,AutoSize=true,Anchor=AnchorStyles.Left},0,row);field.Dock=DockStyle.Fill;root.Controls.Add(field,1,row);}
  void Browse(Control field,string executable){using(var d=new OpenFileDialog{Filter=executable+"|"+executable,CheckFileExists=true})if(d.ShowDialog(this)==DialogResult.OK)field.Text=d.FileName;}
 }
}
