using System;
using System.Drawing;
using System.Windows.Forms;

namespace MdReader {
  sealed partial class ReaderForm {
    Panel nativeTop, nativeToolbar, welcome;
    Label welcomeTitle, welcomeHint;
    QuietButton nativeSections;
    PictureBox nativeIcon;
    Image nativeIconLight, nativeIconDark;
    QuietButton nativeOpen, nativeCreate, nativeNewTab, nativeSample, nativePlus, nativeAppearance, nativeMore;
    CaptionButton[] captionButtons;
    float ScaleFactor { get { using (var graphics = CreateGraphics()) return graphics.DpiX / 96f; } }
    int Px(int value) { return (int)Math.Round(value * ScaleFactor); }
    void BuildNativeSurface() {
      Font = ChromeControls.Font();
      empty = new Panel { Dock = DockStyle.Fill, AllowDrop = true };
      nativeTop = new Panel { Dock = DockStyle.Top, Height = Px(ChromeControls.BarHeight) };
      nativeToolbar = new Panel { Dock = DockStyle.Top, Height = Px(ChromeControls.BarHeight) };
      nativeIconLight = Image.FromFile(System.IO.Path.Combine(Program.CacheRoot, "ui", "icon.png"));
      nativeIconDark = Image.FromFile(System.IO.Path.Combine(Program.CacheRoot, "ui", "icon-dark.png"));
      nativeIcon = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = "Dokvi" };
      nativeIcon.Disposed += delegate { nativeIconLight.Dispose(); nativeIconDark.Dispose(); };
      nativePlus = FlatButton(new PlusButton(), "", L.T("Открыть файлы (Ctrl+O)"), delegate { OpenFromNative(); });
      nativeNewTab = FlatButton(new ChromeLabelButton("new"), L.T("Создать"), L.T("Создать новый файл"), delegate { CreateFromNative(); });
      nativeNewTab.SetBounds(Px(76), Px(ChromeControls.ButtonTop), Px(ChromeControls.CreateWidth), Px(ChromeControls.ButtonHeight));
      nativeIcon.SetBounds(Px(14), Px(13), Px(18), Px(18)); nativePlus.SetBounds(Px(42), Px(ChromeControls.ButtonTop), Px(ChromeControls.OpenWidth), Px(ChromeControls.ButtonHeight));
      nativeTop.Controls.AddRange(new Control[] { nativeIcon, nativePlus, nativeNewTab });
      MouseEventHandler drag = delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) MoveWindow(); };
      nativeTop.MouseDown += drag; nativeIcon.MouseDown += drag;
      nativeTop.DoubleClick += delegate { ToggleMaximize(); }; nativeIcon.DoubleClick += delegate { ToggleMaximize(); };
      nativeSections = new SectionsButton { Text = L.T("Разделы"), AccessibleName = L.T("Разделы"),
        Location = new Point(Px(ChromeControls.ToolbarInset), Px(ChromeControls.ButtonTop)), Size = new Size(Px(ChromeControls.Width("outline-toggle")), Px(ChromeControls.ButtonHeight)),
        FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Enabled = false, TabStop = false };
      nativeSections.FlatAppearance.BorderSize = 0;
      nativeAppearance = FlatButton(new ChromeLabelButton("appearance"), "Aa", L.T("Оформление"), delegate { ToggleNativeMenu("appearance"); });
      nativeAppearance.Font = ChromeControls.Font(13);
      nativeMore = FlatButton(new MoreButton(), "", L.T("Другие действия"), delegate { ToggleNativeMenu("more"); });
      nativeToolbar.Controls.AddRange(new Control[] { nativeSections, nativeAppearance, nativeMore });
      welcome = new Panel { Size = new Size(Px(570), Px(180)) };
      welcomeTitle = new SurfaceLabel { Text = L.T("Откройте или создайте файл"), TextAlign = ContentAlignment.MiddleCenter, Size = new Size(Px(570), Px(32)), Font = new Font("Segoe UI", 15.75f) };
      welcomeHint = new SurfaceLabel { Text = L.T("Перетащите файлы сюда или выберите их на компьютере."), TextAlign = ContentAlignment.MiddleCenter, Location = new Point(0, Px(39)), Size = new Size(Px(570), Px(26)), Font = new Font("Segoe UI", 9.75f) };
      nativeOpen = FlatButton(new ShortcutButton("Ctrl O"), L.T("Открыть файлы…"), L.T("Открыть файлы"), delegate { OpenFromNative(); });
      nativeOpen.SetBounds(Px(295), Px(86), Px(230), Px(40)); nativeOpen.FlatAppearance.BorderSize = 1;
      nativeCreate = FlatButton(new ShortcutButton("Ctrl N"), L.T("Создать новый файл"), L.T("Создать новый файл"), delegate { CreateFromNative(); });
      nativeCreate.SetBounds(Px(45), Px(86), Px(240), Px(40)); nativeCreate.FlatAppearance.BorderSize = 1;
      nativeSample = FlatButton(new QuietButton(), L.T("Посмотреть пример"), L.T("Посмотреть пример"), async delegate { samplePending = true; await EnsureBrowser(); });
      nativeSample.SetBounds(Px(195), Px(139), Px(180), Px(30));
      nativeOpen.Name = "empty-open"; nativeCreate.Name = "empty-new"; nativeSample.Name = "empty-sample";
      welcome.Name = "empty-content"; welcomeTitle.Name = "empty-title"; welcomeHint.Name = "empty-hint";
      nativeAppearance.Name = "appearance"; nativeMore.Name = "more"; nativeSections.Name = "outline-toggle";
      welcome.Controls.AddRange(new Control[] { welcomeTitle, welcomeHint, nativeCreate, nativeOpen, nativeSample });
      empty.Controls.Add(welcome); empty.Controls.Add(nativeToolbar); empty.Controls.Add(nativeTop);
      empty.DragEnter += delegate(object sender, DragEventArgs e) { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
      empty.DragDrop += async delegate(object sender, DragEventArgs e) { var paths = e.Data.GetData(DataFormats.FileDrop) as string[]; if (paths != null) { pendingFiles.AddRange(paths); await EnsureBrowser(); } };
      empty.Resize += delegate { LayoutNativeSurface(); };
      Controls.Add(empty);
      caption = new Panel(); captionButtons = new CaptionButton[3];
      string[] names = { L.T("Свернуть"), L.T("Развернуть"), L.T("Закрыть") };
      for (int i = 0; i < 3; i++) {
        var button = new CaptionButton(i) { AccessibleName = names[i], TabStop = false };
        captionButtons[i] = button; caption.Controls.Add(button);
      }
      captionButtons[0].Click += delegate { MinimizeWindow(); };
      captionButtons[1].Click += delegate { ToggleMaximize(); };
      captionButtons[2].Click += delegate { Close(); };
      Controls.Add(caption); caption.BringToFront();
      ApplyNativeTheme(); LayoutNativeSurface(); LayoutCaption();
    }
    internal void ApplyNativeLanguage() {
      if (welcome == null) return;
      nativeNewTab.Text = L.T("Создать"); nativeNewTab.AccessibleName = L.T("Создать новый файл");
      nativePlus.AccessibleName = L.T("Открыть файлы (Ctrl+O)");
      nativeSections.Text = nativeSections.AccessibleName = L.T("Разделы");
      nativeAppearance.AccessibleName = L.T("Оформление"); nativeMore.AccessibleName = L.T("Другие действия");
      welcomeTitle.Text = L.T("Откройте или создайте файл");
      welcomeHint.Text = L.T("Перетащите файлы сюда или выберите их на компьютере.");
      nativeOpen.Text = L.T("Открыть файлы…"); nativeOpen.AccessibleName = L.T("Открыть файлы");
      nativeCreate.Text = L.T("Создать новый файл"); nativeCreate.AccessibleName = L.T("Создать новый файл");
      nativeSample.Text = nativeSample.AccessibleName = L.T("Посмотреть пример");
      captionButtons[0].AccessibleName = L.T("Свернуть"); captionButtons[2].AccessibleName = L.T("Закрыть");
      LayoutCaption();
    }
    QuietButton FlatButton(QuietButton button, string text, string accessible, EventHandler action) {
      button.Text = text; button.AccessibleName = accessible; button.FlatStyle = FlatStyle.Flat;
      button.UseVisualStyleBackColor = false; button.Cursor = Cursors.Hand;
      button.FlatAppearance.BorderSize = 0; button.Click += action; return button;
    }
    void LayoutNativeSurface() {
      if (welcome == null) return;
      welcome.Location = new Point((empty.ClientSize.Width - welcome.Width) / 2, Math.Max(Px(105), (empty.ClientSize.Height - welcome.Height + Px(20)) / 2));
      int inset = ClientSize.Width <= Px(ChromeControls.NarrowWidth) ? ChromeControls.NarrowInset : ChromeControls.ToolbarInset;
      nativeSections.Left = Px(inset);
      nativeSections.Width = Px(ChromeControls.Width("outline-toggle"));
      nativeMore.SetBounds(nativeToolbar.Width - Px(inset + ChromeControls.ActionWidth), Px(ChromeControls.ButtonTop), Px(ChromeControls.ActionWidth), Px(ChromeControls.ButtonHeight));
      nativeAppearance.SetBounds(nativeToolbar.Width - Px(inset + 2*ChromeControls.ActionWidth + ChromeControls.ToolbarGap), Px(ChromeControls.ButtonTop), Px(ChromeControls.ActionWidth), Px(ChromeControls.ButtonHeight));
    }
    void LayoutCaption() {
      if (caption == null) return;
      caption.SetBounds(ClientSize.Width - Px(138), 0, Px(138), Px(45));
      captionButtons[1].AccessibleName = WindowState == FormWindowState.Maximized ? L.T("Восстановить размер") : L.T("Развернуть");
      for (int i = 0; i < captionButtons.Length; i++) { captionButtons[i].SetBounds(Px(i * 46), 0, Px(46), Px(45)); captionButtons[i].Maximized = WindowState == FormWindowState.Maximized; captionButtons[i].Invalidate(); }
      LayoutNativeSurface();
    }
    void ApplyNativeTheme() {
      if (empty == null) return;
      nativeIcon.Image = IsDark ? nativeIconDark : nativeIconLight;
      var colors = new NativeColors(IsDark);
      BackColor = colors.Surface; empty.BackColor = colors.Surface; empty.ForeColor = colors.Text;
      nativeTop.BackColor = colors.Chrome; nativeNewTab.ForeColor = colors.Muted;
      nativeSections.ForeColor = colors.Muted; nativeSections.Invalidate();
      welcomeHint.ForeColor = colors.Muted; nativeSample.ForeColor = colors.Muted;
      nativeOpen.BackColor = colors.Code;
      nativeOpen.FlatAppearance.BorderColor = colors.Line;
      nativeCreate.BackColor=nativeOpen.BackColor; nativeCreate.FlatAppearance.BorderColor=nativeOpen.FlatAppearance.BorderColor;
      foreach (var button in new[] { nativeOpen, nativeCreate, nativeNewTab, nativeSample, nativePlus, nativeAppearance, nativeMore }) {
        button.FlatAppearance.MouseOverBackColor = colors.Hover;
        button.FocusColor = colors.Accent;
        button.ShortcutColor = colors.Muted;
        button.Invalidate();
      }
      if (caption != null) { caption.BackColor = colors.Chrome; caption.ForeColor = colors.Muted; foreach (var button in captionButtons) { button.HoverColor = colors.Hover; button.Invalidate(); } }
    }
    async void OpenFromNative() { var files = ChooseOpen(); if (files == null || files.Length == 0) return; pendingFiles.AddRange(files); await EnsureBrowser(); }
    internal async void CreateFromNative() {
      if (Core!=null && UiReady) { Emit("command","new"); return; }
      pendingNew++; await EnsureBrowser();
    }
  }
  sealed class SurfaceLabel : Label {
    internal bool AlignLeft;
    internal static void Draw(Graphics graphics, string text, Font font, Rectangle bounds, Color color, Color background, bool centered) {
      if (SurfaceText.Paint(graphics,text,font,bounds,color,background,centered)) return;
      graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
      using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
      using (var brush = new SolidBrush(color)) {
        format.Alignment = centered ? StringAlignment.Center : StringAlignment.Near;
        format.LineAlignment = StringAlignment.Center;
        format.FormatFlags |= StringFormatFlags.NoWrap;
        graphics.DrawString(text, font, brush, bounds, format);
      }
    }
    protected override void OnPaint(PaintEventArgs e) { Draw(e.Graphics, Text, Font, ClientRectangle, ForeColor, Parent.BackColor, !AlignLeft); }
  }
  sealed class CaptionButton : Button {
    readonly int kind; bool hovered;
    internal bool Maximized;
    internal Color HoverColor;
    internal CaptionButton(int kind) { this.kind = kind; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e) {
      e.Graphics.Clear(hovered ? (kind == 2 ? Color.FromArgb(196, 43, 28) : HoverColor) : Parent.BackColor);
      float scale = e.Graphics.DpiX / 96f, x = Width / 2f, y = Height / 2f, r = 4 * scale;
      using (var pen = new Pen(hovered && kind == 2 ? Color.White : Parent.ForeColor, scale)) {
        if (kind == 0) e.Graphics.DrawLine(pen, x - r, y, x + r, y);
        if (kind == 1) { e.Graphics.DrawRectangle(pen, x - r, y - r, 2*r, 2*r); if (Maximized) e.Graphics.DrawLines(pen, new[] { new PointF(x-r+2*scale, y-r-2*scale), new PointF(x+r+2*scale,y-r-2*scale),new PointF(x+r+2*scale,y+r-2*scale) }); }
        if (kind == 2) { e.Graphics.DrawLine(pen,x-r,y-r,x+r,y+r); e.Graphics.DrawLine(pen,x-r,y+r,x+r,y-r); }
      }
    }
  }
}
