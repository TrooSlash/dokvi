#if TESTING
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace MdReader {
  // Samples the actual desktop while the UI thread creates/disposes WebView2;
  // DOM screenshots alone cannot catch an unpainted native child window.
  static class TransitionTests {
    static readonly List<object> Checks = new List<object>();
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static async Task Wait(Func<bool> ready) {
      var watch = Stopwatch.StartNew();
      while (!ready()) { if (watch.ElapsedMilliseconds > 25000) throw new Exception("Transition timed out"); await Task.Delay(20); }
    }
    static IEnumerable<Control> Descendants(Control root) {
      foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    static void Screenshot(ReaderForm form, string path) {
      using (var bitmap = new Bitmap(form.Width, form.Height)) {
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(form.Location, Point.Empty, form.Size);
        bitmap.Save(path);
      }
    }
    static async Task CompareIcons(ReaderForm form,string folder,int cycle,Dictionary<string,Rectangle> nativeButtons) {
      var data=Program.Json.Deserialize<Dictionary<string,object>>(await form.Core.ExecuteScriptAsync(@"Object.fromEntries([...document.querySelectorAll('[data-chrome-icon]')].map(el=>{const r=el.getBoundingClientRect(),b=el.parentElement.getBoundingClientRect();return [el.dataset.chromeIcon,{x:r.x,y:r.y,width:r.width,height:r.height,buttonX:b.x,buttonY:b.y}]}))"));
      using(var before=new Bitmap(Path.Combine(folder,cycle+"-empty.png")))
      using(var after=new Bitmap(Path.Combine(folder,cycle+"-editor.png"))) {
        double scale=form.DeviceDpi/96.0;
        foreach(var pair in nativeButtons) {
          var d=(Dictionary<string,object>)data[pair.Key]; var b=pair.Value;
          int size=(int)Math.Round(Convert.ToDouble(d["width"])*scale);
          var actual=new Rectangle((int)Math.Round(Convert.ToDouble(d["x"])*scale),(int)Math.Round(Convert.ToDouble(d["y"])*scale),size,size);
          int left=pair.Key=="panel"?(int)Math.Round(8*scale):(b.Width-size+1)/2;
          int top=(b.Height-size+1)/2;
          var expected=new Rectangle(b.X+left,b.Y+top,size,size);
          using(var native=before.Clone(expected,before.PixelFormat))
          using(var document=after.Clone(actual,after.PixelFormat)) {
            native.Save(Path.Combine(folder,cycle+"-"+pair.Key+"-native.png")); document.Save(Path.Combine(folder,cycle+"-"+pair.Key+"-document.png"));
            int maxDelta=0, changed=0;
            for(int y=0;y<size;y++) for(int x=0;x<size;x++) {
              var a=native.GetPixel(x,y);var c=document.GetPixel(x,y);
              int delta=Math.Max(Math.Abs(a.R-c.R),Math.Max(Math.Abs(a.G-c.G),Math.Abs(a.B-c.B)));
              maxDelta=Math.Max(maxDelta,delta); if(delta>1) changed++;
            }
            Assert(changed==0,"Icon pixels differ: "+pair.Key+" changed="+changed+" max="+maxDelta);
            Assert(actual.Y==expected.Y,"Icon baseline differs: "+pair.Key+" "+actual.Y+" vs "+expected.Y);
          }
        }
      }
    }
    static async Task CompareButtons(ReaderForm form,string folder,int cycle) {
      var data=Program.Json.Deserialize<Dictionary<string,object>>(await form.Core.ExecuteScriptAsync(@"Object.fromEntries(['new','open','outline-toggle','appearance','more'].map(id=>{const el=document.getElementById(id),r=el.getBoundingClientRect(),s=getComputedStyle(el);return [id,{x:r.x,y:r.y,width:r.width,height:r.height,font:s.fontFamily,size:parseFloat(s.fontSize),weight:s.fontWeight,color:s.color,padding:s.padding,radius:s.borderRadius}]}))"));
      var metrics=new List<object>();
      using(var before=new Bitmap(Path.Combine(folder,cycle+"-empty.png")))
      using(var after=new Bitmap(Path.Combine(folder,cycle+"-editor.png"))) {
        foreach(string id in data.Keys) {
          var button=Descendants(form).OfType<Button>().Single(b=>id=="new"?b.Text==L.T("Создать"):id=="open"?b is PlusButton:b.Name==id);
          var rect=form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
          var d=(Dictionary<string,object>)data[id]; double scale=form.DeviceDpi/96.0;
          var web=new Rectangle((int)Math.Round(Convert.ToDouble(d["x"])*scale),(int)Math.Round(Convert.ToDouble(d["y"])*scale),(int)Math.Round(Convert.ToDouble(d["width"])*scale),(int)Math.Round(Convert.ToDouble(d["height"])*scale));
          Assert(rect.Size==web.Size && rect.Y==web.Y,"Button geometry differs: "+id);
          Assert(button.Font.Name==Convert.ToString(d["font"]).Trim('"') && button.Font.SizeInPoints*96/72==Convert.ToDouble(d["size"]) && Convert.ToString(d["weight"])=="400","Button font differs: "+id);
          int maxDelta=0;
          using(var native=before.Clone(rect,before.PixelFormat))
          using(var document=after.Clone(web,after.PixelFormat)) {
            native.Save(Path.Combine(folder,cycle+"-button-"+id+"-native.png"));
            document.Save(Path.Combine(folder,cycle+"-button-"+id+"-document.png"));
            for(int y=0;y<rect.Height;y++) for(int x=0;x<rect.Width;x++) {
              var a=native.GetPixel(x,y); var b=document.GetPixel(x,y);
              maxDelta=Math.Max(maxDelta,Math.Max(Math.Abs(a.R-b.R),Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B))));
            }
          }
          Assert(maxDelta<=1,"Button pixels differ: "+id+" max="+maxDelta);
          metrics.Add(new {id,native=new {rect.X,rect.Y,rect.Width,rect.Height,font=button.Font.Name,size=button.Font.SizeInPoints*96/72,color=button.ForeColor.ToArgb()},document=d});
        }
      }
      File.WriteAllText(Path.Combine(folder,cycle+"-button-metrics.json"),Program.Json.Serialize(metrics));
    }
    internal static async Task Run(ReaderForm form, bool repeated = false) {
      var folder = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "transition-checks");
      Directory.CreateDirectory(folder);
      try {
        NativeTests.Enabled = true;
        await form.Call("language", new object[] { "ru" });
        form.Activate(); await Task.Delay(300);
        for (int cycle = 0; cycle < (repeated ? 15 : 8); cycle++) {
          int scenario = repeated ? 0 : cycle%4;
          if (repeated) {
            if (cycle == 5) await form.Call("theme", new object[] { "dark" });
            if (cycle == 10) form.ToggleMaximize();
          } else {
            if (scenario == 0) {
              form.WindowState=FormWindowState.Normal; form.Size=new Size(1120,820);
              await form.Call("theme",new object[]{"light"});
              await form.Call("language",new object[]{cycle<4?"ru":"en"});
            }
            if (scenario == 1) form.ToggleMaximize();
            if (scenario == 2) await form.Call("theme", new object[] { "dark" });
            if (scenario == 3) { form.ToggleMaximize(); form.Size = new Size(620, 480); }
          }
          await Task.Delay(150);
          Assert(form.Core == null, "Empty window must release the browser");
          InputFocusTests.Move(form,form.PointToScreen(new Point(form.ClientSize.Width*3/4,form.ClientSize.Height*3/4)));
          await Task.Delay(80);
          var originalBounds = form.Bounds;
          var originalClient = form.ClientSize;
          var create = Descendants(form).OfType<Button>().First(button => button.Text == L.T("Создать"));
          var nativeRect = form.RectangleToClient(create.RectangleToScreen(create.ClientRectangle));
          var nativeColor = create.ForeColor;
          var nativeFont = create.Font.SizeInPoints * 96f / 72f;
          var nativeButtons=new Dictionary<string,Rectangle>();
          foreach(var item in new[]{new {name="panel",type=typeof(SectionsButton)},new {name="plus",type=typeof(PlusButton)},new {name="more",type=typeof(MoreButton)}}) {
            var button=Descendants(form).Single(c=>c.GetType()==item.type);
            nativeButtons.Add(item.name,form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle)));
          }
          Screenshot(form, Path.Combine(folder, cycle + "-empty.png"));
          var samplePoint = form.PointToScreen(new Point(form.ClientSize.Width * 3 / 4, form.ClientSize.Height * 3 / 4));
          int frames = 0, blackFrames = 0, minimum = 255, webEmptyFrames = 0;
          using (var stop = new CancellationTokenSource()) {
            var capture = Task.Run(delegate {
              using (var bitmap = new Bitmap(32, 32)) using (var graphics = Graphics.FromImage(bitmap)) {
                while (!stop.IsCancellationRequested) {
                  graphics.CopyFromScreen(samplePoint, Point.Empty, bitmap.Size);
                  int total = 0;
                  for (int y = 0; y < 32; y += 4) for (int x = 0; x < 32; x += 4) {
                    var color = bitmap.GetPixel(x, y); total += (color.R + color.G + color.B) / 3;
                  }
                  int brightness = total / 64;
                  frames++; minimum = Math.Min(minimum, brightness); if (brightness < 8) blackFrames++;
                  Thread.Sleep(8);
                }
              }
            });
            try {
              if (scenario == 1) {
                var file = Path.Combine(Program.DataRoot, "close-test.md");
                File.WriteAllText(file, "# Saved document\n\nText for the last-tab transition.");
                form.ReceiveArguments(new[] { file });
              } else if (scenario == 2) {
                Descendants(form).OfType<Button>().First(button => button.Text == L.T("Посмотреть пример")).PerformClick();
              } else {
                create.Capture = true; create.PerformClick(); create.Capture = false;
              }
              await Wait(() => form.UiReady && form.Core != null);
              await Task.Delay(200);
              Assert(form.Bounds == originalBounds && form.ClientSize == originalClient, "Window geometry changed on create: " + originalBounds + " -> " + form.Bounds);
              var data = Program.Json.Deserialize<Dictionary<string, object>>(await form.Core.ExecuteScriptAsync(@"(() => {
                const button=document.getElementById('new'), rect=button.getBoundingClientRect(), style=getComputedStyle(button);
                return { y:rect.y, width:rect.width, height:rect.height, color:style.color, font:parseFloat(style.fontSize), editor:!!document.querySelector('.cm-content'), split:document.getElementById('workspace').dataset.view==='split', heading:!!document.querySelector('#document h1') };
              })()"));
              double scale = form.DeviceDpi / 96.0;
              Assert(Math.Abs(Convert.ToDouble(data["y"]) * scale - nativeRect.Y) <= 1 && Math.Abs(Convert.ToDouble(data["height"]) * scale - nativeRect.Height) <= 1 && Math.Abs(Convert.ToDouble(data["width"]) * scale - nativeRect.Width) <= 1, "Header button geometry differs");
              Assert(Convert.ToString(data["color"]) == String.Format("rgb({0}, {1}, {2})", nativeColor.R, nativeColor.G, nativeColor.B) && Convert.ToDouble(data["font"]) == nativeFont, "Header font/color differs");
              if (scenario == 0 || scenario == 3) Assert(Convert.ToBoolean(data["editor"]) && Convert.ToBoolean(data["split"]), "Editor must be mounted when the browser is shown");
              else Assert(Convert.ToBoolean(data["heading"]) && !Convert.ToBoolean(data["split"]), "Saved file/example must be readable when the browser is shown");
              Screenshot(form, Path.Combine(folder, cycle + "-editor.png"));
              await CompareButtons(form,folder,cycle);
              await CompareIcons(form,folder,cycle,nativeButtons);
              form.Core.WebMessageReceived += delegate(object sender, CoreWebView2WebMessageReceivedEventArgs e) {
                if (e.WebMessageAsJson.Contains("\"transitionWebEmpty\"")) webEmptyFrames++;
              };
              await form.Core.ExecuteScriptAsync(@"(() => {
                const empty = document.getElementById('empty-state');
                function check() {
                  if (!empty.hidden && empty.getBoundingClientRect().height > 0)
                    chrome.webview.postMessage({transitionWebEmpty:true});
                  requestAnimationFrame(check);
                }
                requestAnimationFrame(check);
              })()");
              if (scenario == 3) {
                await form.Core.ExecuteScriptAsync("document.getElementById('source').focus()");
                await form.Core.CallDevToolsProtocolMethodAsync("Input.insertText", Program.Json.Serialize(new { text = "# Unsaved draft" }));
                NativeTests.Answers.Enqueue(2);
                await form.Core.ExecuteScriptAsync("document.querySelector('.tab-item.active .tab-close').click()");
                await Task.Delay(200);
                Assert(form.Core != null && form.UiReady && await form.Core.ExecuteScriptAsync("document.getElementById('source').value==='# Unsaved draft' && !document.body.inert") == "true", "Cancel must retain the visible draft");
                NativeTests.Answers.Enqueue(1);
              }
              await form.Core.ExecuteScriptAsync("document.querySelector('.tab-item.active .tab-close').click()");
              await Wait(() => form.Core == null);
              await Task.Delay(200);
              Assert(form.Bounds == originalBounds && form.ClientSize == originalClient, "Window geometry changed on close");
              Assert(form.Store.Entries.Count == 0 && !form.Session.ShouldRestore, "Closed last tab must not return after restart");
              Screenshot(form, Path.Combine(folder, cycle + "-closed.png"));
              Assert(File.ReadAllBytes(Path.Combine(folder, cycle + "-empty.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(folder, cycle + "-closed.png"))), "Native welcome changed after closing the last tab");
            } finally { stop.Cancel(); capture.GetAwaiter().GetResult(); }
          }
          Assert(frames > 20 && blackFrames == 0, "Black transition frames: " + blackFrames + " / " + frames);
          Assert(webEmptyFrames == 0, "Intermediate web welcome screen appeared for " + webEmptyFrames + " frames before native welcome");
          Checks.Add(new { cycle, language=cycle<4?"ru":"en", document = new[] { "pristine new", "saved file", "built-in example", "dirty draft: cancel, discard" }[scenario], dark = form.IsDark, maximized = form.WindowState == FormWindowState.Maximized, frames, blackFrames, webEmptyFrames, minimumBrightness = minimum, geometry = "stable", header = "matched", sharedButtons="all 5 matched within 1 RGB level", sharedIcons="matched within 1 RGB level", emptyScreenshots = "identical" });
        }
        File.WriteAllText(Path.Combine(folder, "result.json"), Program.Json.Serialize(new { ok = true, checks = Checks }));
      } catch (Exception error) {
        File.WriteAllText(Path.Combine(folder, "result.json"), Program.Json.Serialize(new { ok = false, error = error.ToString(), checks = Checks }));
      }
      await form.Call("finish-close", new object[0]);
    }
  }
}
#endif
