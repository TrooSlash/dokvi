using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MdReader {
  sealed class MenuDefinition {
    public string id { get; set; }
    public string kind { get; set; }
    public string text { get; set; }
    public string shortcut { get; set; }
    public string label { get; set; }
    public bool document { get; set; }
    public bool? pressed { get; set; }
    public MenuOption[] options { get; set; }
  }
  sealed class MenuOption { public string id { get; set; } public string value { get; set; } public string text { get; set; } }
  sealed class MenuControlState { public string id { get; set; } public bool disabled { get; set; } public bool selected { get; set; } public string value { get; set; } }
  sealed class DocumentMenuState {
    public MenuControlState[] controls { get; set; }
    public int fontSize { get; set; } public int width { get; set; } public string layout { get; set; }
    public string fileState { get; set; } public string stats { get; set; }
    internal MenuControlState Find(string id) { return (controls ?? new MenuControlState[0]).FirstOrDefault(control => control.id == id); }
  }
  sealed class NativeColors {
    internal readonly Color Surface, Chrome, Text, Muted, Hover, Code, Line, Accent, Selected, Sidebar;
    internal NativeColors(bool dark) {
      Surface = dark ? Color.FromArgb(32,33,34) : Color.White;
      Chrome = dark ? Color.FromArgb(41,43,46) : Color.FromArgb(240,241,243);
      Text = dark ? Color.FromArgb(226,228,231) : Color.FromArgb(41,43,46);
      Muted = dark ? Color.FromArgb(163,167,173) : Color.FromArgb(100,104,110);
      Hover = dark ? Color.FromArgb(57,60,64) : Color.FromArgb(229,231,234);
      Code = dark ? Color.FromArgb(41,43,45) : Color.FromArgb(245,246,247);
      Line = dark ? Color.FromArgb(56,59,63) : Color.FromArgb(228,230,233);
      Accent = dark ? Color.FromArgb(166,197,232) : Color.FromArgb(54,95,137);
      Selected = dark ? Color.FromArgb(45,55,67) : Color.FromArgb(237,241,246);
      Sidebar = dark ? Color.FromArgb(37,38,40) : Color.White;
    }
    internal Color Disabled { get { return Color.FromArgb((Text.R+Sidebar.R)/2,(Text.G+Sidebar.G)/2,(Text.B+Sidebar.B)/2); } }
  }
  sealed partial class ReaderForm {
    internal NativePopover nativeMenu;
    string nativeMenuKind;
    DocumentMenuState nativeMenuDocument;
    int nativeMenuToken;
    MenuDefinition[] menuDefinitions;
    internal void CloseNativeMenu(bool focusTrigger) {
      if (nativeMenu == null) return;
      string kind = nativeMenuKind; bool document = nativeMenuDocument != null;
      var trigger = nativeMenu.Trigger;
      bool restoreFocus = focusTrigger || nativeMenu.ContainsFocus;
      var previous = nativeMenu; nativeMenu = null; nativeMenuKind = null; nativeMenuDocument = null; nativeMenuToken++;
      if (restoreFocus && !trigger.IsDisposed && !IsDisposed) trigger.Select();
      Controls.Remove(previous); previous.Dispose();
      if (document && Core != null) Emit("menu-closed", new { kind, focus = restoreFocus, keyboard = QuietButton.KeyboardFocus });
    }
    bool DocumentMenuAction(string id, string value = null, bool keepOpen = true) {
      if (nativeMenuDocument == null) return false;
      int token = nativeMenuToken;
      if (!keepOpen) CloseNativeMenu(true);
      Emit("menu-action", new { id, value, token, keepOpen });
      return true;
    }
    internal bool NativeMenuShortcut(Keys keys) {
      if (nativeMenuDocument == null || (keys & Keys.Alt) != 0) return false;
      var key=keys & Keys.KeyCode;
      bool ctrl=(keys & Keys.Control)!=0;
      string code=null;
      if (key==Keys.F1) code="F1";
      else if (ctrl && key==Keys.Tab) code="Tab";
      else if (ctrl && key>=Keys.D1 && key<=Keys.D9) code="Digit"+((int)key-(int)Keys.D0);
      else if (ctrl && "BEFNOPRSWYZ".Contains(((char)key).ToString())) code="Key"+key;
      if(code==null) return false;
      CloseNativeMenu(true);
      Emit("menu-key",new {code,ctrlKey=ctrl,shiftKey=(keys & Keys.Shift)!=0});
      return true;
    }
    internal void ToggleNativeMenu(string kind, DocumentMenuState document = null) {
      bool close = nativeMenu != null && nativeMenuKind == kind;
      CloseNativeMenu(false);
      if (close) return;
      nativeMenuDocument = document; nativeMenuToken++;
      float scale = ScaleFactor;
      Func<int,int> px = value => (int)Math.Round(value * scale);
      var colors = new NativeColors(IsDark);
      Control trigger = document != null ? (Control)View : kind == "more" ? nativeMore : nativeAppearance;
      nativeMenuKind = kind;
      nativeMenu = new NativePopover(this, trigger, colors, delegate { CloseNativeMenu(false); });
      int inset = ClientSize.Width <= px(850) ? 11 : 15;
      nativeMenu.TriggerBounds = RectangleToScreen(new Rectangle(ClientSize.Width-px(inset+(kind=="more"?31:68)),px(52),px(31),px(30)));
      nativeMenu.Name = kind + "-menu";
      nativeMenu.AccessibleName = kind == "more" ? nativeMore.AccessibleName : nativeAppearance.AccessibleName;
      nativeMenu.Width = px(kind == "more" ? 285 : 280);
      var content = nativeMenu.Content;
      content.Width = nativeMenu.Width - 2;
      var smallFont = new Font("Segoe UI",8.25f);
      var sizeFont = new Font("Segoe UI",10.5f);
      nativeMenu.Disposed += delegate { smallFont.Dispose(); sizeFont.Dispose(); };
      int y = px(8);
      Action<Control,int,int,int> add = (control,x,width,height) => {
        control.SetBounds(px(x),y,px(width),px(height)); content.Controls.Add(control);
        control.Anchor=AnchorStyles.Top|(x>150?AnchorStyles.Right:width>180?AnchorStyles.Left|AnchorStyles.Right:AnchorStyles.Left);
      };
      Func<string,string,int,int,EventHandler,NativeMenuButton> button = (id,text,x,width,action) => {
        var b = new NativeMenuButton { Name=id, Text=text, AccessibleName=text, Font=Font, ForeColor=colors.Text,
          FocusColor=colors.Accent, ShortcutColor=colors.Muted, DisabledColor=colors.Disabled, BackColor=colors.Selected };
        FlatButton(b,text,text,delegate(object sender,EventArgs e) {
          bool keepOpen = id=="theme" || id.StartsWith("font-") || id.StartsWith("reading-");
          if (!DocumentMenuAction(id,null,keepOpen)) action(sender,e);
        }); b.FlatAppearance.MouseOverBackColor=colors.Hover;
        add(b,x,width,33); return b;
      };
      Action<string,int,int,int> label = (text,x,width,height) => {
        var l = new SurfaceLabel { Text=text, Font=smallFont, ForeColor=colors.Muted, AlignLeft=true };
        add(l,x,width,height);
      };
      Action separator = () => { y+=px(6); add(new Panel { BackColor=colors.Line },12,(kind=="more"?285:280)-26,1); y+=px(7); };
      if (kind == "more") {
        if (menuDefinitions == null) menuDefinitions = Program.Json.Deserialize<MenuDefinition[]>(File.ReadAllText(Path.Combine(Program.CacheRoot,"ui","menu-items.json")));
        foreach (var item in menuDefinitions) {
          if (item.kind == "separator") { separator(); continue; }
          if (item.kind == "label") { label(L.T(item.text),18,249,24); y+=px(24); continue; }
          if (item.kind == "file-info") {
            if (document != null && !String.IsNullOrEmpty(document.fileState)) {
              label(document.fileState,18,249,17); y+=px(17);
              label(document.stats,18,249,17); y+=px(17);
            }
            y+=px(11); continue;
          }
          if (item.kind == "encoding" || item.kind == "language") {
            bool language = item.kind == "language";
            label(L.T(item.text),18,language?249:110,25);
            if (language) y+=px(25);
            var state = document == null ? null : document.Find(item.id);
            var select = new NativeMenuSelect(colors) { Name=item.id, AccessibleName=L.T(item.label), Font=smallFont, Enabled=state!=null?!state.disabled:language };
            var options = item.options;
            select.Items.AddRange(options.Select(option => (object)(option.value=="system" ? L.T("Как в системе ({0})",L.Resolve("system",L.SystemLanguage)=="ru"?"Русский":"English") : L.T(option.text))).ToArray());
            string selectedValue = state!=null ? state.value : language ? L.Preference : "auto";
            select.SelectedIndex = Math.Max(0,Array.FindIndex(options,option=>option.value==selectedValue));
            add(select,language?18:162,language?249:105,25);
            select.SelectionChangeCommitted += async delegate {
              if (select.SelectedIndex<0 || options[select.SelectedIndex].value==selectedValue) return;
              if (DocumentMenuAction(item.id,options[select.SelectedIndex].value,language)) return;
              if (!language) return;
              try { await Call("language",new object[]{options[select.SelectedIndex].value}); }
              catch(Exception error) { MessageBox.Show(this,L.T("Не удалось сохранить настройки: ")+error.Message,"Dokvi",MessageBoxButtons.OK,MessageBoxIcon.Error); }
              RebuildNativeMenu("language");
            };
            y+=px(34); continue;
          }
          string id=item.id, text=id=="theme" ? L.T(IsDark?"Светлая тема":"Тёмная тема") : L.T(item.text);
          var command = button(id,text,8,267,async delegate {
            if (id=="theme") { await Call("theme",new object[]{IsDark?"light":"dark"}); RebuildNativeMenu("theme"); return; }
            CloseNativeMenu(false);
            if (id=="new-more") CreateFromNative();
            else if (id=="open-more") OpenFromNative();
            else if (id=="help") await EnsureBrowser("help");
            else if (id=="default-apps") {
              try { await Call("default-apps",new object[0]); }
              catch(Exception error) { MessageBox.Show(this,error.Message,"Dokvi",MessageBoxButtons.OK,MessageBoxIcon.Error); }
            }
          });
          var commandState = document == null ? null : document.Find(id);
          command.Enabled=commandState!=null?!commandState.disabled:!item.document;
          command.Shortcut=item.shortcut; command.Moon=id=="theme";
          command.Selected=commandState!=null?commandState.selected:item.pressed==true || (id=="theme" && IsDark);
          if (command.Selected) command.ForeColor=colors.Accent;
          y+=px(33);
        }
      } else {
        int size=NativeNumber("font-size",16,13,25), width=NativeNumber("reading-width",80,50,100);
        string layout; if (!Preferences.TryGetValue("reading-layout",out layout) || !new[]{"left","center","full"}.Contains(layout)) layout="left";
        if (document!=null) { size=document.fontSize; width=document.width; layout=document.layout; }
        label(L.T("Размер текста"),18,244,24); y+=px(28);
        var down=button("font-down","A−",15,52,delegate { SavePreference("font-size",(size-1).ToString()); RebuildNativeMenu("font-down"); });
        down.AccessibleName=L.T("Уменьшить текст"); down.Enabled=size>13; down.Selected=true; down.BackColor=colors.Code; down.Centered=true; down.Font=sizeFont;
        var value=new SurfaceLabel { Text=size.ToString(),ForeColor=colors.Text }; add(value,110,60,33);
        var up=button("font-up","A+",211,52,delegate { SavePreference("font-size",(size+1).ToString()); RebuildNativeMenu("font-up"); });
        up.AccessibleName=L.T("Увеличить текст"); up.Enabled=size<25; up.Selected=true; up.BackColor=colors.Code; up.Centered=true; up.Font=sizeFont;
        y+=px(37); separator(); label(L.T("Область чтения"),18,244,25); y+=px(28);
        string[] layouts={"left","center","full"}, labels={L.T("Слева"),L.T("По центру"),L.T("Всё окно")};
        for(int i=0;i<layouts.Length;i++) {
          string selected=layouts[i];
          var b=button("reading-"+selected,labels[i],16+i*84,80,delegate { SavePreference("reading-layout",selected); RebuildNativeMenu("reading-"+selected); });
          b.Font=smallFont; b.Selected=layout==selected; b.Centered=true;
          if(b.Selected) b.ForeColor=colors.Accent;
        }
        y+=px(44); label(L.T("Ширина"),18,180,24);
        var percentage=new SurfaceLabel { Text=(layout=="full"?100:width)+"%",ForeColor=colors.Muted,Font=smallFont }; add(percentage,220,44,24);
        y+=px(26);
        var slider=new NativeWidthSlider { Name="reading-width",AccessibleName=L.T("Ширина области чтения"),Value=layout=="full"?100:width,ForeColor=colors.Accent,TrackColor=colors.Line,FocusColor=colors.Accent,BackColor=colors.Sidebar };
        slider.ValueChanged += delegate {
          bool documentCommand=DocumentMenuAction("reading-width",slider.Value.ToString());
          if (!documentCommand) SavePreference("reading-width",slider.Value.ToString());
          if(layout=="full") {
            layout="left"; if (!documentCommand) SavePreference("reading-layout",layout);
            foreach(var b in content.Controls.OfType<NativeMenuButton>().Where(b=>b.Name.StartsWith("reading-"))) { b.Selected=b.Name=="reading-left"; b.ForeColor=b.Selected?colors.Accent:colors.Text; b.Invalidate(); }
          }
          percentage.Text=slider.Value+"%";
        };
        add(slider,16,248,24); y+=px(27);
      }
      content.Height=y+px(8);
      nativeMenu.Height=Math.Min(content.Height+2,ClientSize.Height-px(100));
      nativeMenu.Location=new Point(ClientSize.Width-px(kind=="more"?15:52)-nativeMenu.Width,px(84));
      Controls.Add(nativeMenu); nativeMenu.BringToFront(); nativeMenu.Update();
    }
    int NativeNumber(string key,int fallback,int min,int max) { string text; int value; return Preferences.TryGetValue(key,out text) && Int32.TryParse(text,out value) ? Math.Max(min,Math.Min(max,value)) : fallback; }
    void RebuildNativeMenu(string focus) {
      string kind=nativeMenuKind; if(kind==null) return;
      var document=nativeMenuDocument;
      var previous=nativeMenu;
      int scroll=-previous.AutoScrollPosition.Y;
      // Keep the focused control alive until its replacement can receive focus.
      nativeMenu=null; nativeMenuKind=null; nativeMenuDocument=null;
      ToggleNativeMenu(kind,document);
      nativeMenu.AutoScrollPosition=new Point(0,scroll);
      var control=nativeMenu.Content.Controls.Find(focus,true).FirstOrDefault();
      if(control!=null && control.Enabled) control.Focus();
      else nativeMenu.HandleNavigation(Keys.Home);
      Controls.Remove(previous); previous.Dispose();
    }
  }
  sealed class NativeMenuButton : QuietButton {
    internal string Shortcut;
    internal bool Moon, Centered;
    internal Color DisabledColor;
    protected override void DrawContent(Graphics g,float scale,Color fill) {
      Color ink=Enabled?ForeColor:DisabledColor;
      int left=(int)Math.Round((Centered?4:Moon?32:10)*scale), inset=(int)Math.Ceiling(4*scale);
      int shortcutWidth=String.IsNullOrEmpty(Shortcut)?0:(int)Math.Ceiling(g.MeasureString(Shortcut,Font).Width+12*scale);
      SurfaceLabel.Draw(g,Text,Font,new Rectangle(left,inset,Math.Max(1,Width-left-shortcutWidth-inset),Height-2*inset),ink,fill,Centered);
      if(shortcutWidth>0) using(var font=new Font("Segoe UI",7.5f)) SurfaceLabel.Draw(g,Shortcut,font,new Rectangle(Width-shortcutWidth,inset,shortcutWidth-inset,Height-2*inset),Enabled?ShortcutColor:DisabledColor,fill,true);
      if(Moon) using(var pen=new Pen(ink,1.4f*scale)) {
        var state=g.Save(); g.TranslateTransform(10*scale,Height/2f-7*scale); g.ScaleTransform(scale,scale);
        using(var moon=new GraphicsPath()) {
          moon.AddBezier(6,0,1,0,-1,6,2,10); moon.AddBezier(2,10,5,15,12,13,14,8);
          moon.AddBezier(14,8,8,11,3,5,6,0); g.DrawPath(pen,moon);
        }
        g.Restore(state);
      }
    }
  }
  sealed class NativePopover : Panel, IMessageFilter {
    internal readonly Panel Content;
    internal readonly Control Trigger;
    internal Rectangle TriggerBounds;
    readonly Form owner; readonly Action close; readonly Color border;
    internal NativePopover(Form owner,Control trigger,NativeColors colors,Action close) {
      this.owner=owner; Trigger=trigger; this.close=close; border=colors.Line;
      SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint,true);
      AutoScroll=true; BackColor=colors.Sidebar; ForeColor=colors.Text;
      Content=new Panel { Location=new Point(1,1),BackColor=colors.Sidebar }; Controls.Add(Content);
      owner.Deactivate+=OwnerChanged; owner.Resize+=OwnerChanged; owner.LocationChanged+=OwnerChanged;
      Application.AddMessageFilter(this);
    }
    void OwnerChanged(object sender,EventArgs e) { close(); }
    public bool PreFilterMessage(ref Message message) {
      if(message.Msg==0x201 || message.Msg==0x204 || message.Msg==0x207) {
        if(Content.Controls.OfType<ComboBox>().Any(select=>select.DroppedDown)) return false;
        Point p=Control.MousePosition;
        if(!RectangleToScreen(ClientRectangle).Contains(p) && !TriggerBounds.Contains(p)) close();
      }
      return false;
    }
    protected override void OnLayout(LayoutEventArgs e) {
      base.OnLayout(e); if(Content!=null) Content.Width=Math.Max(1,ClientSize.Width-2);
      if(Width<16 || Height<16) return;
      using(var shape=QuietButton.RoundedRectangle(new RectangleF(0,0,Width,Height),14*DeviceDpi/96f)) {
        var previous=Region; Region=new Region(shape); if(previous!=null) previous.Dispose();
      }
      if(Content!=null) using(var shape=QuietButton.RoundedRectangle(new RectangleF(0,1-Content.Top,ClientSize.Width-2,ClientSize.Height-2),12*DeviceDpi/96f)) {
        var previous=Content.Region; Content.Region=new Region(shape); if(previous!=null) previous.Dispose();
      }
    }
    protected override void OnPaint(PaintEventArgs e) {
      base.OnPaint(e); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
      using(var pen=new Pen(border)) using(var shape=QuietButton.RoundedRectangle(new RectangleF(.5f,.5f,Width-1,Height-1),14*DeviceDpi/96f)) e.Graphics.DrawPath(pen,shape);
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keys) {
      if(HandleNavigation(keys)) return true;
      return base.ProcessCmdKey(ref msg,keys);
    }
    internal bool HandleNavigation(Keys keys) {
      var focused=FindFocused(this);
      if(keys==Keys.Tab || keys==(Keys.Shift|Keys.Tab)) { SelectNextControl(focused,keys==Keys.Tab,true,true,true); return true; }
      if((focused==null || focused is Button) && (keys==Keys.Down || keys==Keys.Up || keys==Keys.Home || keys==Keys.End)) {
        SelectNextControl(keys==Keys.Home || keys==Keys.End?null:focused,keys==Keys.Down || keys==Keys.Home,true,true,true); return true;
      }
      return false;
    }
    static Control FindFocused(Control parent) { foreach(Control c in parent.Controls) { if(c.Focused) return c; if(c.ContainsFocus) return FindFocused(c); } return null; }
    protected override void Dispose(bool disposing) {
      if(disposing) { Application.RemoveMessageFilter(this); owner.Deactivate-=OwnerChanged; owner.Resize-=OwnerChanged; owner.LocationChanged-=OwnerChanged; }
      base.Dispose(disposing);
    }
  }
  sealed class NativeWidthSlider : Control {
    int value=80; internal Color TrackColor,FocusColor;
    internal event EventHandler ValueChanged;
    internal int Value { get { return value; } set { int next=Math.Max(50,Math.Min(100,value)); if(this.value==next)return; this.value=next; Invalidate(); AccessibilityNotifyClients(AccessibleEvents.ValueChange,-1); if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty); } }
    internal NativeWidthSlider() { SetStyle(ControlStyles.Selectable|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); TabStop=true; AccessibleRole=AccessibleRole.Slider; Cursor=Cursors.Hand; }
    void FromMouse(int x) { float inset=8*DeviceDpi/96f; Value=50+5*(int)Math.Round(Math.Max(0,Math.Min(1,(x-inset)/Math.Max(1,Width-2*inset)))*10); }
    protected override void OnMouseDown(MouseEventArgs e) { if(e.Button==MouseButtons.Left) { Focus(); Capture=true; FromMouse(e.X); } base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if(Capture) FromMouse(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Capture=false; base.OnMouseUp(e); }
    protected override bool IsInputKey(Keys key) { return new[]{Keys.Left,Keys.Right,Keys.Up,Keys.Down,Keys.Home,Keys.End,Keys.PageUp,Keys.PageDown}.Contains(key) || base.IsInputKey(key); }
    protected override void OnKeyDown(KeyEventArgs e) {
      if(e.KeyCode==Keys.Home) Value=50; else if(e.KeyCode==Keys.End) Value=100;
      else if(e.KeyCode==Keys.Left || e.KeyCode==Keys.Down) Value-=5; else if(e.KeyCode==Keys.Right || e.KeyCode==Keys.Up) Value+=5;
      else if(e.KeyCode==Keys.PageUp) Value+=10; else if(e.KeyCode==Keys.PageDown) Value-=10; else { base.OnKeyDown(e); return; }
      e.Handled=true;
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e) {
      var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias; float scale=g.DpiX/96f, x=8*scale+(Width-16*scale)*(value-50)/50f,y=Height/2f;
      using(var pen=new Pen(TrackColor,4*scale) { StartCap=LineCap.Round,EndCap=LineCap.Round }) g.DrawLine(pen,8*scale,y,Width-8*scale,y);
      using(var pen=new Pen(ForeColor,4*scale) { StartCap=LineCap.Round,EndCap=LineCap.Round }) g.DrawLine(pen,8*scale,y,x,y);
      using(var brush=new SolidBrush(ForeColor)) g.FillEllipse(brush,x-7*scale,y-7*scale,14*scale,14*scale);
      if(Focused && ShowFocusCues) using(var pen=new Pen(FocusColor,scale)) g.DrawRectangle(pen,0,0,Width-1,Height-1);
    }
    protected override AccessibleObject CreateAccessibilityInstance() { return new SliderAccessibility(this); }
    sealed class SliderAccessibility : ControlAccessibleObject {
      readonly NativeWidthSlider slider;
      internal SliderAccessibility(NativeWidthSlider slider):base(slider) { this.slider=slider; }
      public override string Value { get { return slider.Value.ToString(); } set { int number; if(Int32.TryParse(value,out number)) slider.Value=number; } }
    }
  }
}
