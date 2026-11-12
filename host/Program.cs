using System;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("Dokvi")]
[assembly: AssemblyProduct("Dokvi")]
[assembly: AssemblyCompany("TrooSlash")]
[assembly: AssemblyCopyright("Copyright (c) 2026 TrooSlash")]
[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("3.0.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

namespace MdReader {
  static class Program {
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 70 * 1024 * 1024, RecursionLimit = 100 };
    internal static string DataRoot, CacheRoot;
    internal static string[] Arguments;
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string path);
    [DllImport("user32")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [STAThread]
    static void Main(string[] args) {
      try {
        WindowsIntegration.Initialize();
        args = args.Select(argument => !argument.StartsWith("--") && FileStore.TextExtensions.Contains(Path.GetExtension(argument)) ? Path.GetFullPath(argument) : argument).ToArray();
        Arguments = args;
        DataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MD Reader Native");
#if TESTING
        DataRoot = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "test-data", DateTime.UtcNow.Ticks.ToString());
        string sessionRoot = args.FirstOrDefault(argument => argument.StartsWith("--session-root="));
        if (sessionRoot != null) {
          string root = Path.GetFullPath(sessionRoot.Substring("--session-root=".Length));
          string testRoot = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "test-data") + Path.DirectorySeparatorChar;
          if (!root.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Test root must stay inside test-data.");
          DataRoot = root;
        }
#endif
        Directory.CreateDirectory(DataRoot);
        L.LoadPreference();
#if TESTING
        string systemLanguage = args.FirstOrDefault(argument => argument.StartsWith("--system-language="));
        if (systemLanguage != null) L.SystemLanguage = systemLanguage.Substring("--system-language=".Length);
        if (args.Contains("--test-storage")) { StorageTests.Run(); return; }
#endif
#if !TESTING
        // Explicit shell integration works without loading WebView or opening
        // a window, even while another Dokvi process owns the session mutex.
        if (args.Length == 1 && (args[0] == "--register-file-types" || args[0] == "--default-apps")) {
          ExtractAssets();
          if (args[0] == "--default-apps") WindowsIntegration.OpenDefaultApps();
          else WindowsIntegration.RegisterFileTypes();
          return;
        }
#endif
        string key = Hash(Encoding.UTF8.GetBytes(DataRoot)).Substring(0, 20);
        bool created;
        using (var mutex = new Mutex(true, "Local\\MDReader.Native." + key, out created)) {
          if (!created) { Forward(key, args); return; }
          ExtractAssets();
          AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) {
            string name = new AssemblyName(e.Name).Name;
            if (name == "Microsoft.Web.WebView2.Core" || name == "Microsoft.Web.WebView2.WinForms") return Assembly.LoadFrom(Path.Combine(CacheRoot, name + ".dll"));
            return null;
          };
          if (LoadLibrary(Path.Combine(CacheRoot, "WebView2Loader.dll")) == IntPtr.Zero) throw new IOException(L.T("Не удалось загрузить WebView2Loader."));
          try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) {}
          Application.EnableVisualStyles();
          Application.SetCompatibleTextRenderingDefault(false);
#if TESTING
          Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
          Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) {
            string details = DateTime.UtcNow.ToString("o") + "\r\n" + String.Join(" ", args) + "\r\n" + e.Exception;
            File.WriteAllText(Path.Combine(DataRoot, "unhandled-exception.txt"), details);
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-unhandled-exception.txt"), details);
            System.Environment.Exit(1);
          };
          if (args.Contains("--test-focus-routing")) { FocusRoutingTests.Run(); return; }
#endif
          Run(key, args);
          mutex.ReleaseMutex();
        }
      } catch (Exception error) {
        Environment.ExitCode = 1;
#if TESTING
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "native-test-error.txt"), error.ToString());
#else
        MessageBox.Show(error.Message, "Dokvi", MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif
      }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run(string key, string[] args) {
      var window = new ReaderForm(args);
      Task.Run(async delegate {
        while (!window.IsDisposed) {
          try {
            using (var pipe = new NamedPipeServerStream("MDReader.Native." + key, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            using (var reader = new StreamReader(pipe, Encoding.UTF8)) {
              await pipe.WaitForConnectionAsync();
              string line = await reader.ReadLineAsync();
              if (line != null && line.Length < 65536 && !window.IsDisposed) {
                string[] incoming = Json.Deserialize<string[]>(line);
                while (!window.IsHandleCreated && !window.IsDisposed) await Task.Delay(20);
                if (!window.IsDisposed) window.BeginInvoke(new Action(delegate { window.ReceiveArguments(incoming); }));
              }
            }
          } catch (IOException) {} catch (ObjectDisposedException) { return; } catch (InvalidOperationException) { if (window.IsDisposed) return; }
        }
      });
      Application.Run(window);
    }
    static void Forward(string key, string[] args) {
      using (var pipe = new NamedPipeClientStream(".", "MDReader.Native." + key, PipeDirection.Out)) {
        pipe.Connect(5000);
        using (var writer = new StreamWriter(pipe, new UTF8Encoding(false))) { writer.WriteLine(Json.Serialize(args)); }
      }
    }
    internal static string Hash(byte[] bytes) {
      using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    static void ExtractAssets() {
      var assembly = Assembly.GetExecutingAssembly();
      string fingerprint;
      using (var stream = assembly.GetManifestResourceStream("bundle.zip"))
      using (var sha = SHA256.Create()) fingerprint = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").Substring(0, 16);
      CacheRoot = Path.Combine(DataRoot, "assets", "2.0-" + fingerprint);
      string marker = Path.Combine(CacheRoot, ".complete");
      if (File.Exists(marker)) return;
      Directory.CreateDirectory(CacheRoot);
      using (var stream = assembly.GetManifestResourceStream("bundle.zip"))
      using (var archive = new ZipArchive(stream, ZipArchiveMode.Read)) {
        foreach (var entry in archive.Entries) {
          if (entry.Name.Length == 0) continue;
          string destination = Path.GetFullPath(Path.Combine(CacheRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
          if (!destination.StartsWith(CacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException(L.T("Недопустимый путь в ресурсах приложения."));
          Directory.CreateDirectory(Path.GetDirectoryName(destination));
          using (var source = entry.Open()) using (var output = File.Create(destination)) source.CopyTo(output);
        }
      }
      File.WriteAllText(marker, fingerprint);
    }
  }
}
