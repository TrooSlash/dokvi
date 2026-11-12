using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdReader {
  class QuietButton : Button {
    bool hovered, pressed;
    internal static bool KeyboardFocus;
    internal Color FocusColor, ShortcutColor;
    internal bool Selected;
    internal QuietButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    protected override void OnMouseEnter(EventArgs e) { hovered=true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered=pressed=false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { KeyboardFocus=false; pressed=e.Button==MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed=false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { KeyboardFocus=true; if(e.KeyCode==Keys.Space) pressed=true; Invalidate(); base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed=false; Invalidate(); base.OnKeyUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { pressed=false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e) {
      var graphics=e.Graphics; float scale=graphics.DpiX/96f;
      graphics.Clear(Parent.BackColor); graphics.SmoothingMode=SmoothingMode.AntiAlias;
      bool focus=Focused && KeyboardFocus && ShowFocusCues;
      Color fill=FlatAppearance.BorderSize>0 || Selected ? BackColor : Parent.BackColor;
      if(Enabled && (hovered || pressed)) fill=FlatAppearance.MouseOverBackColor;
      ChromeControls.PaintSurface(graphics,ClientSize,scale,fill,focus?(Color?)FocusColor:FlatAppearance.BorderSize>0?(Color?)FlatAppearance.BorderColor:null);
      DrawContent(graphics,scale,fill);
    }
    internal static GraphicsPath RoundedRectangle(RectangleF box, float diameter) {
      var shape=new GraphicsPath();
      shape.AddArc(box.Left,box.Top,diameter,diameter,180,90);
      shape.AddArc(box.Right-diameter,box.Top,diameter,diameter,270,90);
      shape.AddArc(box.Right-diameter,box.Bottom-diameter,diameter,diameter,0,90);
      shape.AddArc(box.Left,box.Bottom-diameter,diameter,diameter,90,90);
      shape.CloseFigure(); return shape;
    }
    protected virtual void DrawContent(Graphics graphics, float scale, Color fill) {
      var textBounds = ClientRectangle;
      textBounds.Inflate(-(int)Math.Ceiling(4*scale), -(int)Math.Ceiling(4*scale));
      SurfaceLabel.Draw(graphics,Text,Font,textBounds,Enabled ? ForeColor : SystemColors.GrayText,fill,true);
    }
  }
  sealed class ChromeLabelButton : QuietButton {
    readonly string id;
    internal ChromeLabelButton(string id) { this.id=id; }
    protected override void DrawContent(Graphics graphics,float scale,Color fill) { ChromeControls.PaintLabel(graphics,id,scale,ForeColor); }
  }
  sealed class SectionsButton : QuietButton {
    protected override void DrawContent(Graphics graphics, float scale, Color fill) {
      int size=(int)Math.Round(ChromeIcons.Size("panel")*scale);
      ChromeIcons.Paint(graphics,"panel",size,new Point((int)Math.Round(8*scale),(Height-size+1)/2),ForeColor);
      ChromeControls.PaintLabel(graphics,"outline-toggle",scale,ForeColor);
    }
  }
  sealed class MoreButton : QuietButton {
    protected override void DrawContent(Graphics graphics, float scale, Color fill) {
      int size=(int)Math.Round(ChromeIcons.Size("more")*scale);
      ChromeIcons.Paint(graphics,"more",size,new Point((Width-size+1)/2,(Height-size+1)/2),ForeColor);
    }
  }
  sealed class ShortcutButton : QuietButton {
    readonly string shortcut;
    internal ShortcutButton(string shortcut) { this.shortcut = shortcut; }
    protected override void DrawContent(Graphics graphics, float scale, Color fill) {
      using (var shortcutFont = new Font("Segoe UI", 7.5f)) {
        int textWidth = (int)Math.Ceiling(graphics.MeasureString(Text, Font, PointF.Empty, StringFormat.GenericTypographic).Width);
        int shortcutWidth = (int)Math.Ceiling(graphics.MeasureString(shortcut, shortcutFont, PointF.Empty, StringFormat.GenericTypographic).Width);
        int gap = (int)Math.Round(25 * scale), left = (Width - textWidth - gap - shortcutWidth) / 2;
        SurfaceLabel.Draw(graphics, Text, Font, new Rectangle(left, 3, textWidth, Height - 6), ForeColor, fill, false);
        SurfaceLabel.Draw(graphics, shortcut, shortcutFont, new Rectangle(left + textWidth + gap, 3, shortcutWidth, Height - 6), ShortcutColor, fill, false);
      }
    }
  }
  sealed class PlusButton : QuietButton {
    protected override void DrawContent(Graphics graphics, float scale, Color fill) {
      int size=(int)Math.Round(ChromeIcons.Size("plus")*scale);
      ChromeIcons.Paint(graphics,"plus",size,new Point((Width-size+1)/2,(Height-size+1)/2),Enabled?ForeColor:SystemColors.GrayText);
    }
  }
}
