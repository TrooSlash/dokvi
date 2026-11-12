#if TESTING
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MdReader {
  static class PdfTableTests {
    static async Task Wait(ReaderForm form, string expression) {
      for (int i = 0; i < 600; i++) {
        if (form.UiReady && await form.Core.ExecuteScriptAsync(expression) == "true") return;
        await Task.Delay(50);
      }
      throw new Exception("PDF test timed out: " + expression);
    }

    static string Fixture(string kind, List<string> expected) {
      var text = new StringBuilder("# PDF table check\n\n");
      int columns = kind == "compact" ? 4 : kind == "many-columns" ? 32 : kind == "tall-row" ? 3 : 12;
      int rows = kind == "multipage" ? 60 : kind == "compact" ? 12 : kind == "tall-row" ? 1 : 4;
      var headers = kind == "compact" ? new[] { "Left", "Center", "Right", "Default" } : Enumerable.Range(1, columns).Select(n => "H" + n.ToString("D2")).ToArray();
      expected.AddRange(headers);
      text.AppendLine("| " + String.Join(" | ", headers) + " |");
      text.AppendLine("| " + String.Join(" | ", Enumerable.Range(0, columns).Select(n => kind == "compact" && n == 3 ? "---" : new[] { ":---", ":---:", "---:" }[n % 3])) + " |");
      for (int row = 1; row <= rows; row++) {
        var cells = new List<string>();
        for (int col = 1; col <= columns; col++) {
          string value = kind == "compact" ? new[] { "L", "C", "R", "D" }[col - 1] + row
            : "R" + row.ToString("D3") + "C" + col.ToString("D2");
          if (kind == "wide") value += "LongIdentifierWithoutSpaces1234567890END";
          if (kind == "tall-row" && col == 1) {
            var lines = Enumerable.Range(1, 90).Select(n => "TALL" + n.ToString("D3")).ToArray();
            expected.AddRange(lines); value = String.Join("<br>", lines);
          } else expected.Add(value);
          cells.Add(value);
        }
        text.AppendLine("| " + String.Join(" | ", cells) + " |");
      }
      text.AppendLine("\nEND_OF_DOCUMENT"); expected.Add("END_OF_DOCUMENT");
      return text.ToString();
    }

    internal static async Task Run(ReaderForm form) {
      var results = new List<object>();
      string folder = Path.Combine(Program.DataRoot, "pdf-tables"); Directory.CreateDirectory(folder);
      NativeTests.Enabled = true;
      try {
        foreach (string scenario in new[] { "compact-read", "wide-read", "wide-split", "wide-source", "many-columns-read", "multipage-read", "tall-row-read" }) {
          int dash = scenario.LastIndexOf('-');
          string kind = scenario.Substring(0, dash), mode = scenario.Substring(dash + 1);
          var expected = new List<string>();
          string source = Path.Combine(folder, scenario + ".md");
          File.WriteAllText(source, Fixture(kind, expected), new UTF8Encoding(false));
          form.Size = new Size(620, 820);
          form.ReceiveArguments(new[] { source });
          await Wait(form, "document.getElementById('filename').textContent===" + Program.Json.Serialize(Path.GetFileName(source)) + " && !document.body.inert && document.getElementById('document').getAttribute('aria-busy')!=='true' && !!document.querySelector('#document table')");
          await form.Core.ExecuteScriptAsync("document.getElementById('view-" + mode + "').click()");
          if (mode == "split") await form.Core.ExecuteScriptAsync("document.getElementById('theme').click()");
          if (mode == "source") {
            await Wait(form, "document.getElementById('source').editorMounted");
            await form.Core.ExecuteScriptAsync("{const s=document.getElementById('source');s.focus();s.setSelectionRange(s.value.length,s.value.length)}");
            await form.Core.CallDevToolsProtocolMethodAsync("Input.insertText", Program.Json.Serialize(new { text = "\nUNSAVED_CURRENT_TEXT\n" }));
            expected.Add("UNSAVED_CURRENT_TEXT");
          }
          string stateScript = @"JSON.stringify({theme:document.documentElement.dataset.theme,mode:document.getElementById('workspace').dataset.view,text:document.getElementById('source').value,inert:document.getElementById('workspace').inert,top:document.getElementById('reading-pane').scrollTop,left:document.querySelector('#document .table-scroll')?.scrollLeft||0,sourceTop:document.getElementById('source').scrollTop})";
          await form.Core.ExecuteScriptAsync("{document.getElementById('reading-pane').scrollTop=80;const table=document.querySelector('#document .table-scroll');if(table)table.scrollLeft=10000;}");
          string before = await form.Core.ExecuteScriptAsync(stateScript);
          string pdf = Path.Combine(folder, scenario + ".pdf"); NativeTests.SavePath = pdf;
          await form.Core.ExecuteScriptAsync("document.getElementById('pdf').click()");
          await Wait(form, "!document.getElementById('pdf').disabled && !document.getElementById('workspace').inert");
          if (!File.Exists(pdf)) throw new Exception("PDF export did not create " + scenario);
          string after = await form.Core.ExecuteScriptAsync(stateScript);
          bool restored = before == after;
          if (!restored) throw new Exception("Export changed editor state: " + scenario + " Before: " + before + " After: " + after);
          results.Add(new { scenario, expected, restored, pdf = Path.GetFileName(pdf) });
          File.WriteAllText(Path.Combine(folder, "manifest.json"), Program.Json.Serialize(new { ok = true, runtime = form.Core.Environment.BrowserVersionString, cases = results }));
        }
        foreach (string output in new[] { null, folder }) {
          NativeTests.SavePath = output;
          string before = await form.Core.ExecuteScriptAsync("JSON.stringify({theme:document.documentElement.dataset.theme,mode:document.getElementById('workspace').dataset.view,text:document.getElementById('source').value})");
          await form.Core.ExecuteScriptAsync("document.getElementById('pdf').click()");
          await Wait(form, "!document.getElementById('pdf').disabled && !document.getElementById('workspace').inert");
          if (before != await form.Core.ExecuteScriptAsync("JSON.stringify({theme:document.documentElement.dataset.theme,mode:document.getElementById('workspace').dataset.view,text:document.getElementById('source').value})")) throw new Exception("Cancel or failed export changed editor state");
          if (Directory.GetFiles(folder, "*.pdf").Length != 7) throw new Exception("Cancel or failed export created an extra PDF");
        }
        File.WriteAllText(Path.Combine(folder, "manifest.json"), Program.Json.Serialize(new { ok = true, runtime = form.Core.Environment.BrowserVersionString, cancelRestored = true, failureRestored = true, cases = results }));
      } catch (Exception error) {
        File.WriteAllText(Path.Combine(folder, "manifest.json"), Program.Json.Serialize(new { ok = false, error = error.ToString(), cases = results }));
      } finally { form.ReleaseBrowser(); form.Close(); }
    }
  }
}
#endif
