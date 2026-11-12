#if TESTING
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace MdReader {
  static class NativeTests {
    internal static bool Enabled;
    internal static string[] OpenFiles = new string[0];
    internal static string SavePath;
    static readonly List<string> SaveRequests = new List<string>();
    internal static string ChooseSave(string filename) { SaveRequests.Add(filename); return SavePath; }
    internal static readonly Queue<int> Answers = new Queue<int>();
    static readonly List<string> Checks = new List<string>();
    static readonly Dictionary<string, object> Measurements = new Dictionary<string, object>();
    static string TestRoot, Dist;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct ProcessEntry {
      public uint size, usage, id; public IntPtr heap; public uint module, threads, parent; public int priority; public uint flags;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string name;
    }
    [DllImport("kernel32", SetLastError=true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32", CharSet=CharSet.Unicode)] static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32", CharSet=CharSet.Unicode)] static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr instance, string path, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    internal static int Choice(string title) { if (Answers.Count == 0) throw new Exception("Unexpected dialog: " + title); return Answers.Dequeue(); }
    static void Pass(string label) { Checks.Add(label); File.AppendAllText(Path.Combine(Dist,"native-test-progress.txt"), "PASS " + label + "\r\n"); }
    static void Assert(bool condition, string label) { if (!condition) throw new Exception(label); }
    internal static async Task Run(ReaderForm form, string[] args) {
      Enabled = true; Dist = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
      TestRoot = Path.Combine(Program.DataRoot, "documents"); Directory.CreateDirectory(TestRoot);
      File.WriteAllText(Path.Combine(Dist,"native-test-progress.txt"), "Starting\r\n");
      try {
        await form.Call("language", new object[] { "ru" });
        FileTypeTests();
        form.Activate(); await Task.Delay(700);
        Assert(form.Core == null, "Empty window must not start a browser");
        Measurements["empty"] = Memory();
        CaptureNative(form, "native-empty-screen.png");
        File.WriteAllText(Path.Combine(Dist,"native-controls.txt"),String.Join("\r\n",form.Controls.Cast<Control>().Select(c=>c.GetType().Name+" "+c.Bounds+" visible="+c.Visible+" children="+c.Controls.Count)));
        Pass("Native empty window without browser processes");
        form.CreateFromNative();
        await Poll(async delegate { return form.Core!=null && form.UiReady && await JS<bool>(form,"document.getElementById('filename').textContent.startsWith('Без имени') && document.getElementById('workspace').dataset.view==='split'"); },"create from native empty window");
        Assert(form.Store.Entries.Count==1 && form.Store.Entries.Values.Single().doc.untitled && form.Store.Entries.Values.Single().doc.path==null,"New document has no file on disk");
        await form.Core.ExecuteScriptAsync("document.querySelector('.tab-item.active .tab-close').click()");
        await Poll(async delegate { await Task.Delay(1); return form.Core==null; },"close pristine native document without a prompt");
        Assert(form.Store.Entries.Count==0 && !form.Session.ShouldRestore,"Closing untouched document removes it from host and recovery session");
        Pass("Native Create button opens a pristine editor; closing it needs no prompt and leaves no recovered tab");
        FileTests();
        string file = Path.Combine(TestRoot, "Заметка.md");
        File.WriteAllText(file, "# Заметка\r\n\r\nПростой текст.\r\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(TestRoot,"Другой.md"),"# Другой\n\nВторая вкладка.", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(TestRoot,"Пример.md"),File.ReadAllText(Path.Combine(Program.CacheRoot,"ui","sample.md")),new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(TestRoot,"pixel.svg"),"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"40\"><rect width=\"100\" height=\"40\" fill=\"#789\"/></svg>");
        var watch = Stopwatch.StartNew(); form.ReceiveArguments(new[] { file });
        await Poll(async delegate { return form.Core != null && form.UiReady && await JS<bool>(form,"document.querySelector('#document h1')?.textContent==='Заметка'"); }, "first document");
        Measurements["firstDocumentMs"] = watch.ElapsedMilliseconds;
        await Task.Delay(1500); Measurements["plainDocument"] = Memory();
        Pass("Native startup and first document");
        await form.Core.ExecuteScriptAsync("window.__testRoot=" + Program.Json.Serialize(TestRoot) + ";");
        using (var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("native.test.js"))) await form.Core.ExecuteScriptAsync(reader.ReadToEnd());
        await Poll(async delegate { return await JS<bool>(form,"!!window.__testResult"); }, "UI regression suite", 240000);
        var result = await JS<Dictionary<string,object>>(form,"window.__testResult");
        Assert(Convert.ToBoolean(result["ok"]), Convert.ToString(result.ContainsKey("error") ? result["error"] : "UI regression failed"));
        Assert(Answers.Count == 0, "Unconsumed confirmation answers");
        var environment = form.Environment; var processes = environment.GetProcessInfos().Select(p => p.ProcessId).ToArray();
        await form.Core.ExecuteScriptAsync("document.querySelector('.tab-item.active .tab-close').click()");
        await Poll(async delegate { await Task.Delay(1); return form.Core == null; }, "release last tab");
        await Poll(async delegate { await Task.Delay(1); return processes.All(id => { try { return Process.GetProcessById(id).HasExited; } catch (ArgumentException) { return true; } }); }, "browser processes exit", 20000);
        Measurements["afterLastTab"] = Memory(); Pass("Last tab releases browser processes");
        CaptureNative(form, "native-empty-dark.png");
        form.ReceiveArguments(new[] { file });
        await Poll(async delegate { return form.Core != null && form.UiReady && await JS<bool>(form,"!!document.querySelector('#document h1')"); }, "reopen");
        Assert(await JS<bool>(form,"localStorage.getItem('reading-width')==='65' && document.documentElement.dataset.theme==='dark'"),"Preferences must survive browser disposal");
        Pass("Reopen and persist reading preferences/theme");
        await form.Core.ExecuteScriptAsync("document.getElementById('view-split').click()");
        await Task.Delay(150);
        await form.Core.ExecuteScriptAsync("document.getElementById('source').focus();document.getElementById('source').select()");
        await form.Core.CallDevToolsProtocolMethodAsync("Input.insertText",Program.Json.Serialize(new { text = "# Несохранённый" }));
        Answers.Enqueue(2); form.Close(); await Task.Delay(250); Assert(!form.IsDisposed, "Cancel closing dirty window");
        Assert(await JS<bool>(form,"document.getElementById('source').value==='# Несохранённый'"),"Draft preserved after canceled window close");
        Pass("Unsaved window close cancellation");
        Answers.Enqueue(1); await form.Core.ExecuteScriptAsync("document.getElementById('reload').click()");
        await Poll(async delegate { return await JS<bool>(form,"!document.body.inert && !document.querySelector('.tab-item.active').classList.contains('dirty')"); },"discard existing edits before new window-close test");
        await form.Core.ExecuteScriptAsync("document.getElementById('new').click()");
        await Poll(async delegate { return await JS<bool>(form,"document.getElementById('filename').textContent.startsWith('Без имени') && document.getElementById('source')?.editorMounted"); },"new document before window close");
        await form.Core.CallDevToolsProtocolMethodAsync("Input.insertText",Program.Json.Serialize(new {text="# Новый несохранённый"}));
        Answers.Enqueue(2); form.Close(); await Task.Delay(250);
        Assert(!form.IsDisposed && await JS<bool>(form,"document.getElementById('source').value==='# Новый несохранённый'"),"Cancel window close preserves new draft");
        SavePath=null; Answers.Enqueue(0); form.Close(); await Task.Delay(300);
        Assert(!form.IsDisposed && await JS<bool>(form,"!document.body.inert && document.querySelector('.tab-item.active').classList.contains('dirty') && document.getElementById('source').value==='# Новый несохранённый'"),"Cancel Save As during window close preserves new draft");
        Pass("New document: cancel window close and cancel Save As both retain the draft");
        File.WriteAllText(Path.Combine(Dist,"native-test-result.json"),Program.Json.Serialize(new { ok=true, checks=Checks, measurements=Measurements, root=TestRoot }));
        Answers.Enqueue(1); form.Close();
      } catch (Exception error) {
        File.WriteAllText(Path.Combine(Dist,"native-test-result.json"),Program.Json.Serialize(new { ok=false, error=error.ToString(), checks=Checks, measurements=Measurements, root=TestRoot }));
        CaptureFailure(form);
      }
    }
    static void CaptureNative(ReaderForm form, string name) {
      // The empty surface is entirely WinForms. Render it without requiring an
      // unlocked desktop or an attached RDP monitor; frame checks remain separate.
      using (var bitmap = new Bitmap(form.Width, form.Height)) {
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(Dist, name));
      }
    }
    static async void CaptureFailure(ReaderForm form) {
        if (form.Core != null) {
          try { using (var stream=File.Create(Path.Combine(Dist,"native-failure.png"))) await form.Core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream); } catch {}
          try { File.WriteAllText(Path.Combine(Dist,"native-failure-state.json"), await form.Core.ExecuteScriptAsync("({html:document.body.innerHTML,toast:document.getElementById('toast')?.textContent,errors:window.__testErrors})")); } catch {}
        }
        form.ReleaseBrowser(); form.Close();
    }
    static async Task<T> JS<T>(ReaderForm form, string expression) { return Program.Json.Deserialize<T>(await form.Core.ExecuteScriptAsync(expression)); }
    static async Task Poll(Func<Task<bool>> test, string label, int timeout=30000) {
      var watch=Stopwatch.StartNew(); while(watch.ElapsedMilliseconds<timeout) { if(await test()) return; await Task.Delay(80); } throw new Exception("Timed out: "+label);
    }
    internal static object Memory() {
      var entries=new List<ProcessEntry>(); IntPtr snapshot=CreateToolhelp32Snapshot(2,0);
      try { var entry=new ProcessEntry { size=(uint)Marshal.SizeOf(typeof(ProcessEntry)) }; if(Process32First(snapshot,ref entry)) do { entries.Add(entry); } while(Process32Next(snapshot,ref entry)); } finally { CloseHandle(snapshot); }
      var ids=new HashSet<uint> { (uint)Process.GetCurrentProcess().Id }; bool added;
      do { added=false; foreach(var entry in entries) if(ids.Contains(entry.parent) && ids.Add(entry.id)) added=true; } while(added);
      var rows=new List<object>(); long working=0, committed=0;
      // A browser child may exit between the process snapshot and these reads.
      foreach(uint id in ids) try {
        using(var process=Process.GetProcessById((int)id)) {
          process.Refresh();
          long workingSet=process.WorkingSet64, privateBytes=process.PrivateMemorySize64;
          string name=process.ProcessName;
          rows.Add(new { id, name, workingSet, privateBytes }); working+=workingSet; committed+=privateBytes;
        }
      } catch(ArgumentException) {} catch(InvalidOperationException) {}
      return new { workingSetMiB=Math.Round(working/1048576.0,1), privateMiB=Math.Round(committed/1048576.0,1), processes=rows };
    }
    static string TestPath(string relative) { string path=Path.GetFullPath(Path.Combine(TestRoot,relative)); if(!path.StartsWith(TestRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new Exception("Test path escape"); return path; }
    internal static async Task<object> Call(ReaderForm form,object[] args) {
      if(!Enabled) throw new Exception("Tests not enabled");
      string method=Convert.ToString(args[0]); var data=args.Length>1?args[1] as Dictionary<string,object>:null;
      switch(method) {
        case "pass": Pass(Convert.ToString(args[1])); return null;
        case "configure":
          if(data.ContainsKey("open")) OpenFiles=((object[])data["open"]).Select(item=>TestPath(Convert.ToString(item))).ToArray();
          if(data.ContainsKey("save")) SavePath=data["save"]==null?null:TestPath(Convert.ToString(data["save"]));
          if(data.ContainsKey("answer")) Answers.Enqueue(Convert.ToInt32(data["answer"]));
          return null;
        case "read": return File.ReadAllText(TestPath(Convert.ToString(args[1])));
        case "dialogs": return new {saveCount=SaveRequests.Count,lastSaveName=SaveRequests.LastOrDefault()};
        case "write": File.WriteAllText(TestPath(Convert.ToString(data["name"])),Convert.ToString(data["text"]),new UTF8Encoding(false)); return null;
        case "cdp": return Program.Json.DeserializeObject(await form.Core.CallDevToolsProtocolMethodAsync(Convert.ToString(data["method"]),Program.Json.Serialize(data["params"])));
        case "memory": Measurements[Convert.ToString(args[1])]=Memory(); return null;
        case "profile":
          await form.Core.CallDevToolsProtocolMethodAsync("Profiler.enable","{}");
          await form.Core.CallDevToolsProtocolMethodAsync("Profiler.start","{}");
          await Task.Delay(1500);
          File.WriteAllText(Path.Combine(Dist,"native-cpu-profile.json"),await form.Core.CallDevToolsProtocolMethodAsync("Profiler.stop","{}"));
          return null;
        case "capture": using(var output=File.Create(Path.Combine(Dist,Convert.ToString(args[1])+".png"))) await form.Core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,output); return null;
        case "resize": form.Size=new Size(Convert.ToInt32(data["width"]),Convert.ToInt32(data["height"])); return null;
        case "menu-visible":
          for(int i=0;i<100 && form.nativeMenu==null;i++) await Task.Delay(20);
          if(form.nativeMenu==null) throw new Exception("Native document menu did not open");
          var menuControl=QualityTests.Descendants(form.nativeMenu).Single(c=>c.Name==Convert.ToString(args[1]));
          form.nativeMenu.ScrollControlIntoView(menuControl); menuControl.Focus();
          var visibleBounds=form.RectangleToClient(menuControl.RectangleToScreen(menuControl.ClientRectangle));
          return form.ClientRectangle.Contains(visibleBounds) && form.ClientRectangle.Contains(form.nativeMenu.Bounds);
        case "capture-menu": QualityTests.Capture(form,Path.Combine(Dist,Convert.ToString(args[1])+".png")); return null;
        case "clipboard": return Clipboard.GetText();
        default: throw new Exception("Unknown test method");
      }
    }
    static void FileTypeTests() {
      string subtree = @"Software\Dokvi.Tests\" + Guid.NewGuid().ToString("N");
      try {
        using (var root = Registry.CurrentUser.CreateSubKey(subtree)) {
          const string choicePath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.md\UserChoice";
          using (var key = root.CreateSubKey(@"Software\Classes\.md")) key.SetValue("", "Another.Editor");
          using (var key = root.CreateSubKey(@"Software\Classes\.md\OpenWithProgids")) key.SetValue("Another.Editor", "");
          using (var key = root.CreateSubKey(@"Software\Classes\.txt")) key.SetValue("", "txtfile");
          using (var key = root.CreateSubKey(choicePath)) { key.SetValue("ProgId", "Another.Editor"); key.SetValue("Hash", "untouched"); }
          string executable = Path.Combine(TestRoot, "Тест с пробелом", "Dokvi.exe");
          string icon = Path.Combine(Program.CacheRoot, "ui", "document.ico");
          Assert(WindowsIntegration.WriteFileTypes(root, executable, icon), "Initial shell registration writes values");
          using (var key = root.OpenSubKey(@"Software\Classes\Dokvi.Markdown\DefaultIcon"))
            Assert((string)key.GetValue("") == "\"" + icon + "\",0", "Document icon is separate from application icon");
          using (var key = root.OpenSubKey(@"Software\Classes\Dokvi.Markdown\shell\open\command"))
            Assert((string)key.GetValue("") == "\"" + executable + "\" \"%1\"", "Quoted Unicode executable and file argument");
          using (var key = root.OpenSubKey(@"Software\Classes\.md")) Assert((string)key.GetValue("") == "Another.Editor", "Existing extension default is preserved");
          using (var key = root.OpenSubKey(@"Software\Classes\.md\OpenWithProgids")) Assert(key.GetValue("Another.Editor") != null && key.GetValue("Dokvi.Markdown") != null, "Open-with alternatives are preserved");
          using (var key = root.OpenSubKey(@"Software\Classes\.txt")) Assert((string)key.GetValue("") == "txtfile" && key.SubKeyCount == 0, "TXT registration is untouched");
          using (var key = root.OpenSubKey(choicePath)) Assert((string)key.GetValue("ProgId") == "Another.Editor" && (string)key.GetValue("Hash") == "untouched", "UserChoice is untouched");
          Assert(!WindowsIntegration.WriteFileTypes(root, executable, icon), "Repeated registration makes no changes");
          string moved = Path.Combine(TestRoot, "Moved", "Dokvi-2.4.1.exe");
          Assert(WindowsIntegration.WriteFileTypes(root, moved, icon), "Explicit registration updates a moved executable");
          using (var key = root.OpenSubKey(@"Software\Classes\Dokvi.Markdown\shell\open\command"))
            Assert((string)key.GetValue("") == "\"" + moved + "\" \"%1\"", "Handler follows the explicitly registered executable");
          using (var reader = new BinaryReader(File.OpenRead(icon))) {
            Assert(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1 && reader.ReadUInt16() == 9, "Nine ICO frames");
            foreach (int size in new[] { 16,20,24,32,40,48,64,128,256 }) {
              Assert(reader.ReadByte() == (size == 256 ? 0 : size) && reader.ReadByte() == (size == 256 ? 0 : size), "ICO frame dimensions");
              reader.ReadBytes(14);
              // WinForms Icon(path, 256, 256) selects 128px on .NET Framework;
              // exercise the Windows loader used for actual large shell icons.
              IntPtr handle = LoadImage(IntPtr.Zero, icon, 1, size, size, 0x10);
              Assert(handle != IntPtr.Zero, "Windows loads ICO frame " + size);
              try { using (var frame = Icon.FromHandle(handle)) using (var bitmap = frame.ToBitmap())
                Assert(bitmap.Width == size && bitmap.Height == size, "Windows decodes ICO frame " + size);
              } finally { DestroyIcon(handle); }
            }
          }
        }
        Pass("Markdown shell registration: document icon, Unicode paths, preserved defaults, repeat/update and nine ICO sizes");
      } finally { Registry.CurrentUser.DeleteSubKeyTree(subtree, false); }
    }
    static void FileTests() {
      foreach(string encoding in new[] { "utf-8","utf-16le","utf-16be","windows-1251" }) foreach(bool bom in new[] {false,true}) {
        if(encoding=="windows-1251" && bom) continue;
        string path=Path.Combine(TestRoot,encoding+"-"+bom+".md");
        var format=new Document { encoding=encoding,bom=bom,lineEnding="\r\n" };
        File.WriteAllBytes(path,FileStore.Encode("# Привет\n\nТекст.",format));
        var doc=FileStore.Read(path,encoding);
        Assert(doc.encoding==encoding && doc.bom==bom && doc.text=="# Привет\r\n\r\nТекст.","Encoding/BOM read "+encoding);
        var saved=FileStore.Write(path,doc.text+"\nПравка.",doc,doc.revision);
        Assert(saved.bom==bom && saved.lineEnding=="\r\n" && saved.text.EndsWith("\r\nПравка."),"Encoding write "+encoding);
        bool conflict=false; try { FileStore.Write(path,"oops",doc,doc.revision); } catch(ChangedException) { conflict=true; }
        Assert(conflict,"External file change must block save");
      }
      string legacy=Path.Combine(TestRoot,"legacy.md"); File.WriteAllBytes(legacy,Encoding.GetEncoding(1251).GetBytes("# Привет"));
      var legacyDoc=FileStore.Read(legacy,"auto"); Assert(legacyDoc.encoding=="windows-1251","Automatic encoding");
      bool rejected=false; try { FileStore.Write(legacy,"# Привет 🙂",legacyDoc,legacyDoc.revision); } catch(IOException) { rejected=true; }
      Assert(rejected && FileStore.Read(legacy,"auto").text=="# Привет","Unrepresentable chars preserve original");
      string outside=Path.Combine(Program.DataRoot,"outside.md"); File.WriteAllText(outside,"Outside");
      rejected=false; try { FileStore.Within(TestRoot,"../outside.md"); } catch(IOException) { rejected=true; } Assert(rejected,"Path traversal rejected");
      var store=new FileStore(); var first=store.Load(legacy); Assert(store.Load(legacy.ToUpperInvariant()).id==first.id,"Canonical duplicate detection");
      Pass("UTF-8/UTF-16/1251, BOM, CRLF, atomic saves, conflicts, directory boundary, duplicate paths");
    }
  }
}
#endif
