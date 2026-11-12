#if TESTING
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace MdReader {
  static class QualityTests {
    [StructLayout(LayoutKind.Sequential)] struct WindowRect { public int left,top,right,bottom; }
    [DllImport("user32")] static extern bool GetWindowRect(IntPtr window,out WindowRect rectangle);
    static readonly List<object> Checks = new List<object>();
    internal static IEnumerable<Control> Descendants(Control root) {
      foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    static async Task Ready(ReaderForm form) {
      for (int i = 0; i < 600; i++) {
        if (form.UiReady && await form.Core.ExecuteScriptAsync("!document.body.inert && document.getElementById('document').getAttribute('aria-busy')!=='true' && !!document.querySelector('.katex') && !!document.querySelector('.mermaid-diagram svg')") == "true") { await Task.Delay(250); return; }
        await Task.Delay(50);
      }
      throw new Exception("Quality document did not become ready");
    }
    internal static void Capture(ReaderForm form, string path) { Capture(form.Bounds,path); }
    internal static void Capture(IntPtr window,string path) {
      WindowRect bounds;if(!GetWindowRect(window,out bounds))throw new Exception("Cannot locate test window for capture");
      Capture(Rectangle.FromLTRB(bounds.left,bounds.top,bounds.right,bounds.bottom),path);
    }
    static void Capture(Rectangle bounds,string path) {
      using (var bitmap = new Bitmap(bounds.Width, bounds.Height)) {
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        bitmap.Save(path);
      }
    }
    static async Task EmptySurface(ReaderForm form, string folder) {
      foreach (int width in new[] { 1120, 620 }) foreach (string language in new[] { "ru", "en" }) foreach (string theme in new[] { "light", "dark" }) {
        form.Size = new Size(width, width == 620 ? 480 : 820);
        await form.Call("language", new object[] { language });
        await form.Call("theme", new object[] { theme });
        var outline = Descendants(form).Single(c => c.AccessibleName == L.T("Разделы"));
        Color expected = theme == "dark" ? Color.FromArgb(163, 167, 173) : Color.FromArgb(100, 104, 110);
        if (outline.Enabled || outline.Text != L.T("Разделы")) throw new Exception("Empty outline state or label is incorrect");
        int iconInk = 0, textInk = 0;
        using (var bitmap = new Bitmap(outline.Width, outline.Height)) {
          outline.DrawToBitmap(bitmap, outline.ClientRectangle);
          bitmap.Save(Path.Combine(folder, "outline-" + language + "-" + theme + ".png"));
          for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) {
            Color pixel = bitmap.GetPixel(x, y);
            if (Math.Abs(pixel.R - expected.R) + Math.Abs(pixel.G - expected.G) + Math.Abs(pixel.B - expected.B) < 18) {
              if (x < 33 * form.DeviceDpi / 96f) iconInk++; else textInk++;
            }
          }
          if (iconInk < 5 || textInk < 10) throw new Exception("Native empty outline icon or text is not painted in the theme color: " + theme + " / " + language);
        }
        string captureName = language + "-" + theme + "-" + width;
        string capturePath = Path.Combine(folder, "empty-" + captureName + ".png");
        Color background = theme == "dark" ? Color.FromArgb(32, 33, 34) : Color.White;
        bool painted = false;
        for (int i = 0; i < 20 && !painted; i++) {
          form.Refresh(); await Task.Delay(50);
          Capture(form, capturePath);
          using (var capture = new Bitmap(capturePath))
            painted = capture.GetPixel(capture.Width / 2, capture.Height / 4).ToArgb() == background.ToArgb();
        }
        if (!painted) throw new Exception("Empty-window capture did not show the selected theme: " + theme + " / " + language);
        var geometry = Descendants(form).Where(c => new[] { "empty-content", "empty-title", "empty-hint", "empty-new", "empty-open", "empty-sample", "appearance", "more" }.Contains(c.Name)).ToDictionary(c => c.Name, c => {
          var rect = form.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
          return new { x = rect.X, y = rect.Y, width = rect.Width, height = rect.Height, font = c.Font.SizeInPoints * 96 / 72, color = String.Format("rgb({0}, {1}, {2})", c.ForeColor.R, c.ForeColor.G, c.ForeColor.B) };
        });
        if (form.Core != null) throw new Exception("Empty startup loaded WebView before a user action");
        await form.EnsureBrowser("more");
        bool ready = false;
        for (int i = 0; i < 600; i++) {
          if (form.UiReady && form.nativeMenu != null) { ready = true; break; }
          await Task.Delay(50);
        }
        if (!ready) throw new Exception("Settings menu did not open from the empty window");
        await form.Core.CallDevToolsProtocolMethodAsync("DOM.enable", "{}");
        await form.Core.CallDevToolsProtocolMethodAsync("CSS.enable", "{}");
        var dom = Program.Json.Deserialize<Dictionary<string, object>>(await form.Core.CallDevToolsProtocolMethodAsync("DOM.getDocument", "{}"));
        var root = (Dictionary<string, object>)dom["root"];
        string node = await form.Core.CallDevToolsProtocolMethodAsync("DOM.querySelector", Program.Json.Serialize(new { nodeId = root["nodeId"], selector = "#empty-title" }));
        string fonts = await form.Core.CallDevToolsProtocolMethodAsync("CSS.getPlatformFontsForNode", node);
        File.WriteAllText(Path.Combine(folder, "fonts-" + captureName + ".json"), fonts);
        string expectedColor = String.Format("rgb({0}, {1}, {2})", expected.R, expected.G, expected.B);
        string aligned = await form.Core.ExecuteScriptAsync(@"(()=>{
          const button=document.getElementById('outline-toggle'),style=getComputedStyle(button),theme=document.getElementById('theme');
          return button.disabled && style.opacity==='1' && style.color===" + Program.Json.Serialize(expectedColor) + @"
            && button.textContent.trim()===" + Program.Json.Serialize(outline.Text) + @"
            && theme.closest('#more-menu')!==null && !document.getElementById('appearance-menu').contains(theme)
            && document.getElementById('more-menu').hidden && document.querySelectorAll('#theme').length===1;
        })()");
        if (aligned != "true") throw new Exception("Empty native/web outline or theme menu mismatch");
        if (!Descendants(form.nativeMenu).Any(c=>c.Name=="theme" && c.Visible)) throw new Exception("Theme command missing from native menu");
        string layout = await form.Core.ExecuteScriptAsync(@"(()=>{
          const native=" + Program.Json.Serialize(geometry) + @", errors=[];
          for(const [id,n] of Object.entries(native)) {
            const element=document.getElementById(id),r=element.getBoundingClientRect(),s=getComputedStyle(element);
            for(const key of ['x','y','width','height']) if(Math.abs(r[key]*devicePixelRatio-n[key])>1.1) errors.push(id+' '+key+': '+r[key]+' vs '+n[key]);
            if(id!=='empty-content' && (Math.abs(parseFloat(s.fontSize)-n.font)>.01 || s.color!==n.color || s.fontWeight!=='400')) errors.push(id+' text: '+s.fontSize+' / '+s.fontWeight+' / '+s.color);
          }
          for(const id of ['search-toggle','source-toggle']) if(document.getElementById(id).getBoundingClientRect().width!==0) errors.push('Unexpected empty toolbar button: '+id);
          return errors;
        })()");
        if (layout != "[]") throw new Exception("Empty screen changed when opening settings: " + layout);
        await Task.Delay(150);
        Capture(form, Path.Combine(folder, "menu-" + captureName + ".png"));
        await form.Core.ExecuteScriptAsync("document.getElementById('more').click()");
        await Task.Delay(150);
        Capture(form, Path.Combine(folder, "web-empty-" + captureName + ".png"));
        await form.Core.ExecuteScriptAsync("document.getElementById('more').click()");
        await form.Core.ExecuteScriptAsync("document.getElementById('theme').click()");
        bool targetDark = theme != "dark";
        for (int i = 0; i < 100 && form.IsDark != targetDark; i++) await Task.Delay(30);
        if (form.IsDark != targetDark) throw new Exception("Theme command did not update native chrome");
        await form.Core.ExecuteScriptAsync("document.getElementById('theme').click()");
        for (int i = 0; i < 100 && form.IsDark == targetDark; i++) await Task.Delay(30);
        if (form.IsDark == targetDark) throw new Exception("Theme command did not restore native chrome");
        form.ReleaseBrowser();
        if (outline.ForeColor.ToArgb() != expected.ToArgb()) throw new Exception("Closing settings changed the empty outline color");
        Checks.Add(new { surface = "empty", width, language, theme, nativeIconPixels = iconInk, nativeTextPixels = textInk, themeMenu = "more", outline = "readable and matched", welcomeGeometry = geometry, welcomeMatches = true });
      }
      await form.Call("language", new object[] { "ru" });
      await form.Call("theme", new object[] { "light" });
      form.Size = new Size(1120, 820);
    }
    internal static async Task Run(ReaderForm form) {
      var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "quality-checks");
      Directory.CreateDirectory(folder);
      try {
        await form.Call("language", new object[] { "ru" });
        form.TopMost = true; form.Show(); form.Activate();
        await EmptySurface(form, folder);
        var sample = Descendants(form).OfType<Button>().First(b => b.Text == L.T("Посмотреть пример"));
        sample.PerformClick(); await Ready(form);
        Capture(form, Path.Combine(folder, "reading.png"));
        await form.Core.ExecuteScriptAsync("document.getElementById('view-split').click()"); await Task.Delay(400);
        Capture(form, Path.Combine(folder, "editor.png"));
        foreach (string theme in new[] { "light", "dark" }) {
          if (theme == "dark") { await form.Core.ExecuteScriptAsync("document.getElementById('theme').click()"); await Ready(form); }
          var ratios = await form.Core.ExecuteScriptAsync(@"(()=>{
            const s=getComputedStyle(document.documentElement);
            const l=hex=>{const raw=hex.trim().replace('#',''),v=raw.length===3?[...raw].map(c=>c+c).join(''):raw;return [0,2,4].map(i=>parseInt(v.slice(i,i+2),16)/255).reduce((sum,x,i)=>sum+(x<=.04045?x/12.92:((x+.055)/1.055)**2.4)*[.2126,.7152,.0722][i],0)};
            return ['--text','--muted','--accent'].flatMap(f=>['--surface','--chrome','--hover','--accent-soft'].map(b=>{const a=l(s.getPropertyValue(f)),z=l(s.getPropertyValue(b));return {foreground:f,background:b,ratio:(Math.max(a,z)+.05)/(Math.min(a,z)+.05)}}));
          })()");
          var parsed = Program.Json.DeserializeObject(ratios) as object[];
          foreach (Dictionary<string, object> pair in parsed) if (Convert.ToDouble(pair["ratio"]) < 4.5) throw new Exception(theme + " contrast below 4.5: " + ratios);
          Checks.Add(new { theme, contrast = parsed });
          foreach (double scale in new[] { 1d, 1.25d, 1.5d, 2d }) {
            await form.Core.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", Program.Json.Serialize(new { width = 620, height = 480, deviceScaleFactor = scale, mobile = false }));
            await Task.Delay(150);
            string fits = await form.Core.ExecuteScriptAsync(@"(()=>{
              const r=document.querySelector('.format-toolbar').getBoundingClientRect(),e=document.getElementById('source').getBoundingClientRect();
              return document.documentElement.scrollWidth<=innerWidth && r.left>=0 && r.right<=innerWidth && e.width>150 && e.height>150;
            })()");
            if (fits != "true") throw new Exception("Editor overflow at " + theme + " DPR=" + scale);
            Checks.Add(new { theme, width = 620, height = 480, emulatedDeviceScale = scale, fit = true });
          }
          await form.Core.CallDevToolsProtocolMethodAsync("Emulation.clearDeviceMetricsOverride", "{}");
          await Task.Delay(300);
        }
        Capture(form, Path.Combine(folder, "editor-dark.png"));
        var project = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName;
        form.ReceiveArguments(new[] { Path.Combine(project, "README.md") });
        bool readmeReady = false;
        for (int i = 0; i < 300; i++) {
          if (await form.Core.ExecuteScriptAsync("document.getElementById('filename').textContent==='README.md' && document.getElementById('document').getAttribute('aria-busy')!=='true' && !!document.querySelector('#document img') && [...document.querySelectorAll('#document img')].every(i=>i.complete && i.naturalWidth>0)") == "true") { readmeReady = true; break; }
          await Task.Delay(50);
        }
        if (!readmeReady) throw new Exception("README or its screenshot failed to load");
        await form.Core.ExecuteScriptAsync("document.getElementById('theme').click()"); await Task.Delay(250);
        foreach (int width in new[] { 1120, 620 }) {
          form.Size = new Size(width, 820); await Task.Delay(300);
          if (await form.Core.ExecuteScriptAsync("document.documentElement.scrollWidth<=innerWidth && document.getElementById('document').scrollWidth<=document.getElementById('document').clientWidth+1") != "true") throw new Exception("README overflow at " + width);
          Capture(form, Path.Combine(folder, "readme-" + width + ".png"));
          Checks.Add(new { document = "README.md", width, screenshotLoaded = true, fit = true });
        }
        if (SurfaceText.FallbackCount != 0) throw new Exception("Native text fell back from DirectWrite: " + SurfaceText.FallbackCount);
        File.WriteAllText(Path.Combine(folder, "result.json"), Program.Json.Serialize(new { ok = true, nativeDpi = form.DeviceDpi, textFallbacks = SurfaceText.FallbackCount, checks = Checks }));
      } catch (Exception error) { File.WriteAllText(Path.Combine(folder, "result.json"), Program.Json.Serialize(new { ok = false, error = error.ToString(), checks = Checks })); }
      finally { form.ReleaseBrowser(); form.Close(); }
    }
  }
}
#endif
