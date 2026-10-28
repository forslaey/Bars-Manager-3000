using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace ServiceTool
{
 internal sealed class BundleCloudDialog:ManagerModuleForm
 {
  internal BundleCloud Cloud {get;private set;}
  internal BundleCloudDialog(bool checking):base(checking?"Источник проверки":"Источник загрузки",new Size(420,262))
  {
   MinimumSize=SizeFromClientSize(new Size(420,262));ClientSize=new Size(420,262);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=MinimizeBox=false;Root.RowStyles[0].Height=60;
   var body=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,76));body.RowStyles.Add(new RowStyle(SizeType.Absolute,52));body.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
   var choices=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));choices.RowStyles.Add(new RowStyle(SizeType.Percent,50));choices.RowStyles.Add(new RowStyle(SizeType.Percent,50));
   var y=new BrandCheckBox {Text="Яндекс Диск",Checked=true,Width=350,Height=30,Palette=Colors};var g=new BrandCheckBox {Text="Google Диск",Width=350,Height=30,Palette=Colors};
   if(!checking){y.CheckedChanged+=delegate{if(y.Checked)g.Checked=false;};g.CheckedChanged+=delegate{if(g.Checked)y.Checked=false;};}y.Dock=g.Dock=DockStyle.Fill;y.Margin=g.Margin=Padding.Empty;choices.Controls.Add(y,0,0);choices.Controls.Add(g,0,1);body.Controls.Add(choices,0,0);
   body.Controls.Add(Label(checking?"Можно выбрать оба облака. Такая проверка займёт больше времени.":"Выберите одно облако для скачивания.",true),0,1);
   var actions=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,10,0,0)};actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));var ok=Button("Продолжить",true);var cancel=Button("Отмена",false);cancel.Margin=Padding.Empty;ok.Click+=delegate{if(!y.Checked&&!g.Checked)return;Cloud=y.Checked?(g.Checked?BundleCloud.Both:BundleCloud.Yandex):BundleCloud.Google;DialogResult=DialogResult.OK;Close();};cancel.Click+=delegate{DialogResult=DialogResult.Cancel;Close();};actions.Controls.Add(ok,0,0);actions.Controls.Add(cancel,1,0);body.Controls.Add(actions,0,2);Root.Controls.Add(body,0,1);SetTheme(Colors.Dark,false);
  }
 }
 internal sealed class BundleDifferenceItem
 {internal string Path,State;internal bool Allowed,Selected;}
}
