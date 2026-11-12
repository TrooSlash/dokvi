using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MdReader {
  static class WindowsIntegration {
    internal const string AppId = "Dokvi.Desktop";
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    internal static void Initialize() {
      try { SetCurrentProcessExplicitAppUserModelID(AppId); } catch (EntryPointNotFoundException) {}
    }

    internal static void RegisterFileTypes() {
      string executable = Assembly.GetExecutingAssembly().Location;
      string source = Path.Combine(Program.CacheRoot, "ui", "document.ico");
      string directory = Path.Combine(Program.DataRoot, "shell");
      string icon = Path.Combine(directory, "document.ico");
      Directory.CreateDirectory(directory);
      byte[] bytes = File.ReadAllBytes(source);
      bool changed = !File.Exists(icon) || Program.Hash(File.ReadAllBytes(icon)) != Program.Hash(bytes);
      if (changed) File.WriteAllBytes(icon, bytes);
      changed |= WriteFileTypes(Registry.CurrentUser, executable, icon);
      if (changed) SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    // Passing a registry root lets tests exercise real registry behavior in an
    // isolated subtree. No extension default or Explorer UserChoice is written.
    internal static bool WriteFileTypes(RegistryKey user, string executable, string icon) {
      const string capabilities = @"Software\Dokvi\Capabilities";
      const string classes = @"Software\Classes\";
      const string progId = "Dokvi.Markdown";
      string application = classes + @"Applications\" + Path.GetFileName(executable);
      string command = "\"" + executable + "\" \"%1\"";
      string documentIcon = "\"" + icon + "\",0";
      bool changed = false;
      changed |= SetValue(user, capabilities, "ApplicationName", "Dokvi");
      changed |= SetValue(user, capabilities, "ApplicationDescription", L.T("Чтение и редактирование Markdown"));
      changed |= SetValue(user, capabilities, "ApplicationIcon", "\"" + executable + "\",0");
      changed |= SetValue(user, @"Software\RegisteredApplications", "Dokvi", capabilities);
      changed |= SetValue(user, classes + progId, "", L.T("Markdown-документ Dokvi"));
      changed |= SetValue(user, classes + progId, "FriendlyTypeName", L.T("Markdown-документ Dokvi"));
      changed |= SetValue(user, classes + progId + @"\DefaultIcon", "", documentIcon);
      changed |= SetValue(user, classes + progId + @"\shell\open\command", "", command);
      changed |= SetValue(user, classes + progId + @"\shell\open", "MultiSelectModel", "Document");
      changed |= SetValue(user, application, "FriendlyAppName", "Dokvi");
      changed |= SetValue(user, application + @"\DefaultIcon", "", documentIcon);
      changed |= SetValue(user, application + @"\shell\open\command", "", command);
      changed |= SetValue(user, application + @"\shell\open", "MultiSelectModel", "Document");
      foreach (string extension in new[] { ".md", ".markdown", ".mdown", ".mkd" }) {
        changed |= SetValue(user, classes + extension + @"\OpenWithProgids", progId, "");
        changed |= SetValue(user, application + @"\SupportedTypes", extension, "");
        changed |= SetValue(user, capabilities + @"\FileAssociations", extension, progId);
      }
      return changed;
    }

    static bool SetValue(RegistryKey root, string path, string name, string value) {
      using (var key = root.CreateSubKey(path)) {
        if (String.Equals(key.GetValue(name) as string, value, StringComparison.Ordinal)) return false;
        key.SetValue(name, value, RegistryValueKind.String);
        return true;
      }
    }

    internal static string DefaultAppsUri() {
      // Windows 11 supports a per-user application deep link. Windows 10 opens
      // the general Default Apps page; the user can select Dokvi for .md there.
      using (var system = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
      using (var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications")) {
        int build;
        if (system != null && Int32.TryParse(Convert.ToString(system.GetValue("CurrentBuildNumber")), out build) && build >= 22000
            && registered != null && registered.GetValue("Dokvi") != null)
          return "ms-settings:defaultapps?registeredAppUser=Dokvi";
      }
      return "ms-settings:defaultapps";
    }

    internal static void OpenDefaultApps() {
#if !TESTING
      RegisterFileTypes();
#endif
      Process.Start(new ProcessStartInfo(DefaultAppsUri()) { UseShellExecute = true });
    }
  }
}
