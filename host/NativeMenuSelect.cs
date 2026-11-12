using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdReader {
  // Retain the native popup and accessibility, with the shared surface palette.
  sealed class NativeMenuSelect : ComboBox {
    readonly NativeColors colors;
    int dropDownIndex;
    internal NativeMenuSelect(NativeColors colors) {
      this.colors=colors; DropDownStyle=ComboBoxStyle.DropDownList; FlatStyle=FlatStyle.Flat; DrawMode=DrawMode.OwnerDrawFixed;
      BackColor=colors.Surface; ForeColor=colors.Text; IntegralHeight=true;
    }
    protected override void OnDropDown(EventArgs e) { dropDownIndex=SelectedIndex; base.OnDropDown(e); }
    bool IsDropDownKey(Keys keyData) { return DroppedDown && (keyData==Keys.Enter || keyData==Keys.Escape); }
    protected override bool ProcessCmdKey(ref Message message,Keys keyData) {
      // The form closes the whole menu on Escape; the open list gets first refusal.
      if (!IsDropDownKey(keyData)) return base.ProcessCmdKey(ref message,keyData);
      if (keyData==Keys.Escape) SelectedIndex=dropDownIndex;
      return false;
    }
    protected override bool IsInputKey(Keys keyData) { return IsDropDownKey(keyData) || base.IsInputKey(keyData); }
    protected override void OnDrawItem(DrawItemEventArgs e) {
      if(e.Index<0) return;
      bool selected=(e.State & DrawItemState.Selected)!=0;
      var fill=selected?colors.Selected:colors.Surface;
      using(var brush=new SolidBrush(fill)) e.Graphics.FillRectangle(brush,e.Bounds);
      var bounds=e.Bounds; bounds.Inflate(-4,0);
      SurfaceLabel.Draw(e.Graphics,GetItemText(Items[e.Index]),Font,bounds,colors.Text,fill,false);
    }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ItemHeight=Font.Height+6; }
    protected override void WndProc(ref Message message) {
      base.WndProc(ref message);
      if(message.Msg!=0xF || !IsHandleCreated) return;
      using(var g=Graphics.FromHwnd(Handle)) {
        float scale=g.DpiX/96f;
        g.Clear(BackColor); g.SmoothingMode=SmoothingMode.AntiAlias;
        var text=new Rectangle((int)(5*scale),2,Math.Max(1,Width-(int)(24*scale)),Height-4);
        SurfaceLabel.Draw(g,Text,Font,text,Enabled?ForeColor:colors.Muted,BackColor,false);
        using(var pen=new Pen(Focused && ShowFocusCues?colors.Accent:colors.Line,scale))
        using(var shape=QuietButton.RoundedRectangle(new RectangleF(.5f,.5f,Width-1,Height-1),6*scale)) g.DrawPath(pen,shape);
        float x=Width-11*scale,y=Height/2f;
        using(var pen=new Pen(colors.Muted,scale)) g.DrawLines(pen,new[]{new PointF(x-3*scale,y-scale),new PointF(x,y+2*scale),new PointF(x+3*scale,y-scale)});
      }
    }
  }
}
