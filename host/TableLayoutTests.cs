#if TESTING
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace MdReader {
  static class TableLayoutTests {
    static async Task ExpectAlignment(ReaderForm form, string[] expected) {
      string expression = @"(()=>{
        const article=document.getElementById('document'), table=article.querySelectorAll('table')[1];
        const expected=" + Program.Json.Serialize(expected) + @";
        return !document.body.inert && article.getAttribute('aria-busy')!=='true' && table &&
          table.rows.length===3 && [...table.rows].every(row=>[...row.cells].every((cell,i)=>getComputedStyle(cell).textAlign===expected[i]));
      })()";
      for (int i = 0; i < 100; i++) {
        if (await form.Core.ExecuteScriptAsync(expression) == "true") return;
        await Task.Delay(50);
      }
      throw new Exception("Expected table alignment: " + String.Join(",", expected));
    }

    static async Task ReplaceDelimiter(ReaderForm form, string oldValue, string newValue) {
      string selected = await form.Core.ExecuteScriptAsync(@"(()=>{
        const source=document.getElementById('source'), delimiter=" + Program.Json.Serialize(oldValue) + @";
        const at=source.value.indexOf(delimiter);
        if(at<0) return false;
        source.focus(); source.setSelectionRange(at,at+delimiter.length); return true;
      })()");
      if (selected != "true") throw new Exception("Delimiter missing from editor");
      await form.Core.CallDevToolsProtocolMethodAsync("Input.insertText", Program.Json.Serialize(new { text = newValue }));
    }

    internal static async Task Run(ReaderForm form) {
      var checks = new List<object>();
      var project = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName;
      var folder = Program.DataRoot;
      try {
        form.ReceiveArguments(new[] { Path.Combine(project, "test", "fixtures", "table-layout.md") });
        bool ready = false;
        for (int i = 0; i < 600; i++) {
          if (form.UiReady && await form.Core.ExecuteScriptAsync("!document.body.inert && document.getElementById('document').getAttribute('aria-busy')!=='true' && document.querySelectorAll('#document table').length===3") == "true") { ready = true; break; }
          await Task.Delay(50);
        }
        if (!ready) throw new Exception("Table fixture did not load");
        string probe = File.ReadAllText(Path.Combine(project, "test", "table-layout.js"));
        foreach (string theme in new[] { "light", "dark" }) {
          if ((await form.Core.ExecuteScriptAsync("document.documentElement.dataset.theme")) != Program.Json.Serialize(theme))
            await form.Core.ExecuteScriptAsync("document.getElementById('theme').click()");
          foreach (string mode in new[] { "read", "split" }) {
            await form.Core.ExecuteScriptAsync("document.getElementById('view-" + mode + "').click()");
            foreach (int width in new[] { 620, 1120 }) {
              form.Size = new Size(width, 820);
              foreach (int font in new[] { 16, 24 }) {
                await form.Core.ExecuteScriptAsync("document.documentElement.style.setProperty('--doc-size','" + font + "px')");
                await Task.Delay(180);
                string name = theme + "-" + mode + "-" + width + "-" + font;
                var result = Program.Json.Deserialize<Dictionary<string, object>>(await form.Core.ExecuteScriptAsync(probe));
                checks.Add(new { name, result });
                if (width == 620 && font == 16) {
                  using (var stream = File.Create(Path.Combine(folder, name + ".png")))
                    await form.Core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                }
                if (!Convert.ToBoolean(result["ok"])) throw new Exception("Table layout failed: " + name + " " + Program.Json.Serialize(result));
              }
            }
          }
        }
        await form.Core.ExecuteScriptAsync("document.getElementById('view-split').click()");
        await ReplaceDelimiter(form, "| :--- | :---: | ---: |", "| ---: | :--- | :---: |");
        await ExpectAlignment(form, new[] { "right", "left", "center" });
        checks.Add(new { name = "edit-alignment", ok = true });
        await ReplaceDelimiter(form, "| ---: | :--- | :---: |", "| --- | --- | --- |");
        await ExpectAlignment(form, new[] { "left", "left", "left" });
        checks.Add(new { name = "remove-alignment", ok = true });
        foreach (string[] expected in new[] { new[] { "right", "left", "center" }, new[] { "left", "center", "right" } }) {
          foreach (string type in new[] { "rawKeyDown", "keyUp" })
            await form.Core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", Program.Json.Serialize(new { type, key = "z", code = "KeyZ", modifiers = 2, windowsVirtualKeyCode = 90, nativeVirtualKeyCode = 90 }));
          await ExpectAlignment(form, expected);
          checks.Add(new { name = "undo-alignment-" + expected[0], ok = true });
        }
        await form.Core.ExecuteScriptAsync("document.getElementById('view-read').click(); document.documentElement.style.removeProperty('--doc-size')");
        await form.Core.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"media\":\"print\"}");
        await Task.Delay(150);
        bool printFits = await form.Core.ExecuteScriptAsync(@"(()=>{
          const table=document.querySelector('#document table'), wrapper=table.parentElement;
          return table.rows[1].cells.length===12 && table.getBoundingClientRect().width<=wrapper.clientWidth+1 && getComputedStyle(wrapper).overflowX==='visible';
        })()") == "true";
        checks.Add(new { name = "print-table-fit", ok = printFits });
        if (!printFits) throw new Exception("Table no longer fits print layout");
        await ExpectAlignment(form, new[] { "left", "center", "right" });
        checks.Add(new { name = "print-alignment", ok = true });
        await form.Core.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"media\":\"screen\"}");
        File.WriteAllText(Path.Combine(folder, "table-layout.json"), Program.Json.Serialize(new { ok = true, runtime = form.Core.Environment.BrowserVersionString, nativeDpi = form.DeviceDpi, checks }));
      } catch (Exception error) {
        File.WriteAllText(Path.Combine(folder, "table-layout.json"), Program.Json.Serialize(new { ok = false, error = error.ToString(), checks }));
      } finally { form.ReleaseBrowser(); form.Close(); }
    }
  }
}
#endif
