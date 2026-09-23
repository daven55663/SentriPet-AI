using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SentriPet
{
    /// <summary>A tiny test harness for --selftest: named checks grouped in sections, results as text.</summary>
    class TestKit
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        readonly StringBuilder report = new StringBuilder();
        readonly List<string> temps = new List<string>();
        public int Passed, Failed, Skipped;

        public string Report { get { return report.ToString(); } }

        public void Section(string name)
        {
            report.AppendLine().AppendLine("== " + name);
        }

        public bool Check(string name, bool ok, string detail)
        {
            if (ok) Passed++; else Failed++;
            report.AppendLine((ok ? "PASS " : "FAIL ") + name + (string.IsNullOrEmpty(detail) ? "" : "  — " + detail));
            return ok;
        }

        public bool Check(string name, bool ok) { return Check(name, ok, null); }

        public void Skip(string name, string reason)
        {
            Skipped++;
            report.AppendLine("SKIP " + name + "  — " + reason);
        }

        public bool Equal(string name, object expected, object actual)
        {
            bool ok = Equals(expected, actual);
            return Check(name, ok, ok ? Show(actual) : "expected " + Show(expected) + ", got " + Show(actual));
        }

        public bool Near(string name, double expected, double actual, double tolerance)
        {
            bool ok = Math.Abs(expected - actual) <= tolerance;
            return Check(name, ok, (ok ? "" : "expected " + expected.ToString("G6", CI) + " ± " + tolerance.ToString("G3", CI) + ", got ") + actual.ToString("G6", CI));
        }

        public bool Contains(string name, string text, string part)
        {
            bool ok = text != null && part != null && text.Contains(part);
            return Check(name, ok, ok ? Short(text) : "\"" + part + "\" not in " + Show(text));
        }

        public bool Close(string name, DateTime? expected, DateTime? actual, TimeSpan tolerance)
        {
            bool ok = expected.HasValue && actual.HasValue && Math.Abs((expected.Value - actual.Value).TotalSeconds) <= tolerance.TotalSeconds;
            return Check(name, ok, ok ? Show(actual) : "expected " + Show(expected) + " ± " + tolerance.TotalMinutes.ToString("0.#", CI) + " min, got " + Show(actual));
        }

        /// <summary>Runs a block of checks; an exception counts as one failure and ends only that block.</summary>
        public void Run(string name, Action body)
        {
            try { body(); }
            catch (Exception ex)
            {
                string frame = (ex.StackTrace ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
                Check(name + "（例外）", false, ex.GetType().Name + ": " + ex.Message + " " + frame);
            }
        }

        public string TempDir(string name)
        {
            string d = Path.Combine(Path.GetTempPath(), "sentripet-test-" + name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            temps.Add(d);
            return d;
        }

        public void Cleanup()
        {
            foreach (var d in temps)
            {
                try { Directory.Delete(d, true); } catch { }
            }
        }

        public static void WriteFile(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        public static string Iso(DateTime utc) { return utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CI); }

        public static long Unix(DateTime utc) { return (long)Math.Round(Json.ToUnixMs(utc) / 1000.0); }

        /// <summary>Waits for a condition (polling), for things that run on worker threads.</summary>
        public static bool WaitUntil(Func<bool> condition, int timeoutMs)
        {
            var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < end)
            {
                if (condition()) return true;
                Thread.Sleep(50);
            }
            return condition();
        }

        static string Short(string s) { return s.Length > 90 ? s.Substring(0, 90) + "…" : s; }

        static string Show(object o)
        {
            if (o == null) return "null";
            var s = o as string;
            if (s != null) return "\"" + Short(s) + "\"";
            if (o is double) return ((double)o).ToString("0.#####", CI);
            if (o is DateTime) return ((DateTime)o).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CI) + "Z";
            if (o is DateTime?) return Show(((DateTime?)o).Value);
            return Convert.ToString(o, CI);
        }
    }

    /// <summary>A provider whose detection and data are supplied by the test.</summary>
    class StubProvider : Provider
    {
        public Func<Detection> OnDetect;
        public Func<bool, AppSettings, Snapshot> OnFetch;
        public int Fetches;

        public StubProvider(string id, string name)
        {
            Id = id;
            Name = name;
            Color = Palette.FromId(id);
        }

        public override int IntervalSeconds { get { return 1; } }

        public override Detection Detect()
        {
            return OnDetect != null ? OnDetect() : new Detection { Installed = true };
        }

        public override Snapshot Fetch(bool force, AppSettings settings)
        {
            Interlocked.Increment(ref Fetches);
            return OnFetch != null ? OnFetch(force, settings) : null;
        }
    }

    /// <summary>A minimal HTTP server on 127.0.0.1 (a raw socket, so no admin rights or URL reservations needed).</summary>
    class FakeHttpServer : IDisposable
    {
        readonly TcpListener listener;
        readonly Dictionary<string, KeyValuePair<int, string>> routes = new Dictionary<string, KeyValuePair<int, string>>();
        public readonly List<string> Requests = new List<string>();
        volatile bool stopped;

        public int Port { get; private set; }

        /// <param name="port">0 = any free port</param>
        public FakeHttpServer(int port)
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            new Thread(Loop) { IsBackground = true }.Start();
        }

        public void Route(string path, int status, string body)
        {
            lock (routes) routes[path] = new KeyValuePair<int, string>(status, body);
        }

        void Loop()
        {
            while (!stopped)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); }
                catch { return; }
                try { Serve(c); }
                catch { }
                finally { try { c.Close(); } catch { } }
            }
        }

        void Serve(TcpClient c)
        {
            c.ReceiveTimeout = 5000;
            var stream = c.GetStream();
            var head = new StringBuilder();
            var one = new byte[1];
            while (head.Length < 65536)
            {
                if (stream.Read(one, 0, 1) <= 0) break;
                head.Append((char)one[0]);
                if (head.Length >= 4 && head[head.Length - 1] == '\n' && head[head.Length - 2] == '\r' && head[head.Length - 3] == '\n') break;
            }
            string text = head.ToString();
            lock (Requests) Requests.Add(text);
            string[] first = text.Split(new[] { "\r\n" }, StringSplitOptions.None)[0].Split(' ');
            string path = first.Length >= 2 ? first[1] : "/";
            KeyValuePair<int, string> resp;
            bool found;
            lock (routes) found = routes.TryGetValue(path, out resp);
            if (!found) resp = new KeyValuePair<int, string>(404, "{\"error\":\"not found\"}");
            var body = Encoding.UTF8.GetBytes(resp.Value);
            var hdr = Encoding.ASCII.GetBytes("HTTP/1.1 " + resp.Key + " " + (resp.Key < 400 ? "OK" : "Error") +
                "\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
            stream.Write(hdr, 0, hdr.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        public void Dispose()
        {
            stopped = true;
            try { listener.Stop(); } catch { }
        }
    }

    /// <summary>
    /// --fake-codex-app-server result.json: a stand-in for "codex app-server" used by the self-test. Speaks the same
    /// line-based JSON-RPC over stdin/stdout and answers account/rateLimits/read with the file's content.
    /// </summary>
    static class FakeCodexServer
    {
        public static int Run(string resultFile)
        {
            string result = "{}";
            if (resultFile != null && File.Exists(resultFile))
                result = Json.Serialize(Json.Parse(File.ReadAllText(resultFile, Encoding.UTF8)), false);   // one line
            var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            // a notification before any reply: the client has to skip it
            stdout.WriteLine("{\"method\":\"codex/event\",\"params\":{\"msg\":\"fake app-server ready\"}}");
            string line;
            while ((line = stdin.ReadLine()) != null)
            {
                var o = Json.TryParse(line);
                var id = Json.Get(o, "id");
                if (o == null || id == null) continue;   // blank lines and notifications ("initialized")
                string method = Json.Str(Json.Get(o, "method"));
                string rid = Json.Serialize(id, false);
                if (method == "initialize") stdout.WriteLine("{\"id\":" + rid + ",\"result\":{\"userAgent\":\"fake-codex/0.0\"}}");
                else if (method == "account/rateLimits/read") stdout.WriteLine("{\"id\":" + rid + ",\"result\":" + result + "}");
                else stdout.WriteLine("{\"id\":" + rid + ",\"error\":{\"message\":\"unknown method\"}}");
            }
            return 0;
        }
    }
}
