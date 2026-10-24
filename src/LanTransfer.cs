using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace FolderSyncTool
{
    public class TransferProgress
    {
        public int FileIndex;
        public int FileCount;
        public string FileName = "";
        public string Phase = "sending";   // hashing | sending | done | failed | cancelled
        public long BytesSent;
        public long BytesTotal;
        public double BytesPerSec;
        public TimeSpan Eta;
        public int Percent;
    }

    /// <summary>
    /// LAN TCP transfer engine (v5 protocol):
    ///   FILE   | b64(name) | size | sha256 | enc | b64(from)   -> ACK|offset -> payload -> DONE
    ///   CHAT   | b64(from) | enc                              -> payload(text)
    ///   TASK   | b64(from) | enc                              -> payload(wire line)
    ///   TASKDONE| b64(from)| enc                              -> payload(taskId)
    ///   INFO   | b64(name)                                    -> INFO reply
    /// enc=1 payloads are AES-256-CBC: [16B IV][ciphertext]. Encrypted file
    /// payloads cannot resume (CBC offset constraints); plain ones can.
    /// </summary>
    public class LanTransfer : IDisposable
    {
        public static string ReceiveRootOverride = null;   // test hook

        public string MachineName = Environment.MachineName;
        public Func<byte[]> KeyProvider;                   // -> 32-byte AES key
        public Func<bool> EncryptEnabled;
        public Func<bool> KeepReceivedVersions;
        public Func<int> VersionCount;

        public event Action<string, string> ChatReceived;   // from, text
        public event Action<string, WorkItem> TaskReceived; // from, item
        public event Action<string, string> TaskDoneReceived;
        public event Action<string, string> FileReceived;   // from, fullPath

        /// <summary>Diagnostics hook: receive-side errors (wire, crypto, IO).</summary>
        public static Action<string> ReceiveError;

        /// <summary>Diagnostics hook: send-side errors (wire, crypto, IO).</summary>
        public static Action<string> SendError;

        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;

        public const int ChunkSize = 64 * 1024;

        // ------------------------------------------------------------------
        // sender
        // ------------------------------------------------------------------

        public static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "file";
            char[] invalid = new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
            StringBuilder sb = new StringBuilder();
            foreach (char c in name)
            {
                if (Array.IndexOf(invalid, c) >= 0 || c < 32) sb.Append('_');
                else sb.Append(c);
            }
            string s = sb.ToString().TrimEnd('.', ' ');
            if (s.Length == 0) s = "file";
            string upper = s.ToUpperInvariant();
            if (upper == "CON" || upper == "PRN" || upper == "AUX" || upper == "NUL"
                || (upper.StartsWith("COM") && IsDigits(upper.Substring(3)))
                || (upper.StartsWith("LPT") && IsDigits(upper.Substring(3))))
                s = "_" + s;
            return s;
        }

        private static bool IsDigits(string s)
        {
            // only COM1..COM9 / LPT1..LPT9 are reserved, so exactly one digit
            if (s.Length != 1) return false;
            return s[0] >= '0' && s[0] <= '9';
        }

        public static string ReceiveRoot(string fromName)
        {
            if (ReceiveRootOverride != null)
                return Path.Combine(ReceiveRootOverride, SafeName(fromName ?? "pc"));
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(docs, "FolderSyncTool", "Received", SafeName(fromName ?? "pc"));
        }

        public static byte[] ComputeSha256(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(fs);
            }
        }

        public static string ToHex(byte[] hash)
        {
            StringBuilder sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>Human-readable transfer rate for the progress label.</summary>
        public static string FormatSpeed(double bytesPerSec)
        {
            if (bytesPerSec <= 0) return "";
            const double KB = 1024, MB = KB * 1024, GB = MB * 1024;
            if (bytesPerSec >= GB) return (bytesPerSec / GB).ToString("0.0") + " GB/s";
            if (bytesPerSec >= MB) return (bytesPerSec / MB).ToString("0.0") + " MB/s";
            if (bytesPerSec >= KB) return (bytesPerSec / KB).ToString("0.0") + " KB/s";
            return bytesPerSec.ToString("0") + " B/s";
        }

        /// <summary>
        /// Send files to one peer with progress (percent/speed/ETA), cancel
        /// support, SHA-256 integrity, and resume of interrupted plain sends.
        /// </summary>
        public bool SendFiles(string[] paths, PeerInfo peer,
                              Action<TransferProgress> progress, CancellationToken ct)
        {
            if (paths == null || paths.Length == 0 || peer == null) return false;
            byte[] key = (EncryptEnabled != null && EncryptEnabled()) ? KeyProviderSafe() : null;
            bool ok = true;

            for (int i = 0; i < paths.Length; i++)
            {
                if (ct.IsCancellationRequested) { ok = false; ReportCancelled(progress, i, paths.Length); break; }
                string path = paths[i];
                string name = Path.GetFileName(path);
                var pr = new TransferProgress
                {
                    FileIndex = i, FileCount = paths.Length, FileName = name
                };

                try
                {
                    long size = new FileInfo(path).Length;

                    // phase 1: hash (progress 0..10)
                    pr.Phase = "hashing"; pr.Percent = 0; pr.BytesTotal = size;
                    if (progress != null) progress(pr);
                    string sha = ToHex(ComputeSha256(path));
                    pr.Phase = "sending"; pr.Percent = 2;
                    if (progress != null) progress(pr);

                    // phase 2: transfer (progress 2..100)
                    bool sent = SendOneFile(path, peer.Ip, name, size, sha, key, pr,
                                            progress, ct);
                    if (!sent) { ok = false; break; }
                }
                catch (OperationCanceledException)
                {
                    pr.Phase = "cancelled"; pr.Percent = 0;
                    if (progress != null) progress(pr);
                    ok = false;
                    break;
                }
                catch (Exception ex)
                {
                    Action<string> sink = SendError;
                    if (sink != null) sink(pr.FileName + " @phase=" + pr.Phase + ": " +
                        ex.GetType().Name + ": " + ex.Message);
                    pr.Phase = "failed"; pr.Percent = 0; pr.BytesPerSec = 0; pr.Eta = TimeSpan.Zero;
                    if (progress != null) progress(pr);
                    ok = false;
                }
            }
            return ok;
        }

        private byte[] KeyProviderSafe()
        {
            byte[] k = KeyProvider != null ? KeyProvider() : null;
            if (k == null || k.Length != 32)
                throw new InvalidOperationException("AES key missing or not 32 bytes");
            return k;
        }

        private void ReportCancelled(Action<TransferProgress> progress, int i, int n)
        {
            if (progress == null) return;
            progress(new TransferProgress
            {
                FileIndex = i, FileCount = n, FileName = "", Phase = "cancelled"
            });
        }

        private bool SendOneFile(string path, string ip, string name, long size, string sha,
                                 byte[] key, TransferProgress pr,
                                 Action<TransferProgress> progress, CancellationToken ct)
        {
            using (TcpClient client = new TcpClient())
            {
                IAsyncResult ar = client.BeginConnect(ip, LanDiscovery.TcpPort, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(5000))
                    throw new IOException("connect timeout");
                client.EndConnect(ar);

                using (NetworkStream ns = client.GetStream())
                {
                    ns.ReadTimeout = 10000;
                    ns.WriteTimeout = 10000;

                    bool enc = key != null;
                    string head = LanDiscovery.Proto + "|FILE|" + LanDiscovery.B64(name)
                        + "|" + size + "|" + sha + "|" + (enc ? "1" : "0")
                        + "|" + LanDiscovery.B64(MachineName);
                    byte[] headBytes = Encoding.UTF8.GetBytes(head + "\n");
                    ns.Write(headBytes, 0, headBytes.Length);
                    ns.Flush();

                    string ack = LanDiscovery.ReadLine(ns, 8000);
                    if (ack == null) throw new IOException("no ACK from receiver");
                    if (ack.StartsWith("FAIL"))
                        throw new IOException("receiver refused: " + LanDiscovery.UnB64(
                            ack.Length > 5 ? ack.Substring(5) : ""));

                    long offset = 0;
                    string[] ap = ack.Split('|');
                    if (ap.Length >= 2 && ap[0] == "ACK")
                        long.TryParse(ap[1], out offset);
                    if (offset < 0 || offset > size) offset = 0;
                    if (enc && offset > 0) offset = 0; // encrypted streams can't resume

                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (offset > 0) fs.Seek(offset, SeekOrigin.Begin);
                        long remaining = size - offset;
                        if (enc)
                        {
                            using (Aes aes = Aes.Create())
                            {
                                aes.Key = key;
                                aes.GenerateIV();
                                ns.Write(aes.IV, 0, 16); // plaintext IV prefix
                                using (ICryptoTransform t = aes.CreateEncryptor())
                                using (CryptoStream cs = new CryptoStream(ns, t, CryptoStreamMode.Write, true))
                                {
                                    Pump(fs, cs, remaining, pr, progress, ct, offset, size);
                                    cs.FlushFinalBlock();
                                }
                            }
                        }
                        else
                        {
                            Pump(fs, ns, remaining, pr, progress, ct, offset, size);
                        }
                    }
                    ns.Flush();

                    // phase 3: wait for DONE (best effort).
                    // NOTE: a receiver-refused result must NOT use IOException — that
                    // same type signals benign timeouts below and would be swallowed.
                    string receiverError = null;
                    try
                    {
                        string done = LanDiscovery.ReadLine(ns, 5000);
                        if (done != null && done.StartsWith("FAIL"))
                            receiverError = done.Contains("|")
                                ? LanDiscovery.UnB64(done.Substring(5)) : "integrity";
                    }
                    catch (IOException)
                    {
                        // old peer / closed early — treat as success, receiver keeps part for resume
                    }
                    if (receiverError != null)
                        throw new InvalidOperationException("receiver failed: " + receiverError);
                }
            }

            pr.Phase = "done"; pr.Percent = 100;
            pr.BytesSent = pr.BytesTotal; pr.BytesPerSec = 0; pr.Eta = TimeSpan.Zero;
            if (progress != null) progress(pr);
            return true;
        }

        private static void Pump(Stream src, Stream dst, long remaining,
                                 TransferProgress pr, Action<TransferProgress> progress,
                                 CancellationToken ct, long offset, long total)
        {
            byte[] buf = new byte[ChunkSize];
            var runStart = Stopwatch.StartNew();
            long sentSinceReport = 0;
            long sentTotal = 0;
            double ema = 0;
            DateTime lastReport = DateTime.UtcNow;
            bool reported = false;

            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();
                int want = (int)Math.Min(buf.Length, remaining);
                int n = src.Read(buf, 0, want);
                if (n <= 0) break;
                dst.Write(buf, 0, n);
                remaining -= n;
                sentSinceReport += n;
                sentTotal += n;

                DateTime now = DateTime.UtcNow;
                double dt = (now - lastReport).TotalSeconds;
                bool final = remaining == 0;

                if (final || dt >= 0.2)
                {
                    // guard: very fast transfers reach the final report before the
                    // 200ms window — fall back to the run average so speed/ETA
                    // are never zero on the last tick
                    double inst = dt > 0.05
                        ? sentSinceReport / dt
                        : sentTotal / Math.Max(0.05, runStart.Elapsed.TotalSeconds);
                    ema = !reported || ema == 0 ? inst : (ema * 0.7 + inst * 0.3);
                    reported = true;
                    sentSinceReport = 0;
                    lastReport = now;

                    long done = offset + (total - remaining);
                    pr.BytesSent = done;
                    pr.BytesPerSec = ema;
                    long left = total - done;
                    pr.Eta = ema > 0 ? TimeSpan.FromSeconds(left / ema) : TimeSpan.Zero;
                    pr.Percent = total > 0 ? (int)Math.Min(100, done * 100 / total) : 100;
                    if (progress != null) progress(pr);
                }
            }
        }

        // ------------------------------------------------------------------
        // small messages (chat / task / taskdone) — fire and forget
        // ------------------------------------------------------------------

        public static bool SendSmall(string ip, string type, string payload, byte[] key, string fromName)
        {
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    IAsyncResult ar = c.BeginConnect(ip, LanDiscovery.TcpPort, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(3000)) return false;
                    c.EndConnect(ar);
                    using (NetworkStream ns = c.GetStream())
                    {
                        ns.WriteTimeout = 5000;
                        string head = LanDiscovery.Proto + "|" + type + "|"
                            + LanDiscovery.B64(fromName) + "|" + (key != null ? "1" : "0");
                        byte[] hb = Encoding.UTF8.GetBytes(head + "\n");
                        ns.Write(hb, 0, hb.Length);
                        byte[] pb = Encoding.UTF8.GetBytes(payload ?? "");
                        WritePayload(ns, pb, key);
                        ns.Flush();
                    }
                }
                return true;
            }
            catch { return false; }
        }

        private static void WritePayload(NetworkStream ns, byte[] payload, byte[] key)
        {
            if (key == null)
            {
                ns.Write(payload, 0, payload.Length);
            }
            else
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.GenerateIV();
                    ns.Write(aes.IV, 0, 16);
                    using (ICryptoTransform t = aes.CreateEncryptor())
                    using (CryptoStream cs = new CryptoStream(ns, t, CryptoStreamMode.Write, true))
                    {
                        cs.Write(payload, 0, payload.Length);
                        cs.FlushFinalBlock();
                    }
                }
            }
        }

        public static byte[] ReadPayload(NetworkStream ns, byte[] key)
        {
            if (key == null)
                return ReadAllRemaining(ns);
            byte[] iv = new byte[16];
            if (!ReadExactly(ns, iv, 16)) return null;
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                using (ICryptoTransform t = aes.CreateDecryptor())
                using (CryptoStream cs = new CryptoStream(ns, t, CryptoStreamMode.Read, true))
                {
                    return ReadAllRemaining(cs);
                }
            }
        }

        private static byte[] ReadAllRemaining(Stream s)
        {
            using (var ms = new MemoryStream())
            {
                byte[] buf = new byte[8192];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                return ms.ToArray();
            }
        }

        private static bool ReadExactly(NetworkStream ns, byte[] buf, int len)
        {
            int got = 0;
            while (got < len)
            {
                int n = ns.Read(buf, got, len - got);
                if (n <= 0) return false;
                got += n;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // receiver
        // ------------------------------------------------------------------

        public void Start()
        {
            if (_running) return;
            _running = true;
            _listener = new TcpListener(IPAddress.Any, LanDiscovery.TcpPort);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();
            _acceptThread = new Thread(AcceptLoop);
            _acceptThread.IsBackground = true;
            _acceptThread.Start();
        }

        public void Dispose()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient c = null;
                try { c = _listener.AcceptTcpClient(); }
                catch (SocketException) { if (!_running) break; continue; }
                catch (ObjectDisposedException) { break; }
                catch (InvalidOperationException) { break; }

                var t = new Thread(delegate () { HandleClientSafe(c); });
                t.IsBackground = true;
                t.Start();
            }
        }

        private void HandleClientSafe(TcpClient c)
        {
            try { HandleClient(c); }
            catch (Exception ex)
            {
                Action<string> sink = ReceiveError;
                if (sink != null) sink(ex.GetType().Name + ": " + ex.Message);
            }
            try { c.Close(); } catch { }
        }

        private void HandleClient(TcpClient c)
        {
            using (NetworkStream ns = c.GetStream())
            {
                ns.ReadTimeout = 30000;
                string head = LanDiscovery.ReadLine(ns, 8000);
                if (head == null) return;
                string[] p = head.Split('|');
                if (p.Length < 3 || p[0] != LanDiscovery.Proto) return;

                string type = p[1];

                if (type == "INFO")
                {
                    string resp = LanDiscovery.Proto + "|INFO|" + LanDiscovery.B64(MachineName) + "\n";
                    byte[] rb = Encoding.UTF8.GetBytes(resp);
                    ns.WriteTimeout = 5000;
                    ns.Write(rb, 0, rb.Length);
                    ns.Flush();
                    return;
                }

                // parse per-type enc flag
                bool enc;
                string from;
                if (type == "FILE")
                {
                    if (p.Length < 7) return;
                    enc = p[5] == "1";
                    from = LanDiscovery.UnB64(p[6]);
                }
                else
                {
                    if (p.Length < 4) return;
                    enc = p[3] == "1";
                    from = LanDiscovery.UnB64(p[2]);
                }

                byte[] key = null;
                if (enc && KeyProvider != null)
                {
                    byte[] k = KeyProvider();
                    if (k != null && k.Length == 32) key = k;
                }

                if (type == "FILE")
                {
                    HandleFile(ns, p, enc && key != null, from, key);
                }
                else
                {
                    if (enc && key == null) return; // cannot decrypt — drop
                    byte[] payload = ReadPayload(ns, key);
                    if (payload == null) return;
                    string text = Encoding.UTF8.GetString(payload);

                    if (type == "CHAT")
                    {
                        if (ChatReceived != null) ChatReceived(from, text);
                    }
                    else if (type == "TASK")
                    {
                        WorkItem w = TaskStore.FromWire(text);
                        if (w != null)
                        {
                            w.AssignedBy = from;
                            w.Confirmed = false;
                            TaskStore.AddOrUpdate(Program.TasksPathOverride ?? AppSettings.TasksPath, w);
                            if (TaskReceived != null) TaskReceived(from, w);
                        }
                    }
                    else if (type == "TASKDONE")
                    {
                        string taskId = text.Trim();
                        if (TaskDoneReceived != null) TaskDoneReceived(from, taskId);
                    }
                }
            }
        }

        private void HandleFile(NetworkStream ns, string[] p, bool enc, string from, byte[] key)
        {
            string name = SafeName(LanDiscovery.UnB64(p[2]));
            long size = 0;
            long.TryParse(p[3], out size);
            string sha = p[4];

            string root = ReceiveRoot(from);
            Directory.CreateDirectory(root);
            string final = Path.Combine(root, name);
            string part = final + ".fst_part";

            // resume decision
            long offset = 0;
            if (File.Exists(part))
            {
                long partLen = new FileInfo(part).Length;
                if (!enc && partLen > 0 && partLen <= size) offset = partLen;
                else { try { File.Delete(part); } catch { } }
            }

            byte[] ackLine = Encoding.UTF8.GetBytes("ACK|" + offset + "\n");
            ns.WriteTimeout = 8000;
            ns.Write(ackLine, 0, ackLine.Length);
            ns.Flush();

            bool ok = false;
            string failReason = "";

            try
            {
                using (SHA256 hasher = SHA256.Create())
                using (var outFs = new FileStream(part, FileMode.Append, FileAccess.Write))
                {
                    // seed hash with the already-received prefix (resume case)
                    if (offset > 0)
                    {
                        using (var inFs = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            byte[] seed = new byte[ChunkSize];
                            int n;
                            while ((n = inFs.Read(seed, 0, seed.Length)) > 0) hasher.TransformBlock(seed, 0, n, null, 0);
                        }
                    }

                    if (enc)
                    {
                        byte[] iv = new byte[16];
                        if (!ReadExactly(ns, iv, 16)) throw new IOException("missing IV");
                        using (Aes aes = Aes.Create())
                        {
                            aes.Key = key;
                            aes.IV = iv;
                            using (ICryptoTransform dt = aes.CreateDecryptor())
                            using (CryptoStream cs = new CryptoStream(ns, dt, CryptoStreamMode.Read, true))
                            {
                                ok = StreamToFile(cs, outFs, hasher, size - offset);
                            }
                        }
                    }
                    else
                    {
                        ok = StreamToFile(ns, outFs, hasher, size - offset);
                    }
                }

                if (ok)
                {
                    string got = ToHex(HashFile(part));
                    if (!string.IsNullOrEmpty(sha) && got != sha)
                    {
                        try { File.Delete(part); } catch { }
                        ok = false;
                        failReason = "checksum mismatch";
                        Action<string> sink = ReceiveError;
                        if (sink != null)
                            sink("integrity: expected " + sha + " got " + got);
                    }
                }
                else failReason = "incomplete transfer";

                if (ok)
                {
                    if (File.Exists(final))
                    {
                        bool keepVersions = KeepReceivedVersions != null && KeepReceivedVersions();
                        if (keepVersions)
                        {
                            int count = VersionCount != null ? VersionCount() : 5;
                            BackupEngine.SaveVersion(final, count);
                        }
                        File.Delete(final);
                    }
                    File.Move(part, final);
                    byte[] done = Encoding.UTF8.GetBytes("DONE|1\n");
                    ns.Write(done, 0, done.Length);
                    ns.Flush();
                    if (FileReceived != null) FileReceived(from, final);
                }
                else
                {
                    byte[] fail = Encoding.UTF8.GetBytes("FAIL|" + LanDiscovery.B64(failReason) + "\n");
                    ns.Write(fail, 0, fail.Length);
                    ns.Flush();
                }
            }
            catch (Exception ex)
            {
                // interrupted — keep .fst_part for resume (plain mode)
                failReason = ex.Message;
                try
                {
                    byte[] fail = Encoding.UTF8.GetBytes("FAIL|" + LanDiscovery.B64(failReason) + "\n");
                    ns.Write(fail, 0, fail.Length);
                }
                catch { }
            }
        }

        private bool StreamToFile(Stream input, FileStream outFs, SHA256 hasher, long expected)
        {
            byte[] buf = new byte[ChunkSize];
            long received = 0;
            while (received < expected)
            {
                int want = (int)Math.Min(buf.Length, expected - received);
                int n = input.Read(buf, 0, want);
                if (n <= 0) break;
                outFs.Write(buf, 0, n);
                hasher.TransformBlock(buf, 0, n, null, 0);
                received += n;
            }
            outFs.Flush();
            return received == expected;
        }

        private static byte[] HashFile(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(fs);
            }
        }
    }
}
