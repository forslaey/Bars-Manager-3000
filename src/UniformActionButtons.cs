using System;
using System.Drawing;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class UniformActionButtons
 {
  internal static void Bind(TableLayoutPanel panel)
  {
   bool arranging=false;
   panel.Layout+=delegate{
    if(arranging||panel.ClientSize.Width<1||panel.ClientSize.Height<1)return;arranging=true;
    try{
     float scale=panel.DeviceScale();int gap=(int)Math.Round(10*scale),vertical=(int)Math.Round(8*scale);
     int width=Math.Max(1,(panel.ClientSize.Width-2*gap)/3),height=Math.Max(1,(panel.ClientSize.Height-vertical)/2);
     // Equal absolute tracks; rounding remainder is kept outside the buttons.
     for(int i=0;i<3;i++){panel.ColumnStyles[i].SizeType=SizeType.Absolute;panel.ColumnStyles[i].Width=width+(i<2?gap:0);}
     for(int i=0;i<2;i++){panel.RowStyles[i].SizeType=SizeType.Absolute;panel.RowStyles[i].Height=height+(i==0?vertical:0);}
     for(int row=0;row<2;row++)for(int column=0;column<3;column++){
      var button=panel.GetControlFromPosition(column,row);if(button==null)continue;
      button.Dock=DockStyle.None;button.Anchor=AnchorStyles.Top|AnchorStyles.Left;button.Margin=Padding.Empty;
      button.SetBounds(column*(width+gap),row*(height+vertical),width,height);
     }
    }finally{arranging=false;}
   };
  }
  static float DeviceScale(this Control control){using(var graphics=control.CreateGraphics())return graphics.DpiX/96F;}
 }
}
