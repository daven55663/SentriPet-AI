using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SentriPet
{
    /// <summary>
    /// A tiny web server for OBS and scripts (#22), reachable only from this computer (it listens on 127.0.0.1):
    ///   /            a page with a transparent background for OBS's browser source (usage bars, or the pet with ?view=pet)
    ///   /usage.json  the same JSON as usage.json
    ///   /pet.png     the widget as it looks right now
    /// Only GET, only with a Host of 127.0.0.1 / localhost (a web page elsewhere cannot read it through DNS tricks),
    /// no cross-origin headers, one short request per connection.
    /// </summary>
    class UsageServer : IDisposable
    {
        public const int DefaultPort = 47291;
        const int MaxHeader = 8192;

        readonly Func<string> json;
        readonly Func<byte[]> petPng;
        TcpListener listener;
        CancellationTokenSource stop;

        public int Port { get; private set; }
        public bool Running { get { return listener != null; } }

        public UsageServer(Func<string> json, Func<byte[]> petPng)
        {
            this.json = json;
            this.petPng = petPng;
        }

        /// <summary>Starts listening (port 0 = any free port, for tests). False, with the reason logged, when the port is taken.</summary>
        public bool Start(int port)
        {
            Stop();
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                listener = l;
                Port = ((IPEndPoint)l.LocalEndpoint).Port;
                stop = new CancellationTokenSource();
                var token = stop.Token;
                Task.Run(() => AcceptLoop(l, token));
                Log.Info("usage server on http://127.0.0.1:" + Port + "/");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("usage server: port " + port + ": " + ex.Message);
                listener = null;
                return false;
            }
        }

        public void Stop()
        {
            if (listener == null) return;
            try { stop.Cancel(); } catch { }
            try { listener.Stop(); } catch { }
            listener = null;
        }

        public void Dispose() { Stop(); }

        public string Url { get { return "http://127.0.0.1:" + Port + "/"; } }

        async Task AcceptLoop(TcpListener l, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await l.AcceptTcpClientAsync(token).ConfigureAwait(false); }
                catch { return; }   // stopped
                _ = Task.Run(() => Serve(client));
            }
        }

        void Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 3000;
                    client.SendTimeout = 5000;
                    var stream = client.GetStream();
                    string head = ReadHead(stream);
                    if (head == null) return;
                    string contentType;
                    int status;
                    byte[] body = Respond(head, out status, out contentType);
                    bool headOnly = head.StartsWith("HEAD ", StringComparison.Ordinal);
                    var sb = new StringBuilder();
                    sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reason(status)).Append("\r\n");
                    sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
                    sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
                    sb.Append("Cache-Control: no-store\r\n");
                    sb.Append("X-Content-Type-Options: nosniff\r\n");
                    sb.Append("Connection: close\r\n\r\n");
                    var h = Encoding.ASCII.GetBytes(sb.ToString());
                    stream.Write(h, 0, h.Length);
                    if (!headOnly) stream.Write(body, 0, body.Length);
                    stream.Flush();
                }
                catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException) { }
                catch (Exception ex) { Log.Warn("usage server: " + ex.Message); }
            }
        }

        /// <summary>The request line and headers, up to the empty line (null when the client sent nothing sensible).</summary>
        static string ReadHead(Stream stream)
        {
            var buf = new byte[MaxHeader];
            int n = 0;
            while (n < buf.Length)
            {
                int r = stream.Read(buf, n, buf.Length - n);
                if (r <= 0) break;
                n += r;
                string s = Encoding.ASCII.GetString(buf, 0, n);
                int end = s.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (end >= 0) return s.Substring(0, end);
            }
            return null;
        }

        /// <summary>The answer to one request (separate from the socket, for the tests).</summary>
        internal byte[] Respond(string head, out int status, out string contentType)
        {
            contentType = "text/plain; charset=utf-8";
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var parts = lines[0].Split(' ');
            if (parts.Length < 3) { status = 400; return Text("bad request"); }
            string method = parts[0], target = parts[1];
            string host = null;
            for (int i = 1; i < lines.Length; i++)
            {
                int c = lines[i].IndexOf(':');
                if (c > 0 && lines[i].Substring(0, c).Trim().Equals("Host", StringComparison.OrdinalIgnoreCase))
                    host = lines[i].Substring(c + 1).Trim();
            }
            if (!HostAllowed(host)) { status = 403; return Text("only for this computer: use http://127.0.0.1:" + Port + "/"); }
            if (method != "GET" && method != "HEAD") { status = 405; return Text("only GET"); }
            string path = target;
            int q = path.IndexOfAny(new[] { '?', '#' });
            if (q >= 0) path = path.Substring(0, q);

            if (path == "/" || path == "/overlay" || path == "/overlay.html")
            {
                status = 200;
                contentType = "text/html; charset=utf-8";
                return Encoding.UTF8.GetBytes(OverlayHtml);
            }
            if (path == "/usage.json")
            {
                string j = json();
                if (j == null) { status = 503; return Text("starting"); }
                status = 200;
                contentType = "application/json; charset=utf-8";
                return Encoding.UTF8.GetBytes(j);
            }
            if (path == "/pet.png")
            {
                byte[] png = null;
                try { png = petPng != null ? petPng() : null; }
                catch (Exception ex) { Log.Warn("pet.png: " + ex.Message); }
                if (png == null) { status = 404; return Text("no picture"); }
                status = 200;
                contentType = "image/png";
                return png;
            }
            status = 404;
            return Text("not found");
        }

        /// <summary>127.0.0.1, localhost or [::1], with this server's port (or none).</summary>
        internal bool HostAllowed(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            string name = host, port = null;
            if (host.StartsWith("[", StringComparison.Ordinal))
            {
                int close = host.IndexOf(']');
                if (close < 0) return false;
                name = host.Substring(0, close + 1);
                if (close + 1 < host.Length) { if (host[close + 1] != ':') return false; port = host.Substring(close + 2); }
            }
            else
            {
                int c = host.LastIndexOf(':');
                if (c >= 0) { name = host.Substring(0, c); port = host.Substring(c + 1); }
            }
            name = name.ToLowerInvariant();
            if (name != "127.0.0.1" && name != "localhost" && name != "[::1]") return false;
            return port == null || port == Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        static byte[] Text(string s) { return Encoding.UTF8.GetBytes(s + "\n"); }

        static string Reason(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 400: return "Bad Request";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 503: return "Service Unavailable";
                default: return "Error";
            }
        }

        static string overlay;

        /// <summary>The OBS page (built in, the resource SentriPet.overlay.html).</summary>
        public static string OverlayHtml
        {
            get
            {
                if (overlay != null) return overlay;
                using (var s = typeof(UsageServer).Assembly.GetManifestResourceStream("SentriPet.overlay.html"))
                    overlay = s == null ? "<!doctype html><title>SentriPet</title>" : new StreamReader(s, Encoding.UTF8).ReadToEnd();
                return overlay;
            }
        }
    }
}
