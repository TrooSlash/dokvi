using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MdReader {
  sealed partial class ReaderForm : Form {
    internal const string Origin = "https://md-reader.invalid";
    internal readonly FileStore Store = new FileStore();
    internal readonly SessionStore Session = new SessionStore(Program.DataRoot);
    readonly System.Threading.SemaphoreSlim sessionGate = new System.Threading.SemaphoreSlim(1, 1);
    bool sessionRestored;
    internal WebView2 View;
    internal CoreWebView2Environment Environment;
    internal bool UiReady;
    internal readonly Dictionary<string, string> Preferences = new Dictionary<string, string>();
    readonly HashSet<string> preferenceKeys = new HashSet<string>(new[] { "theme", "font-size", "reading-layout", "reading-width", "editor-split", "language" });
    readonly List<string> pendingFiles = new List<string>();
    Panel empty, caption;
    bool allowClose, creating, samplePending;
    int pendingNew;
    string activeId, pendingAction;
    Document sampleDocument;
    Task browserStopped = Task.FromResult(true);
    string browserLanguage;
    Rectangle normalWindowBounds;
    FormWindowState stateBeforeMinimize = FormWindowState.Normal;
    readonly Timer settingsTimer = new Timer { Interval = 350 };
    bool settingsDirty;
    internal bool IsDark { get { string theme; return Preferences.TryGetValue("theme", out theme) && theme == "dark"; } }
    internal string ProfileDirectory { get { return Path.Combine(Program.DataRoot, "WebView2"); } }
    internal CoreWebView2 Core { get { return View == null ? null : View.CoreWebView2; } }
    [DllImport("user32")] static extern bool ReleaseCapture();
    [DllImport("user32")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] struct PointI { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct MinMaxInfo { public PointI reserved, maxSize, maxPosition, minTrack, maxTrack; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int size; public NativeRect monitor, work; public uint flags; }

    internal ReaderForm(string[] arguments) {
      Text = "Dokvi"; Size = new Size(1120, 820); MinimumSize = new Size(620, 480);
      StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.None;
      AutoScaleMode = AutoScaleMode.Dpi; DoubleBuffered = true;
      Icon = new Icon(Path.Combine(Program.CacheRoot, "ui", "icon.ico"));
      try {
        string file = Path.Combine(Program.DataRoot, "settings.json");
        if (File.Exists(file)) foreach (var pair in Program.Json.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))) if (preferenceKeys.Contains(pair.Key)) Preferences[pair.Key] = pair.Value;
      } catch {}
      string language; if (Preferences.TryGetValue("language", out language)) L.SetPreference(language);
      BuildNativeSurface();
      var resizeInput = new ResizeInputFilter(this);
      Application.AddMessageFilter(resizeInput);
      Disposed += delegate { Application.RemoveMessageFilter(resizeInput); };
      settingsTimer.Tick += delegate { settingsTimer.Stop(); try { FlushPreferences(); } catch (Exception error) { Emit("error", L.T("Не удалось сохранить настройки: ") + error.Message); } };
      pendingFiles.AddRange(FileArguments(arguments));
      Shown += async delegate {
#if TESTING
        if (arguments.Contains("--test-unhandled-exception")) {
          BeginInvoke(new Action(delegate { throw new InvalidOperationException("Intentional UI exception: regression guard"); })); return;
        }
        if (arguments.Any(argument => argument.StartsWith("--document-timing="))) { await RunDocumentTiming(arguments); return; }
        if (arguments.Any(argument => argument.StartsWith("--benchmark="))) { await BenchmarkTests.Run(this, arguments); return; }
        if (arguments.Contains("--shell-probe")) ShellTests.Observe(this);
        if (arguments.Any(argument => argument.StartsWith("--language-phase="))) { await LanguageTests.Run(this, arguments); return; }
        if (arguments.Any(argument => argument.StartsWith("--session-phase="))) { await SessionTests.Run(this, arguments); return; }
        if (arguments.Contains("--test-window")) { await WindowTests.Run(this); return; }
        if (arguments.Contains("--test-click")) { await ClickTests.Run(this); return; }
        if (arguments.Contains("--test-click-taskbar")) { await ClickTests.Run(this, true); return; }
        if (arguments.Contains("--test-startup")) { await StartupTests.Run(this); return; }
        if (arguments.Contains("--test-resize")) { await ResizeTests.Run(this); return; }
        if (arguments.Contains("--test-shortcuts")) { await ShortcutTests.Run(this); return; }
        if (arguments.Contains("--test-input-focus")) { await InputFocusTests.Run(this); return; }
        if (arguments.Contains("--test-taskbar-focus")) { await TaskbarFocusTests.Run(this); return; }
        if (arguments.Contains("--test-quality")) { await QualityTests.Run(this); return; }
        if (arguments.Contains("--test-native-menu")) { await NativeMenuTests.Run(this); return; }
        if (arguments.Contains("--test-button-states")) { await ButtonStateTests.Run(this); return; }
        if (arguments.Contains("--test-keyboard-navigation")) { await KeyboardNavigationTests.Run(this); return; }
        if (arguments.Contains("--test-dialog-keyboard")) { await DialogKeyboardTests.Run(this); return; }
        if (arguments.Contains("--test-table-layout")) { await TableLayoutTests.Run(this); return; }
        if (arguments.Contains("--test-pdf-tables")) { await PdfTableTests.Run(this); return; }
        if (arguments.Contains("--test-pdf-content")) { await PdfContentTests.Run(this); return; }
        if (arguments.Contains("--test-frame")) { await FrameTests.Run(this); return; }
        if (arguments.Contains("--test-transition")) { await TransitionTests.Run(this); return; }
        if (arguments.Contains("--test-transition-repeat")) { await TransitionTests.Run(this, true); return; }
        if (arguments.Any(argument => argument.StartsWith("--test"))) { await NativeTests.Run(this, arguments); return; }
#endif
        if (pendingFiles.Count > 0 || Session.ShouldRestore) await EnsureBrowser();
      };
      FormClosing += delegate(object sender, FormClosingEventArgs e) {
        if (!allowClose && UiReady && Core != null) { PrepareWindowCommand(); e.Cancel = true; Emit("close-request", null); }
      };
      FormClosed += delegate { settingsTimer.Dispose(); try { FlushPreferences(); } catch {} if (View != null) View.Dispose(); };
      Resize += delegate {
        LayoutCaption();
        if (Core != null) {
          try { Core.MemoryUsageTargetLevel = WindowState == FormWindowState.Minimized ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal; } catch {}
        }
      };
    }
    internal static IEnumerable<string> FileArguments(string[] arguments) {
      return arguments.Where(argument => !argument.StartsWith("--") && FileStore.TextExtensions.Contains(Path.GetExtension(argument)));
    }
    internal async void ReceiveArguments(string[] arguments) {
      if (WindowState == FormWindowState.Minimized) RestoreWindow();
      Show(); Activate();
      var files = FileArguments(arguments).ToArray();
      if (Core == null || !UiReady) { pendingFiles.AddRange(files); if (files.Length > 0) await EnsureBrowser(); }
      else if (files.Length > 0) Emit("documents", Store.LoadMany(files));
    }
    internal async Task EnsureBrowser(string action = null) {
      CloseNativeMenu(false);
      if (action != null) pendingAction = action;
      if (Core != null && UiReady) { if (pendingAction != null) { Emit("ui-action", pendingAction); pendingAction = null; } return; }
      if (creating || View != null) return;
      creating = true;
      try {
        // WebView2 cannot reuse a live profile with different environment languages.
        // Disposal is asynchronous; wait for the previous browser only when changing it.
        if (browserLanguage != null && browserLanguage != L.Language && !browserStopped.IsCompleted) {
          if (await Task.WhenAny(browserStopped, Task.Delay(10000)) != browserStopped)
            throw new IOException(L.T("Просмотр ещё закрывается. Повторите открытие файла через несколько секунд."));
        }
        string runtime = CoreWebView2Environment.GetAvailableBrowserVersionString();
        var options = new CoreWebView2EnvironmentOptions();
        options.Language = L.Language == "ru" ? "ru-RU" : "en-US";
        options.AreBrowserExtensionsEnabled = false;
        Environment = await CoreWebView2Environment.CreateAsync(null, ProfileDirectory, options);
        var stopped = new TaskCompletionSource<bool>();
        Environment.BrowserProcessExited += delegate { stopped.TrySetResult(true); };
        browserStopped = stopped.Task; browserLanguage = L.Language;
        // A hidden WebView does not paint. Let it render behind the native
        // welcome surface, which stays in front until the editor is ready.
        Color background = IsDark ? Color.FromArgb(32, 33, 34) : Color.White;
        View = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = background };
        Controls.Add(View); empty.BringToFront(); caption.BringToFront();
        var controllerOptions = Environment.CreateCoreWebView2ControllerOptions();
        controllerOptions.AllowHostInputProcessing = true;
        controllerOptions.IsInPrivateModeEnabled = true;
        controllerOptions.DefaultBackgroundColor = background;
        await View.EnsureCoreWebView2Async(Environment, controllerOptions);
        var settings = Core.Settings;
        settings.IsStatusBarEnabled = false; settings.IsZoomControlEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false; settings.AreDevToolsEnabled = false;
        settings.AreHostObjectsAllowed = false; settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsGeneralAutofillEnabled = false; settings.IsPasswordAutosaveEnabled = false;
        settings.IsPinchZoomEnabled = false; settings.IsSwipeNavigationEnabled = false;
        settings.IsNonClientRegionSupportEnabled = true;
        Core.SetVirtualHostNameToFolderMapping("md-reader.invalid", Path.Combine(Program.CacheRoot, "ui"), CoreWebView2HostResourceAccessKind.DenyCors);
        Core.AddWebResourceRequestedFilter("https://md-assets.invalid/*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        Core.WebResourceRequested += AssetRequest;
        Core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; e.Handled = true; };
        Core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e) {
          Uri uri;
          if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) || uri.GetLeftPart(UriPartial.Authority) != Origin || uri.AbsolutePath != "/index.html") e.Cancel = true;
        };
        Core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; if (e.IsUserInitiated) { try { OpenExternal(e.Uri); } catch {} } };
        Core.DownloadStarting += delegate(object sender, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; };
        Core.WebMessageReceived += MessageReceived;
        Core.ProcessFailed += delegate(object sender, CoreWebView2ProcessFailedEventArgs e) {
          if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
            MessageBox.Show(this, L.T("Процесс просмотра завершился. Сохранённые файлы не изменены. Закройте и снова откройте программу."), "Dokvi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        string bridge = File.ReadAllText(Path.Combine(Program.CacheRoot, "ui", "native-bridge.js"));
        await Core.AddScriptToExecuteOnDocumentCreatedAsync("window.__nativePreferences=" + Program.Json.Serialize(Preferences) + ";window.__nativeSystemLanguage=" + Program.Json.Serialize(L.SystemLanguage) + ";\n" + bridge);
#if TESTING
        if (documentTiming) await InstallDocumentTimingProbe();
#endif
        Core.Navigate(Origin + "/index.html");
      } catch (WebView2RuntimeNotFoundException) {
        if (View != null && !IsDisposed) ReleaseBrowser();
        if (MessageBox.Show(this, L.T("Для просмотра документов нужен Microsoft Edge WebView2 Runtime. Открыть официальный сайт установки?"), L.T("Компонент Windows"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
          OpenExternal("https://developer.microsoft.com/microsoft-edge/webview2/");
        pendingFiles.Clear();
      } catch (Exception error) {
        if (View != null && !IsDisposed) ReleaseBrowser();
#if TESTING
        if (NativeTests.Enabled) throw;
#endif
        MessageBox.Show(this, error.Message, L.T("Не удалось открыть просмотр"), MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
      finally { creating = false; }
    }
    async void MessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e) {
      if (e.Source != Origin + "/index.html") return;
      string requestId = null;
      try {
        var request = Program.Json.DeserializeObject(e.WebMessageAsJson) as Dictionary<string, object>;
        if (request == null) return;
        requestId = Convert.ToString(request["id"]);
        if (requestId.Length > 100) return;
        string method = Convert.ToString(request["method"]);
        var args = request.ContainsKey("args") ? request["args"] as object[] : null;
        object result = await Call(method, args ?? new object[0], e);
        if (Core != null) Core.PostWebMessageAsJson(Program.Json.Serialize(new { reply = requestId, value = result }));
        if (method == "ready" && UiReady && pendingAction != null) { Emit("ui-action", pendingAction); pendingAction = null; }
      } catch (Exception error) {
        if (Core != null && requestId != null) Core.PostWebMessageAsJson(Program.Json.Serialize(new { reply = requestId, error = error.Message }));
      }
    }
    static string Arg(object[] args, int index) { return index < args.Length && args[index] != null ? Convert.ToString(args[index]) : null; }
    internal async Task<object> Call(string method, object[] args, CoreWebView2WebMessageReceivedEventArgs message = null) {
      switch (method) {
        case "chrome-controls": return ChromeControls.ForDocument(Convert.ToDouble(args[0]));
        case "menu":
          string menuKind = Arg(args, 0);
          if (UiReady && (menuKind == "more" || menuKind == "appearance")) ToggleNativeMenu(menuKind, Program.Json.Deserialize<DocumentMenuState>(Arg(args, 1)));
          return null;
        case "menu-update":
          if (nativeMenu != null && nativeMenuDocument != null && Convert.ToInt32(args[0]) == nativeMenuToken) {
            nativeMenuDocument = Program.Json.Deserialize<DocumentMenuState>(Arg(args, 1));
            if (Arg(args, 2) != "reading-width") RebuildNativeMenu(Arg(args, 2));
          }
          return null;
        case "menu-close": CloseNativeMenu(args.Length > 0 && Convert.ToBoolean(args[0])); return null;
        case "menu-focus":
          if (nativeMenu != null) nativeMenu.HandleNavigation(args.Length > 0 && Convert.ToBoolean(args[0]) ? Keys.End : Keys.Home);
          return null;
        case "initial":
          var first = pendingFiles.ToArray(); pendingFiles.Clear();
          var batch = sessionRestored ? new DocumentBatch() : Session.Restore(Store);
          sampleDocument = batch.documents.FirstOrDefault(doc => doc.demo) ?? sampleDocument;
          sessionRestored = true;
          var opened = Store.LoadMany(first);
          foreach (var doc in opened.documents) if (!batch.documents.Any(existing => existing.id == doc.id)) batch.documents.Add(doc);
          batch.errors.AddRange(opened.errors);
          if (opened.documents.Count > 0) batch.activeId = opened.documents.Last().id;
          if (samplePending) { samplePending = false; batch.documents.Add(Demo()); }
          while (pendingNew>0) { pendingNew--; batch.documents.Add(Store.CreateDocument()); }
          return batch;
        case "ready":
          // Inputs received while the first document is loading must not be lost
          // or race its asynchronous editor setup.
          if (pendingFiles.Count > 0 || pendingNew > 0 || samplePending) return await Call("initial", new object[0]);
          if (View == null || View.IsDisposed) return null;
          UiReady = true;
          View.BringToFront(); caption.BringToFront(); empty.Visible = false; View.Focus();
          return null;
        case "new": return Store.CreateDocument();
        case "sample": return Demo();
        case "session":
          var checkpoint = Program.Json.Deserialize<SessionManifest>(Program.Json.Serialize(args[0]));
          await sessionGate.WaitAsync();
          try { await Session.Checkpoint(Store, checkpoint.tabs.Any(tab => tab.id == "sample") ? Demo() : null, checkpoint); }
          finally { sessionGate.Release(); }
          return null;
        case "open":
          var paths = ChooseOpen();
          return paths == null ? null : Store.LoadMany(paths);
        case "drop":
          if (message == null) throw new IOException(L.T("Не удалось получить перетаскиваемые файлы."));
          var dropped = new List<string>();
          foreach (object additional in message.AdditionalObjects) {
            var file = additional as CoreWebView2File;
            if (file == null || String.IsNullOrEmpty(file.Path)) throw new IOException(L.T("Не удалось получить путь файла."));
            dropped.Add(file.Path);
          }
          return Store.LoadMany(dropped);
        case "activate":
          activeId = Arg(args, 0);
          Text = activeId == null ? "Dokvi" : (activeId == "sample" ? Demo().name : Store.Get(activeId).doc.name) + " — Dokvi";
          return null;
        case "close":
          if (Arg(args, 0) == "sample") sampleDocument = null; else Store.Entries.Remove(Arg(args, 0));
          if (args.Length > 1 && Convert.ToBoolean(args[1]) && Store.Entries.Count == 0 && pendingFiles.Count == 0 && pendingNew == 0 && !samplePending) {
            UiReady = false; activeId = null; ShowNativeSurface(args.Length>2?(bool?)Convert.ToBoolean(args[2]):null);
            // Reply before disposing the WebView that delivered this message.
            BeginInvoke(new Action(async delegate {
              if (IsDisposed) return;
              ReleaseBrowser();
              if (pendingFiles.Count > 0 || pendingNew > 0 || samplePending || pendingAction != null) await EnsureBrowser();
            }));
            return true;
          }
          return false;
        case "confirm-unsaved":
          return new[] { "save", "discard", "cancel" }[Choice(L.T("Несохранённые изменения"), L.T("Сохранить изменения в «{0}»?", Arg(args, 0)), L.T("Сохранить"), L.T("Не сохранять"), L.T("Отмена"))];
        case "finish-close": allowClose = true; BeginInvoke(new Action(Close)); return null;
        case "save": return SaveDocument(Arg(args, 0), Arg(args, 1), args.Length > 2 && Convert.ToBoolean(args[2]));
        case "reload":
          var entry = Store.Get(Arg(args, 0));
          if (entry.doc.untitled) throw new IOException(L.T("Сначала сохраните новый файл."));
          return Store.Load(entry.doc.path, Arg(args, 1) ?? "auto", entry.root, true);
        case "link":
          string href = Arg(args, 1);
          if (String.IsNullOrEmpty(href) || href.Length > 8192) throw new IOException(L.T("Недопустимая ссылка."));
          Uri external;
          if (Uri.TryCreate(href, UriKind.Absolute, out external)) { OpenExternal(href); return null; }
          var source = Store.Get(Arg(args, 0));
          if (source.doc.untitled) throw new IOException(L.T("Сохраните новый файл, чтобы открывать ссылки на соседние файлы."));
          string clean = Uri.UnescapeDataString(href.Split('#')[0].Split('?')[0]);
          return Store.Load(FileStore.Within(source.root, Path.Combine(source.doc.relativeDirectory, clean)), "auto", source.root);
        case "copy":
          string copy = Arg(args, 0);
          if (!String.IsNullOrEmpty(copy) && copy.Length < 5000000) Clipboard.SetText(copy);
          return null;
        case "theme":
          SavePreference("theme", Arg(args, 0));
          ApplyNativeTheme();
          return null;
        case "language":
          string preference = Arg(args, 0);
          if (preference != "ru" && preference != "en" && preference != "system") throw new IOException("Invalid language");
          SavePreference("language", preference); L.SetPreference(preference); ApplyNativeLanguage(); FlushPreferences();
          return L.Language;
        case "settings": SavePreference(Arg(args, 0), Arg(args, 1)); return null;
        case "default-apps": WindowsIntegration.OpenDefaultApps(); return null;
        case "idle":
          await Task.Delay(120);
          if (UiReady && activeId == null && Store.Entries.Count == 0 && pendingFiles.Count == 0) ReleaseBrowser(args.Length>0?(bool?)Convert.ToBoolean(args[0]):null);
          return null;
        case "pdf":
          string id = Arg(args, 0);
          string name = id == "sample" ? Demo().name : Store.Get(id).doc.name;
          string output = ChooseSave(Path.ChangeExtension(name, ".pdf"), true);
          if (output == null) return null;
          var print = Environment.CreatePrintSettings();
          print.ShouldPrintBackgrounds = true; print.ShouldPrintHeaderAndFooter = false;
          print.PageWidth = 8.2677; print.PageHeight = 11.6929;
          print.MarginTop = .5; print.MarginBottom = .5; print.MarginLeft = .55; print.MarginRight = .55;
          if (await Core.ExecuteScriptAsync("!!document.querySelector('#document tr > :is(th,td):nth-child(9)')") == "true")
            print.Orientation = CoreWebView2PrintOrientation.Landscape;
          double pageWidth = print.Orientation == CoreWebView2PrintOrientation.Landscape ? print.PageHeight : print.PageWidth;
          await Core.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('dokvi-before-pdf',{detail:" + Program.Json.Serialize(new { width = (pageWidth - print.MarginLeft - print.MarginRight) * 96 }) + "}))");
          if (!await Core.PrintToPdfAsync(output, print)) throw new IOException(L.T("Не удалось сохранить PDF."));
          return output;
#if TESTING
        case "test": return await NativeTests.Call(this, args);
#endif
        default: throw new IOException(L.T("Неизвестная команда."));
      }
    }
    internal Document Demo() {
      if (sampleDocument != null) return sampleDocument;
      string text = File.ReadAllText(Path.Combine(Program.CacheRoot, "ui", L.Language == "ru" ? "sample.md" : "sample.en.md"));
      return sampleDocument = new Document { id = "sample", text = text, name = L.T("Пример.md"), encoding = "utf-8", bytes = Encoding.UTF8.GetByteCount(text), demo = true };
    }
    Document SaveDocument(string id, string text, bool saveAs) {
      Entry entry = id == "sample" ? null : Store.Get(id);
      string filename = entry == null ? null : entry.doc.path;
      var format = entry == null ? new Document { encoding = "utf-8", lineEnding = "\n" } : entry.doc;
      if (saveAs || filename == null) {
        filename = ChooseSave(filename ?? (entry == null ? Demo().name : entry.doc.name), false);
        if (filename == null) return null;
        format = new Document { encoding = "utf-8", lineEnding = entry == null ? "\n" : entry.doc.lineEnding };
      }
      filename = File.Exists(filename) ? FileStore.Canonical(filename) : Path.GetFullPath(filename);
      bool same = entry != null && String.Equals(entry.doc.path, filename, StringComparison.OrdinalIgnoreCase);
      if (Store.Entries.Values.Any(other => other != entry && String.Equals(other.doc.path, filename, StringComparison.OrdinalIgnoreCase)))
        throw new IOException(L.T("Этот файл уже открыт в другой вкладке. Выберите другое имя."));
      string revision = same ? entry.doc.revision : FileStore.Revision(filename);
      Document content;
      try { content = FileStore.Write(filename, text, format, revision); }
      catch (ChangedException) {
        int answer = Choice(L.T("Файл изменён на диске"), L.T("«{0}» изменён другой программой.", Path.GetFileName(filename)), L.T("Сохранить как…"), L.T("Перезаписать"), L.T("Отмена"));
        if (answer == 2) return null;
        if (answer == 0) return SaveDocument(id, text, true);
        content = FileStore.Write(filename, text, format, FileStore.Revision(filename));
      }
      if (id == "sample") sampleDocument = null;
      return Store.Register(content, entry == null ? Guid.NewGuid().ToString("N") : id, same ? entry.root : Path.GetDirectoryName(filename));
    }
    void AssetRequest(object sender, CoreWebView2WebResourceRequestedEventArgs e) {
      try {
        var uri = new Uri(e.Request.Uri);
        var query = HttpUtility.ParseQueryString(uri.Query);
        var entry = Store.Get(query["v"]);
        string filename = FileStore.Within(entry.root, Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')));
        if (!FileStore.ImageExtensions.Contains(Path.GetExtension(filename))) throw new IOException();
        if (new FileInfo(filename).Length > FileStore.Limit) throw new IOException();
        var mime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { ".svg", "image/svg+xml" }, { ".png", "image/png" }, { ".jpg", "image/jpeg" }, { ".jpeg", "image/jpeg" }, { ".gif", "image/gif" }, { ".webp", "image/webp" }, { ".avif", "image/avif" }, { ".bmp", "image/bmp" }, { ".ico", "image/x-icon" } };
        e.Response = Environment.CreateWebResourceResponse(new MemoryStream(File.ReadAllBytes(filename)), 200, "OK", "Content-Type: " + mime[Path.GetExtension(filename)] + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff");
      } catch { e.Response = Environment.CreateWebResourceResponse(new MemoryStream(), 403, "Forbidden", "Content-Type: text/plain"); }
    }
    internal void Emit(string name, object value) {
      if (Core != null) Core.PostWebMessageAsJson(Program.Json.Serialize(new { eventName = name, value = value }));
    }
    void ShowNativeSurface(bool? keyboardFocus=null) {
      if(keyboardFocus.HasValue) QuietButton.KeyboardFocus=keyboardFocus.Value;
      CloseNativeMenu(false);
      ApplyNativeTheme(); empty.Visible = true; empty.BringToFront(); caption.BringToFront();
      // Paint the replacement before removing the browser's child windows.
      empty.Update(); caption.Update();
    }
    internal void ReleaseBrowser(bool? keyboardFocus=null) {
      UiReady = false; activeId = null;
      ShowNativeSurface(keyboardFocus);
      if (View != null) { Controls.Remove(View); View.Dispose(); View = null; }
      Environment = null; Text = "Dokvi";
    }
    void SavePreference(string key, string value) {
      if (key == null || !preferenceKeys.Contains(key) || value == null || value.Length > 100) return;
      string previous; if (Preferences.TryGetValue(key, out previous) && previous == value) return;
      Preferences[key] = value;
      settingsDirty = true; settingsTimer.Stop(); settingsTimer.Start();
    }
    void FlushPreferences() {
      if (!settingsDirty) return;
      string file = Path.Combine(Program.DataRoot, "settings.json"), temporary = file + ".tmp";
      File.WriteAllText(temporary, Program.Json.Serialize(Preferences), new UTF8Encoding(false));
      if (File.Exists(file)) File.Replace(temporary, file, null); else File.Move(temporary, file);
      settingsDirty = false;
    }
    static void OpenExternal(string value) {
      Uri uri;
      if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || !new[] { "http", "https", "mailto" }.Contains(uri.Scheme)) throw new IOException(L.T("Недопустимая ссылка."));
      Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
    internal string[] ChooseOpen() {
#if TESTING
      if (NativeTests.Enabled) return NativeTests.OpenFiles;
#endif
      using (var dialog = new OpenFileDialog { Title = L.T("Открыть Markdown"), Multiselect = true, Filter = L.T("Markdown и текст|*.md;*.markdown;*.mdown;*.mkd;*.txt"), CheckFileExists = true })
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileNames : null;
    }
    internal string ChooseSave(string filename, bool pdf) {
#if TESTING
      if (NativeTests.Enabled) return NativeTests.ChooseSave(filename);
#endif
      using (var dialog = new SaveFileDialog { Title = pdf ? L.T("Сохранить PDF") : L.T("Сохранить файл"), FileName = filename, Filter = pdf ? "PDF|*.pdf" : L.T("Markdown|*.md;*.markdown;*.mdown;*.mkd|Текст|*.txt"), DefaultExt = pdf ? "pdf" : "md", AddExtension = true, OverwritePrompt = true })
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }
    internal int Choice(string title, string message, params string[] options) {
#if TESTING
      if (NativeTests.Enabled) return NativeTests.Choice(title);
#endif
      using (var dialog = new Form { Text = title, ClientSize = new Size(500, 145), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, AutoScaleMode = AutoScaleMode.Dpi, Font = new Font("Segoe UI", 9) }) {
        var label = new Label { Text = message, Location = new Point(20, 20), Size = new Size(460, 62) };
        dialog.Controls.Add(label);
        int answer = 2;
        for (int i = 0; i < options.Length; i++) {
          int index = i;
          var button = new Button { Text = options[i], Size = new Size(145, 30), Location = new Point(20 + 155 * i, 95) };
          button.Click += delegate { answer = index; dialog.DialogResult = DialogResult.OK; };
          dialog.Controls.Add(button);
          if (i == 0) dialog.AcceptButton = button;
          if (i == 2) { dialog.CancelButton = button; button.DialogResult = DialogResult.Cancel; }
        }
        dialog.ShowDialog(this); return answer;
      }
    }
    internal void MoveWindow() { ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); }
    void PrepareWindowCommand() {
      // Button.Click runs before WinForms releases mouse capture. Windows
      // ignores SC_MINIMIZE / SC_MAXIMIZE while that capture is still held.
      ReleaseCapture();
      // Windows restores the managed active control after taskbar activation.
      // A caption button must not become the editor's default Enter action.
      if (ActiveControl is CaptionButton) ActiveControl = UiReady ? View : null;
    }
    void WindowCommand(int command) {
      PrepareWindowCommand();
      if ((command==0xF020 || command==0xF030) && WindowState==FormWindowState.Normal) normalWindowBounds=Bounds;
      var restore=normalWindowBounds;
      // Keep WinForms' cached state and the native state in sync, including
      // after the WebView controller and its window hooks have been disposed.
      FormWindowState next;
      if (command==0xF020) {
        if (WindowState!=FormWindowState.Minimized) stateBeforeMinimize=WindowState;
        next=FormWindowState.Minimized;
      } else if (command==0xF030) next=FormWindowState.Maximized;
      else next=WindowState==FormWindowState.Minimized ? stateBeforeMinimize : FormWindowState.Normal;
      // Supply restored bounds as outer-window coordinates before leaving
      // maximized/minimized mode, so WinForms does not add its standard frame.
      if (next==FormWindowState.Normal && WindowState!=FormWindowState.Normal && !restore.IsEmpty) Bounds=restore;
      WindowState=next;
      // WinForms includes the removed standard frame in its restored size.
      if (command==0xF120 && WindowState==FormWindowState.Normal && !restore.IsEmpty) Bounds=restore;
      LayoutCaption();
    }
    internal void ToggleMaximize() { WindowCommand(WindowState == FormWindowState.Maximized ? 0xF120 : 0xF030); }
    internal void MinimizeWindow() { WindowCommand(0xF020); }
    internal void RestoreWindow() { WindowCommand(0xF120); }
    protected override CreateParams CreateParams {
      get { var parameters = base.CreateParams; parameters.Style |= 0x00040000 | 0x00080000 | 0x00010000 | 0x00020000; parameters.Style &= ~0x00C00000; return parameters; }
    }
    protected override bool ProcessCmdKey(ref Message message, Keys keys) {
      if (!QuietButton.KeyboardFocus) { QuietButton.KeyboardFocus=true; if (ActiveControl != null) ActiveControl.Invalidate(); }
      if (nativeMenu != null && keys == Keys.Escape) { CloseNativeMenu(true); return true; }
      if (nativeMenu != null && NativeMenuShortcut(keys)) return true;
      if (nativeMenu != null && nativeMenu.HandleNavigation(keys)) return true;
      if (Core == null) {
        if (keys == (Keys.Control | Keys.N)) { CreateFromNative(); return true; }
        if (keys == (Keys.Control | Keys.O)) { OpenFromNative(); return true; }
        if (keys == Keys.F1) { var task = EnsureBrowser("help"); return true; }
      }
      return base.ProcessCmdKey(ref message, keys);
    }
    protected override bool ProcessDialogKey(Keys keyData) {
      // Native WebView focus can move without clearing WinForms' last default button.
      // Let the browser handle Enter/Tab/Escape while focus is inside its child window.
      if (View != null && View.ContainsFocus) return false;
      return base.ProcessDialogKey(keyData);
    }
    protected override void WndProc(ref Message message) {
      if (message.Msg == 0x112) {
        int command=(int)message.WParam & 0xFFF0;
        if (command==0xF020 || command==0xF030 || command==0xF120) {
          WindowCommand(command); message.Result=IntPtr.Zero; return;
        }
      }
      if (message.Msg == 0x83 && message.WParam != IntPtr.Zero) { message.Result = IntPtr.Zero; return; }
      // The whole window is client area. DefWindowProc otherwise draws a
      // classic caption over our tab bar after activation or DrawMenuBar.
      if (message.Msg == 0x85) { message.Result = IntPtr.Zero; return; }
      if (message.Msg == 0x86 && WindowState != FormWindowState.Minimized) { message.Result = new IntPtr(1); return; }
      if (message.Msg == 0x24) {
        var data = (MinMaxInfo)Marshal.PtrToStructure(message.LParam, typeof(MinMaxInfo));
        var info = new MonitorInfo { size = Marshal.SizeOf(typeof(MonitorInfo)) };
        if (GetMonitorInfo(MonitorFromWindow(Handle, 2), ref info)) {
          data.maxPosition.x = info.work.left - info.monitor.left; data.maxPosition.y = info.work.top - info.monitor.top;
          data.maxSize.x = info.work.right - info.work.left; data.maxSize.y = info.work.bottom - info.work.top;
          data.minTrack.x = MinimumSize.Width; data.minTrack.y = MinimumSize.Height;
          Marshal.StructureToPtr(data, message.LParam, false); message.Result = IntPtr.Zero; return;
        }
      }
      base.WndProc(ref message);
      if (message.Msg == 0x84 && WindowState == FormWindowState.Normal && (int)message.Result == 1) {
        long position = message.LParam.ToInt64();
        var point = PointToClient(new Point((short)(position & 65535), (short)((position >> 16) & 65535)));
        message.Result = new IntPtr(ResizeHit(point));
      }
    }
  }
}
