using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// A plain WebView2 host separates Windows/provider cost from Dokvi's renderer.
internal sealed class WebViewAccessibilityControl : Form {
  readonly WebView2 view = new WebView2 { Dock = DockStyle.Fill };
  readonly string profile;

  WebViewAccessibilityControl(string root) {
    profile = root;
    Text = "WebView2 accessibility control";
    Size = new Size(1120, 820);
    StartPosition = FormStartPosition.CenterScreen;
    Controls.Add(view);
    Shown += async delegate {
      try {
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(profile, "browser"));
        var options = environment.CreateCoreWebView2ControllerOptions();
        options.IsInPrivateModeEnabled = true;
        options.AllowHostInputProcessing = true;
        await view.EnsureCoreWebView2Async(environment, options);
        var html = new StringBuilder("<!doctype html><html lang='en'><meta charset='utf-8'><title>Static large document</title><style>body{font:16px/1.7 'Segoe UI',sans-serif;margin:28px 56px}h2{font-size:20px}a{color:#526b80}</style><body>");
        for (int i = 0; i < 1200; i++) {
          html.Append("<h2 id='section-").Append(i).Append("'>Section ").Append(i).Append("</h2><p>");
          for (int j = 0; j < 6; j++) html.Append("Document text. <strong>Important point</strong> and <a href='#section-0'>link</a>. ");
          html.Append("</p>");
        }
        html.Append("</body></html>");
        view.CoreWebView2.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e) {
          File.WriteAllText(Path.Combine(profile, "ready.txt"), e.IsSuccess.ToString());
        };
        view.CoreWebView2.NavigateToString(html.ToString());
      } catch (Exception error) {
        File.WriteAllText(Path.Combine(profile, "error.txt"), error.ToString());
        Close();
      }
    };
  }

  [STAThread]
  static void Main(string[] args) {
    if (args.Length != 1) throw new ArgumentException("Pass an isolated test profile directory.");
    string root = Path.GetFullPath(args[0]);
    Directory.CreateDirectory(root);
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new WebViewAccessibilityControl(root));
  }
}
