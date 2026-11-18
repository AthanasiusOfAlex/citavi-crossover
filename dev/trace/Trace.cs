// CitaviTrace — logs every .NET exception Citavi throws, including the ones it catches and hides.
//
// Nothing in Citavi is modified. The .NET Framework loads this class before Citavi's own code when
// two environment variables are set (citavi-crossover does this for `run --trace` and
// `diagnose --trace`):
//     APPDOMAIN_MANAGER_ASM  = CitaviTrace, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
//     APPDOMAIN_MANAGER_TYPE = CitaviTrace.Manager
// and CitaviTrace.dll sits next to Citavi.exe. Output: the file named by CITAVI_TRACE_FILE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

[assembly: AssemblyVersion("1.0.0.0")]

namespace CitaviTrace
{
    public sealed class Manager : AppDomainManager
    {
        static readonly object Gate = new object();
        static readonly Dictionary<string, int> Seen = new Dictionary<string, int>();
        static string _file;
        static int _frames = 24;
        [ThreadStatic] static bool _busy;

        // Tokens must never reach the log: JWTs, bearer headers, and anything after a URL's ? or #.
        static readonly Regex Secret = new Regex(
            @"eyJ[A-Za-z0-9_\-]{8,}(\.[A-Za-z0-9_\-]+)*|(?i:bearer)\s+\S+|(?<=[?#&](?i:access_token|id_token|refresh_token|code|sig|token|key)=)[^&\s""']+",
            RegexOptions.Compiled);

        public override void InitializeNewDomain(AppDomainSetup appDomainInfo)
        {
            try
            {
                _file = Environment.GetEnvironmentVariable("CITAVI_TRACE_FILE");
                if (string.IsNullOrEmpty(_file)) return;
                int n; if (int.TryParse(Environment.GetEnvironmentVariable("CITAVI_TRACE_FRAMES"), out n) && n > 0) _frames = n;
                Directory.CreateDirectory(Path.GetDirectoryName(_file));
                Write("=== trace started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ", domain " + AppDomain.CurrentDomain.FriendlyName + " ===\r\n");
                AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Write(Stamp() + " UNHANDLED " + Clean(Convert.ToString(e.ExceptionObject)) + "\r\n\r\n");
            }
            catch { }
        }

        static string Stamp() { return DateTime.Now.ToString("HH:mm:ss.fff") + " [t" + Thread.CurrentThread.ManagedThreadId + "]"; }
        static string Clean(string s) { return s == null ? "" : Secret.Replace(s, "<redacted>"); }

        static void Write(string text)
        {
            lock (Gate) { try { File.AppendAllText(_file, text, Encoding.UTF8); } catch { } }
        }

        static void OnFirstChance(object sender, FirstChanceExceptionEventArgs e)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                Exception ex = e.Exception;
                var sb = new StringBuilder();
                var st = new StackTrace(1, false);
                int shown = 0;
                for (int i = 0; i < st.FrameCount && shown < _frames; i++)
                {
                    MethodBase m = st.GetFrame(i).GetMethod();
                    if (m == null) continue;
                    string type = m.DeclaringType == null ? "" : m.DeclaringType.FullName + ".";
                    if (type.StartsWith("CitaviTrace.")) continue;
                    sb.Append("    at ").Append(type).Append(m.Name).Append("\r\n");
                    shown++;
                }
                string where = sb.ToString();
                string head = ex.GetType().FullName + ": " + Clean(ex.Message);
                for (Exception inner = ex.InnerException; inner != null; inner = inner.InnerException)
                    head += "\r\n    <- " + inner.GetType().FullName + ": " + Clean(inner.Message);
                string key = head + "\n" + where;
                int count;
                lock (Gate) { Seen.TryGetValue(key, out count); Seen[key] = ++count; }
                if (count == 1) Write(Stamp() + " " + head + "\r\n" + where + "\r\n");
                else if (count == 2 || count == 10 || count % 100 == 0) Write(Stamp() + " (x" + count + ") " + head + "\r\n\r\n");
            }
            catch { }
            finally { _busy = false; }
        }
    }
}
