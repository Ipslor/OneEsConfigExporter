using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace OneCConfigExporter {
 internal sealed class HelpSection {
  public string Title,Body;
  public override string ToString(){return Title;}
 }
 internal sealed class HelpForm : Form {
  readonly List<HelpSection> sections=new List<HelpSection>();
  readonly RichTextBox text=new RichTextBox();
  readonly TreeView contents=new TreeView();
  readonly ListBox results=new ListBox();
  readonly TextBox query=new TextBox();
  readonly Font titleFont=new Font("Segoe UI",14,FontStyle.Bold),bodyFont=new Font("Segoe UI",10);
  public HelpForm(){
   Text="Справка — Выгрузка конфигурации 1С v.2.5.1";Font=new Font("Segoe UI",9);StartPosition=FormStartPosition.CenterParent;Size=new Size(950,680);MinimumSize=new Size(740,460);ShowInTaskbar=false;
   var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(8)};root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(root);
   var split=new SplitContainer{Dock=DockStyle.Fill,FixedPanel=FixedPanel.Panel1,SplitterWidth=6};root.Controls.Add(split,0,0);split.Size=new Size(900,560);split.SplitterDistance=250;split.Panel1MinSize=200;split.Panel2MinSize=300;
   var tabs=new TabControl{Dock=DockStyle.Fill};var index=new TabPage("Содержание");var search=new TabPage("Поиск");tabs.TabPages.AddRange(new[]{index,search});split.Panel1.Controls.Add(tabs);
   contents.Dock=DockStyle.Fill;contents.HideSelection=false;index.Controls.Add(contents);
   var searchLayout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(4)};searchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));searchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));searchLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));search.Controls.Add(searchLayout);
   searchLayout.Controls.Add(new Label{Text="Слово или фраза:",AutoSize=true},0,0);query.Dock=DockStyle.Fill;searchLayout.Controls.Add(query,0,1);results.Dock=DockStyle.Fill;results.HorizontalScrollbar=true;searchLayout.Controls.Add(results,0,2);
   text.Dock=DockStyle.Fill;text.ReadOnly=true;text.BackColor=SystemColors.Window;text.BorderStyle=BorderStyle.FixedSingle;text.Font=bodyFont;text.WordWrap=true;text.DetectUrls=false;split.Panel2.Controls.Add(text);
   var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft};var close=new Button{Text="Закрыть",AutoSize=true,DialogResult=DialogResult.Cancel};close.Click+=(s,e)=>Close();footer.Controls.Add(close);root.Controls.Add(footer,0,1);CancelButton=close;
   contents.AfterSelect+=(s,e)=>Display(e.Node.Tag as HelpSection);results.SelectedIndexChanged+=(s,e)=>Display(results.SelectedItem as HelpSection);query.TextChanged+=(s,e)=>Search();
   LoadGuide();foreach(var section in sections)contents.Nodes.Add(new TreeNode(section.Title){Tag=section});
   if(contents.Nodes.Count>0)contents.SelectedNode=contents.Nodes[0];
  }
  void LoadGuide(){
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("OneCConfigExporter.UsageGuide.md")){
    if(stream==null){sections.Add(new HelpSection{Title="Инструкция недоступна",Body="Не найден встроенный текст инструкции."});return;}
    using(var reader=new StreamReader(stream,Encoding.UTF8)){
     HelpSection current=null;var body=new StringBuilder();
     while(!reader.EndOfStream){string line=reader.ReadLine();if(line.StartsWith("## ")){
       if(current!=null){current.Body=body.ToString().Trim();sections.Add(current);}current=new HelpSection{Title=line.Substring(3)};body.Clear();
      }else if(current!=null)body.AppendLine(line);
     }if(current!=null){current.Body=body.ToString().Trim();sections.Add(current);}
    }
   }
  }
  void Display(HelpSection section){
   if(section==null)return;text.Clear();text.SelectionFont=titleFont;text.AppendText(section.Title+Environment.NewLine+Environment.NewLine);
   text.SelectionFont=bodyFont;text.AppendText(section.Body);text.SelectionStart=0;text.SelectionLength=0;text.ScrollToCaret();
  }
  bool resourcesReleased;
  protected override void Dispose(bool disposing){bool first=disposing&&!resourcesReleased;var font=Font;resourcesReleased|=disposing;base.Dispose(disposing);if(first){font.Dispose();titleFont.Dispose();bodyFont.Dispose();}}
  void Search(){string term=query.Text.Trim();results.BeginUpdate();results.Items.Clear();if(term.Length>0)foreach(var section in sections)if((section.Title+"\n"+section.Body).IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0)results.Items.Add(section);results.EndUpdate();if(results.Items.Count>0)results.SelectedIndex=0;}
 }
}
