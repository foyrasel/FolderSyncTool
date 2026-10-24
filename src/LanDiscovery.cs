using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace FolderSyncTool
{
    public class PeerInfo
    {
        public string Ip;
        public string Name;
        public DateTime LastSeenUtc;
        public bool Manual;          // v5: added by IP, never expires

        public override string ToString()
        {
            return Name + " (" + Ip + ")" + (Manual ? " [manual]" : "");
        }
    }

    /// <summary>
    /// LAN peer discovery over UDP broadcast + v5 manual peers added by IP.
    /// Manual peers are merged into the list and validated via TCP handshake.
    /// </summary>
    public class LanDiscovery : IDisposable
    {
        public const int UdpPort = 50110;
        public const int TcpPort = 50111;
        public const string Proto = "FSTPROTO1";
        private const string Hello = "FSTDISC1";
        private const string Reply = "FSTHERE1";
        private static readonly TimeSpan TTL = TimeSpan.FromSeconds(75);

        public string MachineName = Environment.MachineName;
        public List<PeerInfo> Peers = new List<PeerInfo>();

        private UdpClient _udp;
        private Thread _listenThread;
        private volatile bool _running;

        public event Action<PeerInfo> PeerAdded;
        public event Action<PeerInfo> PeerRemoved;

        public LanDiscovery()
        {
            string env = Environment.GetEnvironmentVariable("FST_MACHINE_NAME");
            if (!string.IsNullOrEmpty(env)) MachineName = env; // test hook
        }

        // ---- manual peers (v5) -------------------------------------------

        /// <summary>Add a manual peer by IP; returns the resolved name via TCP handshake.</summary>
        public static PeerInfo ProbeManualPeer(string ip, int timeoutMs)
        {
            string name;
            if (!HandshakeInfo(ip, TcpPort, timeoutMs, out name))
                return null;
            return new PeerInfo { Ip = ip, Name = name, LastSeenUtc = DateTime.UtcNow, Manual = true };
        }

        public void AddManualPeer(PeerInfo p)
        {
            if (p == null) return;
            p.Manual = true;
            lock (Peers)
            {
                PeerInfo old = Peers.Find(x => x.Ip == p.Ip);
                if (old != null) { old.Name = p.Name; old.LastSeenUtc = DateTime.UtcNow; old.Manual = true; }
                else { Peers.Add(p); }
            }
            SaveManualPeers();
        }

        public void RemovePeer(string ip)
        {
            lock (Peers)
            {
                PeerInfo old = Peers.Find(x => x.Ip == ip);
                if (old == null) return;
                Peers.Remove(old);
            }
            SaveManualPeers();
        }

        /// <summary>Persist manual peers into settings ("ip;ip;ip").</summary>
        public void SaveManualPeers()
        {
            StringBuilder sb = new StringBuilder();
            lock (Peers)
            {
                foreach (PeerInfo p in Peers)
                    if (p.Manual) sb.Append(p.Ip).Append(';');
            }
            Program.Settings.ManualPeers = sb.ToString().TrimEnd(';');
        }

        /// <summary>Restore manual peers from settings (pure parse, testable).</summary>
        public static List<string> ParseManualPeers(string joined)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(joined)) return list;
            foreach (string part in joined.Split(';'))
            {
                string ip = part.Trim();
                if (ip.Length == 0) continue;
                if (!IsPlausibleIp(ip)) continue;
                if (!list.Contains(ip)) list.Add(ip);
            }
            return list;
        }

        public static bool IsPlausibleIp(string s)
        {
            IPAddress a;
            return IPAddress.TryParse(s, out a) && a.AddressFamily == AddressFamily.InterNetwork;
        }

        // ---- discovery -----------------------------------------------------

        public void Start()
        {
            if (_running) return;
            _running = true;
            _udp = new UdpClient();
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, UdpPort));
            _udp.EnableBroadcast = true;
            _listenThread = new Thread(ListenLoop);
            _listenThread.IsBackground = true;
            _listenThread.Start();
        }

        public void Announce()
        {
            try
            {
                using (UdpClient c = new UdpClient())
                {
                    c.EnableBroadcast = true;
                    byte[] data = Encoding.UTF8.GetBytes(Hello + "|" + MachineName);
                    IPEndPoint ep = new IPEndPoint(IPAddress.Broadcast, UdpPort);
                    for (int i = 0; i < 3; i++) { c.Send(data, data.Length, ep); }
                }
            }
            catch { }
        }

        /// <summary>Drop non-manual peers not seen recently (call from a UI timer).</summary>
        public void ExpireStale()
        {
            List<PeerInfo> stale = new List<PeerInfo>();
            lock (Peers)
            {
                foreach (PeerInfo p in Peers)
                    if (!p.Manual && DateTime.UtcNow - p.LastSeenUtc > TTL) stale.Add(p);
                foreach (PeerInfo p in stale) Peers.Remove(p);
            }
            foreach (PeerInfo p in stale)
                if (PeerRemoved != null) PeerRemoved(p);
        }

        public PeerInfo[] Snapshot()
        {
            lock (Peers) return Peers.ToArray();
        }

        private void ListenLoop()
        {
            IPEndPoint any = new IPEndPoint(IPAddress.Any, UdpPort);
            while (_running)
            {
                try
                {
                    byte[] data = _udp.Receive(ref any);
                    string msg = Encoding.UTF8.GetString(data);
                    HandleDatagram(msg, any.Address);
                }
                catch (SocketException)
                {
                    // timed out / interrupted — loop continues
                }
                catch (ObjectDisposedException) { break; }
                catch { }
            }
        }

        private void HandleDatagram(string msg, IPAddress from)
        {
            string ip = from.ToString();
            if (ip.StartsWith("127.") || ip == "::1") return; // self

            if (msg.StartsWith(Hello + "|"))
            {
                string theirName = msg.Substring(Hello.Length + 1);
                UpsertPeer(ip, theirName);
                // answer directly so the asker learns us even across ARP quirks
                try
                {
                    byte[] reply = Encoding.UTF8.GetBytes(Reply + "|" + MachineName);
                    _udp.Send(reply, reply.Length, new IPEndPoint(from, UdpPort));
                }
                catch { }
            }
            else if (msg.StartsWith(Reply + "|"))
            {
                string theirName = msg.Substring(Reply.Length + 1);
                UpsertPeer(ip, theirName);
            }
        }

        private void UpsertPeer(string ip, string name)
        {
            PeerInfo added = null;
            lock (Peers)
            {
                PeerInfo p = Peers.Find(x => x.Ip == ip);
                if (p == null)
                {
                    p = new PeerInfo { Ip = ip, Name = name, LastSeenUtc = DateTime.UtcNow, Manual = false };
                    Peers.Add(p);
                    added = p;
                }
                else
                {
                    p.Name = string.IsNullOrEmpty(p.Name) || p.Manual ? p.Name : name;
                    p.LastSeenUtc = DateTime.UtcNow;
                }
            }
            if (added != null && PeerAdded != null) PeerAdded(added);
        }

        // ---- TCP info handshake (used by manual add + group chat) ---------

        public static bool HandshakeInfo(string ip, int port, int timeoutMs, out string theirName)
        {
            theirName = null;
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    IAsyncResult ar = c.BeginConnect(ip, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                    c.EndConnect(ar);
                    using (NetworkStream s = c.GetStream())
                    {
                        s.ReadTimeout = Math.Max(1000, timeoutMs);   // never hang on silent peer
                        string line = Proto + "|INFO|" + B64(Environment.MachineName);
                        byte[] outBuf = Encoding.UTF8.GetBytes(line + "\n");
                        s.Write(outBuf, 0, outBuf.Length);
                        s.Flush();
                        string resp = ReadLine(s, timeoutMs);
                        if (resp == null) return false;
                        string[] p = resp.Split('|');
                        if (p.Length >= 3 && p[0] == Proto && p[1] == "INFO")
                        {
                            theirName = UnB64(p[2]);
                            return true;
                        }
                        return false;
                    }
                }
            }
            catch { return false; }
        }

        // ---- shared helpers ------------------------------------------------

        public static string B64(string s)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? ""));
        }

        public static string UnB64(string s)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(s ?? "")); }
            catch { return ""; }
        }

        /// <summary>Read one \n-terminated ASCII/UTF-8 line with timeout.</summary>
        public static string ReadLine(NetworkStream s, int timeoutMs)
        {
            var sb = new StringBuilder();
            byte[] buf = new byte[1];
            DateTime start = DateTime.UtcNow;
            while (true)
            {
                if (timeoutMs > 0 && (DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                    return sb.Length > 0 ? sb.ToString() : null;
                int n = s.Read(buf, 0, 1);
                if (n <= 0) return sb.Length > 0 ? sb.ToString() : null;
                if (buf[0] == (byte)'\n') break;
                sb.Append((char)buf[0]);
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            _running = false;
            try { if (_udp != null) _udp.Close(); } catch { }
        }
    }
}
