#if TESTING
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace MdReader {
  sealed partial class ReaderForm {
    bool documentTiming;
    async Task InstallDocumentTimingProbe() {
      await Core.AddScriptToExecuteOnDocumentCreatedAsync(@"
        window.__documentTiming = { navigationStart: performance.timeOrigin };
        addEventListener('DOMContentLoaded', () => {
          const times = window.__documentTiming;
          const observe = () => {
            const article = document.getElementById('document');
            if (!article) return;
            const now = performance.now();
            if (article.innerText.trim() && !times.text) times.text = now;
            if (article.querySelector('.katex') && !times.formula) times.formula = now;
            if (article.querySelector('.mermaid-diagram svg') && !times.diagram) times.diagram = now;
            if (times.text && article.getAttribute('aria-busy') !== 'true' && !document.body.inert && !times.complete) times.complete = now;
          };
          new MutationObserver(observe).observe(document.documentElement, {subtree:true, childList:true, attributes:true});
          observe();
        });");
    }

    internal async Task RunDocumentTiming(string[] arguments) {
      string scenario = arguments.First(arg => arg.StartsWith("--document-timing=")).Substring(18);
      string path = arguments.FirstOrDefault(arg => arg.StartsWith("--timing-file="));
      if (path != null) path = path.Substring(14);
      string resultPath = Path.Combine(Program.DataRoot, "document-timing.json");
      try {
        documentTiming = true;
        File.WriteAllText(Path.Combine(Program.DataRoot, "empty-ready.json"), Program.Json.Serialize(new { scenario, dpi = DeviceDpi, width = Width, height = Height, nativeOnly = Core == null }));
        var wait = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(Program.DataRoot, "open.signal"))) {
          if (wait.ElapsedMilliseconds > 180000) throw new TimeoutException("No document-open signal");
          await Task.Delay(30);
        }
        var commandUtc = DateTime.UtcNow;
        var clock = Stopwatch.StartNew();
        if (scenario == "sample") nativeSample.PerformClick();
        else ReceiveArguments(new[] { path });
        double previousPollMs = 0;
        while (!UiReady) {
          previousPollMs = clock.Elapsed.TotalMilliseconds;
          if (previousPollMs > 120000) throw new TimeoutException("Document not revealed");
          await Task.Delay(30);
        }
        double revealedMs = clock.Elapsed.TotalMilliseconds;
        string observation = await Core.ExecuteScriptAsync(@"JSON.stringify({
          probe: window.__documentTiming,
          text: document.getElementById('document').innerText.slice(0,160),
          formulas: document.querySelectorAll('#document .katex').length,
          diagrams: document.querySelectorAll('#document .mermaid-diagram svg').length,
          busy: document.getElementById('document').getAttribute('aria-busy')==='true',
          inert: document.body.inert,
          fontsReady: document.fonts.status==='loaded'
        })");
        string observedJson = Program.Json.Deserialize<string>(observation);
        var observed = Program.Json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(observedJson);
        bool ok = !Convert.ToBoolean(observed["busy"]) && !Convert.ToBoolean(observed["inert"]) && Convert.ToBoolean(observed["fontsReady"]) && !String.IsNullOrWhiteSpace((string)observed["text"]);
        if (scenario == "sample") ok &= Convert.ToInt32(observed["formulas"]) > 0 && Convert.ToInt32(observed["diagrams"]) > 0;
        using (var stream = File.Create(Path.Combine(Program.DataRoot, "document-ready.png")))
          await Core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        double verifiedMs = clock.Elapsed.TotalMilliseconds;
        if (scenario == "sample") {
          await Core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", Program.Json.Serialize(new {
            expression = "document.querySelector('#document .katex').scrollIntoView({block:'start'}); new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))",
            awaitPromise = true
          }));
          using (var stream = File.Create(Path.Combine(Program.DataRoot, "formulas-ready.png")))
            await Core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        }
        File.WriteAllText(resultPath + ".tmp", Program.Json.Serialize(new {
          ok, scenario, commandUtc, pollIntervalRequestedMs = 30, revealLowerMs = previousPollMs,
          revealUpperMs = revealedMs, verifiedUpperMs = verifiedMs,
          observed, runtime = Core.Environment.BrowserVersionString, dpi = DeviceDpi,
          method = "TESTING observer; native welcome removed at UiReady; DOM timestamps may precede reveal. CapturePreview verifies WebView content, not desktop visibility."
        }));
        File.Move(resultPath + ".tmp", resultPath);
        wait.Restart();
        while (!File.Exists(Path.Combine(Program.DataRoot, "close.signal")) && wait.ElapsedMilliseconds < 180000) await Task.Delay(100);
      } catch (Exception error) {
        File.WriteAllText(resultPath, Program.Json.Serialize(new { ok = false, scenario, error = error.ToString() }));
      } finally { ReleaseBrowser(); Close(); }
    }
  }
}
#endif
