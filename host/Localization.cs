using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace MdReader {
  static class L {
    static readonly Dictionary<string, string> English = LoadCatalog();
    internal static string SystemLanguage = CultureInfo.CurrentUICulture.Name;
    internal static string Preference = "system";
    internal static string Language { get { return Resolve(Preference, SystemLanguage); } }
    internal static string Resolve(string preference, string systemLanguage) {
      if (preference == "en" || preference == "ru") return preference;
      string code = (systemLanguage ?? "").Split('-', '_')[0];
      return String.Equals(code, "ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
    }
    static Dictionary<string, string> LoadCatalog() {
      using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("strings.en.json"))
      using (var reader = new StreamReader(stream)) return Program.Json.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
    }
    internal static void LoadPreference() {
      try {
        var file = Path.Combine(Program.DataRoot, "settings.json");
        if (!File.Exists(file)) return;
        var settings = Program.Json.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
        string value;
        if (settings.TryGetValue("language", out value)) SetPreference(value);
      } catch { }
    }
    internal static void SetPreference(string value) { Preference = value == "ru" || value == "en" ? value : "system"; }
    internal static string T(string source, params object[] args) {
      string translated;
      if (Language == "en" && English.TryGetValue(source, out translated)) source = translated;
      return args.Length == 0 ? source : String.Format(CultureInfo.InvariantCulture, source, args);
    }
  }
}
