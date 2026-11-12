#if TESTING
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace MdReader {
  static class PdfContentTests {
    static async Task Wait(ReaderForm form, string expression) {
      for (int i = 0; i < 1200; i++) {
        if (form.UiReady && await form.Core.ExecuteScriptAsync(expression) == "true") return;
        await Task.Delay(50);
      }
      throw new Exception("PDF content test timed out: " + expression);
    }
    static void Picture(string path, int width, int height) {
      using (var bitmap = new Bitmap(width, height)) using (var g = Graphics.FromImage(bitmap)) using (var font = new Font("Arial", 24)) {
        g.Clear(Color.WhiteSmoke);
        g.FillRectangle(Brushes.Red, 0, 0, width, 45);
        g.DrawString("IMAGE TOP", font, Brushes.Black, 10, 50);
        g.DrawRectangle(Pens.Black, 1, 1, width - 3, height - 3);
        g.DrawString("IMAGE BOTTOM", font, Brushes.Black, 10, height - 95);
        g.FillRectangle(Brushes.Blue, 0, height - 45, width, 45);
        bitmap.Save(path, ImageFormat.Png);
      }
    }
    static string Fixture(string kind, List<string> expected) {
      var md = new StringBuilder("# PDF content check\n\n");
      if (kind == "images-landscape") {
        kind = "images";
        md.AppendLine("|1|2|3|4|5|6|7|8|9|\n|-|-|-|-|-|-|-|-|-|\n|a|b|c|d|e|f|g|h|i|\n");
      }
      if (kind == "code" || kind == "mixed") {
        md.AppendLine("## CODE\n\n```python\n# Русский комментарий\ndef greeting(name):\n    return f'Hello, {name}'\n```");
        md.AppendLine("\n```text");
        int count = kind == "code" ? 90 : 8;
        for (int i = 1; i <= count; i++) { string line = "LINE" + i.ToString("D3") + "  indent    value"; md.AppendLine(line); expected.Add("LINE" + i.ToString("D3")); }
        string longLine = "LONG_START_" + new string('x', 280) + "_LONG_END";
        md.AppendLine(longLine + "\n```\n"); expected.Add(longLine);
      }
      if (kind == "math" || kind == "mixed") {
        md.AppendLine("## MATH\n\nInline $E = mc^2$ and $\\frac{a}{b}$.\n\n$$\\int_0^1 x^2\\,dx=\\frac{1}{3}$$\n\n$$\\begin{pmatrix}1&2\\\\3&4\\end{pmatrix}$$\n");
        if (kind == "math") {
          var terms = Enumerable.Range(1, 30).Select(i => "TERM" + i.ToString("D2")).ToArray();
          md.AppendLine("$$" + String.Join("+", terms.Select(t => "\\mathrm{" + t + "}")) + "$$\n"); expected.AddRange(terms);
          var columns = Enumerable.Range(1, 18).Select(i => "COL" + i.ToString("D2")).ToArray();
          md.AppendLine("$$\\begin{bmatrix}" + String.Join("&", columns.Select(c => "\\mathrm{" + c + "}")) + "\\end{bmatrix}$$\n"); expected.AddRange(columns);
        }
      }
      if (kind == "diagrams" || kind == "mixed") {
        md.AppendLine("## DIAGRAMS\n\n```mermaid\nflowchart LR\nA[DIAGRAM_START] --> B[DIAGRAM_END]\n```\n");
        expected.AddRange(new[] { "DIAGRAM_START", "DIAGRAM_END" });
        if (kind == "diagrams") {
          foreach (string direction in new[] { "LR", "TD" }) {
            md.AppendLine("```mermaid\nflowchart " + direction);
            for (int i = 1; i <= 16; i++) { string label = direction + i.ToString("D2"); md.AppendLine("N" + i + "[" + label + "]" + (i < 16 ? " --> N" + (i + 1) : "")); expected.Add(label); }
            md.AppendLine("```\n");
          }
          md.AppendLine("```mermaid\nsequenceDiagram\nClient->>Server: REQUEST\nServer-->>Client: RESPONSE\n```\n"); expected.AddRange(new[] { "REQUEST", "RESPONSE" });
        }
      }
      if (kind == "images" || kind == "mixed") {
        md.AppendLine("## IMAGES\n\n![Wide local image](wide.png)\n\n![Vector image](vector.svg)\n");
        if (kind == "images") md.AppendLine("![Tall image](tall.png)\n\n![Delayed remote image](https://pdf-content.invalid/delayed.png)\n");
      }
      if (kind == "errors") md.AppendLine("## ERRORS\n\n$$\\notAValidCommand{TEST}$$\n\n```mermaid\ninvalid diagram TYPE_ERROR\n```\n\n![MISSING_IMAGE](missing.png)\n");
      md.AppendLine("END_OF_DOCUMENT"); expected.Add("END_OF_DOCUMENT");
      return md.ToString();
    }
    internal static async Task Run(ReaderForm form) {
      string folder = Path.Combine(Program.DataRoot, "pdf-content"); Directory.CreateDirectory(folder);
      var cases = new List<object>(); NativeTests.Enabled = true;
      try {
        Picture(Path.Combine(folder, "wide.png"), 1400, 260); Picture(Path.Combine(folder, "tall.png"), 400, 2400);
        File.WriteAllText(Path.Combine(folder, "vector.svg"), "<svg xmlns='http://www.w3.org/2000/svg' width='900' height='180' viewBox='0 0 900 180'><rect x='2' y='2' width='896' height='176' fill='#e8eef5' stroke='#224466'/><circle cx='70' cy='90' r='40' fill='#224466'/><text x='140' y='105' font-family='Arial' font-size='32'>LOCAL SVG: BEGIN TO END</text></svg>");
        bool remoteHooked = false; int remoteRequests = 0;
        foreach (string scenario in new[] { "code-read", "math-read", "diagrams-read", "images-read", "images-landscape-read", "mixed-read", "mixed-split", "mixed-source", "errors-read" }) {
          int dash = scenario.LastIndexOf('-'); string kind = scenario.Substring(0, dash), mode = scenario.Substring(dash + 1);
          var expected = new List<string>(); string source = Path.Combine(folder, scenario + ".md");
          File.WriteAllText(source, Fixture(kind, expected), new UTF8Encoding(false));
          form.Size = new Size(620, 820); form.ReceiveArguments(new[] { source });
          await Wait(form, "document.getElementById('filename').textContent===" + Program.Json.Serialize(Path.GetFileName(source)) + " && !document.body.inert && document.getElementById('document').getAttribute('aria-busy')!=='true'");
          if (!remoteHooked) {
            form.Core.AddWebResourceRequestedFilter("https://pdf-content.invalid/*", CoreWebView2WebResourceContext.Image, CoreWebView2WebResourceRequestSourceKinds.All);
            form.Core.WebResourceRequested += async (sender, e) => {
              if (!e.Request.Uri.StartsWith("https://pdf-content.invalid/")) return;
              using (var deferral = e.GetDeferral()) {
                remoteRequests++; await Task.Delay(e.Request.Uri.EndsWith("timeout.png") ? 20000 : 5000);
                e.Response = form.Core.Environment.CreateWebResourceResponse(new MemoryStream(File.ReadAllBytes(Path.Combine(folder, "wide.png"))), 200, "OK", "Content-Type: image/png\r\nCache-Control: no-store");
              }
            }; remoteHooked = true;
          }
          await form.Core.ExecuteScriptAsync("document.getElementById('view-" + mode + "').click()");
          if (mode == "split") await form.Core.ExecuteScriptAsync("document.getElementById('theme').click()");
          if (mode == "source") {
            await Wait(form, "document.getElementById('source').editorMounted");
            await form.Core.ExecuteScriptAsync("{const s=document.getElementById('source');s.focus();s.setSelectionRange(s.value.length,s.value.length)}");
            await form.Core.CallDevToolsProtocolMethodAsync("Input.insertText", Program.Json.Serialize(new { text = "\n\n$$\\mathrm{NEWFORMULA}=42$$\n" })); expected.Add("NEWFORMULA");
          }
          await Wait(form, "document.getElementById('document').getAttribute('aria-busy')!=='true'");
          await form.Core.ExecuteScriptAsync("{document.getElementById('reading-pane').scrollTop=80;document.querySelectorAll('#document pre,#document .katex-display').forEach(e=>e.scrollLeft=10000)}");
          string stateScript = "JSON.stringify({theme:document.documentElement.dataset.theme,mode:document.getElementById('workspace').dataset.view,text:document.getElementById('source').value,sourceTop:document.getElementById('source').scrollTop,readTop:document.getElementById('reading-pane').scrollTop,horizontal:document.getElementById('workspace').dataset.view==='source'?[]:[...document.querySelectorAll('#document pre,#document .katex-display')].map(e=>e.scrollLeft)})";
          string before = await form.Core.ExecuteScriptAsync(stateScript);
          string pdf = Path.Combine(folder, scenario + ".pdf"); NativeTests.SavePath = pdf;
          await form.Core.ExecuteScriptAsync("document.getElementById('pdf').click()");
          await Wait(form, "!document.getElementById('pdf').disabled && !document.getElementById('workspace').inert");
          if (!File.Exists(pdf)) throw new Exception("PDF missing: " + scenario);
          bool restored = before == await form.Core.ExecuteScriptAsync(stateScript);
          string diagnostics = await form.Core.ExecuteScriptAsync("JSON.stringify({math:document.querySelectorAll('#document .katex').length,svg:document.querySelectorAll('#document .mermaid-diagram>svg').length,diagramErrors:document.querySelectorAll('#document .diagram-error').length,images:[...document.querySelectorAll('#document img')].map(i=>({src:i.getAttribute('src'),loaded:i.complete&&i.naturalWidth>0}))})");
          cases.Add(new { scenario, expected, restored, diagnostics = Program.Json.DeserializeObject(diagnostics), pdf = Path.GetFileName(pdf) });
        }
        string timeoutSource = Path.Combine(folder, "timeout.md");
        File.WriteAllText(timeoutSource, "# TIMEOUT\n\n![Slow image](https://pdf-content.invalid/timeout.png)\n\nEND_OF_DOCUMENT");
        form.ReceiveArguments(new[] { timeoutSource });
        await Wait(form, "document.getElementById('filename').textContent==='timeout.md' && !!document.querySelector('#document img')");
        string timeoutPdf = Path.Combine(folder, "images-timeout-retry.pdf"); NativeTests.SavePath = timeoutPdf;
        await form.Core.ExecuteScriptAsync("document.getElementById('pdf').click()");
        await Wait(form, "!document.getElementById('pdf').disabled && !document.getElementById('workspace').inert");
        if (File.Exists(timeoutPdf)) throw new Exception("Incomplete PDF was saved after image timeout");
        await Wait(form, "document.getElementById('toast').textContent.includes('Изображения ещё загружаются') || document.getElementById('toast').textContent.includes('Images are still loading')");
        await Wait(form, "document.querySelector('#document img').complete && document.querySelector('#document img').naturalWidth>0");
        await form.Core.ExecuteScriptAsync("document.getElementById('pdf').click()");
        await Wait(form, "!document.getElementById('pdf').disabled && !document.getElementById('workspace').inert");
        if (!File.Exists(timeoutPdf)) throw new Exception("PDF retry failed");
        cases.Add(new { scenario = "timeout-retry", expected = new[] { "END_OF_DOCUMENT" }, restored = true, diagnostics = "{\"images\":[{\"loaded\":true}]}", pdf = Path.GetFileName(timeoutPdf) });
        File.WriteAllText(Path.Combine(folder, "manifest.json"), Program.Json.Serialize(new { ok = true, runtime = form.Core.Environment.BrowserVersionString, remoteRequests, timeoutRecovered = true, cases }));
      } catch (Exception error) { File.WriteAllText(Path.Combine(folder, "manifest.json"), Program.Json.Serialize(new { ok = false, error = error.ToString(), cases })); }
      finally { form.ReleaseBrowser(); form.Close(); }
    }
  }
}
#endif
