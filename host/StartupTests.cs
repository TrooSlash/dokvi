#if TESTING
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdReader {
  static class StartupTests {
    static bool HasText(Control root, string text) {
      return root.Text == text || root.Controls.Cast<Control>().Any(child => HasText(child, text));
    }
    internal static async Task Run(ReaderForm form) {
      await Task.Delay(200);
      string expected = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
      bool native = form.Core == null;
      bool language = L.Preference == "system" && L.Language == expected && HasText(form, expected == "ru" ? "Откройте или создайте файл" : "Open or create a file");
      using (var bitmap = new Bitmap(form.Width, form.Height)) {
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(Program.DataRoot, "startup.png"));
      }
      File.WriteAllText(Path.Combine(Program.DataRoot, "startup.json"), Program.Json.Serialize(new {
        ok = native && language, actualSystemCulture = CultureInfo.CurrentUICulture.Name, language = L.Language,
        readyObservedMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds,
        observationDelayMs = 200, dpi = form.DeviceDpi, nativeOnly = native,
        checks = new[] { "Native empty window", "Actual Windows UI culture and system language preference" }
      }));
      form.Close();
    }
  }
}
#endif
