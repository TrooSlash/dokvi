#if TESTING
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdReader {
  static class LanguageTests {
    static readonly List<string> Checks = new List<string>();
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); Checks.Add(message); }
    static IEnumerable<Control> All(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (var nested in All(child)) yield return nested; } }
    static async Task<T> JS<T>(ReaderForm form, string code) { return Program.Json.Deserialize<T>(await form.Core.ExecuteScriptAsync(code)); }
    static async Task Poll(Func<Task<bool>> condition) { for (int i=0;i<250;i++) { if (await condition()) return; await Task.Delay(60); } throw new Exception("Language UI timed out"); }
    static async Task Select(ReaderForm form, string preference, string expected) {
      // The document and empty window share the native selector.
      form.CloseNativeMenu(false);
      await form.Core.ExecuteScriptAsync("document.getElementById('more').click()");
      await Poll(()=>Task.FromResult(form.nativeMenu!=null));
      var keys = new List<string> { "Home" };
      if (preference != "system") keys.Add("ArrowDown");
      if (preference == "en") keys.Add("ArrowDown");
      foreach (string key in keys) {
        All(form.nativeMenu).Single(c=>c.Name=="language").Focus();
        SendKeys.SendWait(key=="Home"?"{HOME}":"{DOWN}");
        await Task.Delay(250);
      }
      await Poll(async delegate { return await JS<bool>(form, "document.documentElement.lang==="+Program.Json.Serialize(expected)+" && localStorage.getItem('language')==="+Program.Json.Serialize(preference)); });
      Assert(L.Language == expected && L.Preference == preference, "Native host follows the UI language choice");
      var settings = Program.Json.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(Program.DataRoot,"settings.json")));
      Assert(settings["language"] == preference, "Language choice is saved before restart");
      form.CloseNativeMenu(false);
    }
    internal static async Task Run(ReaderForm form, string[] arguments) {
      NativeTests.Enabled = true;
      string phase=arguments.First(value=>value.StartsWith("--language-phase=")).Substring("--language-phase=".Length);
      try {
        string expected=phase=="seed-en" || phase=="saved-ru" || phase=="system-ru" || phase=="sample-seed" ? "ru" : "en";
        Assert(L.Language==expected, "Fresh process resolves " + phase + " to " + expected);
        Assert(form.Core==null, "Localized empty window stays native");
        Assert(All(form).Any(control=>control.Text==(expected=="en" ? "Open or create a file" : "Откройте или создайте файл")), "Native welcome uses the resolved language");
        Assert(All(form).Any(control=>control.AccessibleName==(expected=="en" ? "Minimize" : "Свернуть")), "Native caption labels use the resolved language");
        if (expected=="en") using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size)); bitmap.Save(Path.Combine(Program.DataRoot,"empty-"+phase+".png")); }
        await form.EnsureBrowser();
        await Poll(async delegate { return form.Core!=null && form.UiReady && await JS<bool>(form,"document.documentElement.lang==="+Program.Json.Serialize(expected)+" && document.getElementById('empty-new').textContent.includes("+Program.Json.Serialize(expected=="en"?"Create a new file":"Создать новый файл")+")"); });
        Assert(await JS<bool>(form,"document.getElementById('language').value==="+Program.Json.Serialize(L.Preference)), "Private WebView inherits the persisted preference");
        if (phase=="seed-en") await Select(form,"en","en");
        if (phase=="saved-en") await Select(form,"ru","ru");
        if (phase=="saved-ru") await Select(form,"system","en");
        if (phase=="sample-seed") {
          await form.Core.ExecuteScriptAsync("document.getElementById('empty-sample').click()");
          await Poll(async delegate { return await JS<bool>(form,"document.getElementById('filename').textContent==='Пример.md' && document.querySelector('#document h1')?.textContent==='Чтение Markdown'"); });
          var sample = form.Demo();
          await Select(form,"en","en");
          Assert(Object.ReferenceEquals(sample,form.Demo()), "Changing language preserves the opened example baseline");
          Assert(await JS<bool>(form,"!document.querySelector('.tab-item.active').classList.contains('dirty') && document.getElementById('filename').textContent==='Пример.md'"), "Opened example stays unchanged after language switch");
          File.WriteAllText(Path.Combine(Program.DataRoot,phase+".json"),Program.Json.Serialize(new {ok=true,checks=Checks}));
          form.Close(); return; // Exercise the real session checkpoint and next process restore.
        }
        if (phase=="sample-restore") {
          await Poll(async delegate { return await JS<bool>(form,"document.querySelector('#document h1')?.textContent==='Чтение Markdown'"); });
          Assert(await JS<bool>(form,"document.documentElement.lang==='en' && document.getElementById('filename').textContent==='Пример.md' && !document.querySelector('.tab-item.active').classList.contains('dirty')"), "Restart keeps the example language, name and clean state with English UI");
          Assert(form.Demo().name=="Пример.md" && form.Demo().text.StartsWith("# Чтение Markdown"), "Restored example retains its original host baseline");
          await form.Core.ExecuteScriptAsync("document.querySelector('.tab-item.active .tab-close').click()");
          await Poll(async delegate { await Task.Delay(1); return form.Core==null; });
        }
        if (phase=="seed-en" || phase=="saved-en") {
          await form.Core.ExecuteScriptAsync("document.getElementById('new').click()");
          await Poll(async delegate { return await JS<bool>(form,"!!document.getElementById('source')?.editorMounted"); });
          Assert(form.Store.Entries.Values.Single().doc.name.StartsWith(L.Language=="en"?"Untitled":"Без имени"), "New document names follow the selected language");
          await form.Core.ExecuteScriptAsync("document.querySelector('.tab-item.active .tab-close').click()");
          await Poll(async delegate { await Task.Delay(1); return form.Core==null; });
          Assert(All(form).Any(control=>control.Text==(L.Language=="en"?"Open or create a file":"Откройте или создайте файл")), "Native empty screen retains language after browser disposal");
          await form.EnsureBrowser();
          await Poll(async delegate { return form.Core!=null && form.UiReady && await JS<bool>(form,"document.documentElement.lang==="+Program.Json.Serialize(L.Language)); });
          Assert(true, "Browser can reopen immediately after changing its language");
          await form.Core.ExecuteScriptAsync("document.getElementById('empty-sample').click()");
          await Poll(async delegate { return await JS<bool>(form,"document.querySelector('#document h1')?.textContent==="+Program.Json.Serialize(L.Language=="en"?"Reading Markdown":"Чтение Markdown")); });
          Assert(form.Demo().name==(L.Language=="en"?"Example.md":"Пример.md"), "Fresh built-in example uses the selected language");
          await form.Core.ExecuteScriptAsync("document.querySelector('.tab-item.active .tab-close').click()");
          await Poll(async delegate { await Task.Delay(1); return form.Core==null; });
        }
        File.WriteAllText(Path.Combine(Program.DataRoot,phase+".json"),Program.Json.Serialize(new {ok=true,checks=Checks}));
        form.ReleaseBrowser(); form.Close();
      } catch(Exception error) {
        File.WriteAllText(Path.Combine(Program.DataRoot,phase+".json"),Program.Json.Serialize(new {ok=false,checks=Checks,error=error.ToString()}));
        form.ReleaseBrowser(); form.Close();
      }
    }
  }
}
#endif
