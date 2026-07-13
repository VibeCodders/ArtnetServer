using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ArtnetNode.Core.Interfaces;
using ArtnetNode.Drivers;
using Microsoft.Extensions.Logging;

namespace ArtnetNode.Core
{
    public class ArtnetNodeEngine
    {
        private ArtNetServer? _artNetServer;
        private ArtnetHttpServer? _httpServer;
        private readonly IDriverFactory _driverFactory;
        private readonly ILogger _logger;
        private readonly ArtnetOptions _options;
        private readonly UniverseMergeManager _mergeManager;
        private HealthCheckService? _healthCheck;
        private bool _isRunning;

        internal IDmxInterface? _dmxInterface;
        internal List<DmxInterfaceInstance> ActiveInterfaces { get; } = new List<DmxInterfaceInstance>();

        private readonly Dictionary<int, byte[]> _lastReceivedDmx = new Dictionary<int, byte[]>();
        public bool ManualOverrideActive { get; private set; }
        public byte[] ManualOverrideValues { get; } = new byte[512];
        public bool[] ManualOverrideFlags { get; } = new bool[512];
        private bool _blackoutActive;
        private readonly object _reconnectLock = new object();

        public string BindIpAddress { get; set; } = "0.0.0.0";
        public int TargetUniverse { get; set; } = 0;
        public int Port { get; set; } = 6454;
        public string DriverType { get; set; } = "simulation";
        public string ComPort { get; set; } = "";

        public List<DmxInterfaceConfig> Interfaces { get; } = new List<DmxInterfaceConfig>();

        public bool BlackoutActive
        {
            get => _blackoutActive;
            set
            {
                _blackoutActive = value;
                if (_isRunning)
                {
                    foreach (var inst in ActiveInterfaces)
                    {
                        try
                        {
                            byte[] finalDmx = GetCurrentMergedDmx(inst.Config.Universe);
                            inst.Interface.SendDmx(finalDmx);
                            DmxReceived?.Invoke(this, new DmxEventArgs(finalDmx, inst.Config.Universe, "LocalBlackout", 0));
                        }
                        catch
                        {
                        }
                    }
                    StatusChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public bool IsRunning => _isRunning;
        public long TotalPacketsReceived => _artNetServer?.TotalPacketsReceived ?? 0;
        public string LastSenderIpAddress => _artNetServer?.LastSenderIpAddress ?? "N/A";
        public int HttpPort => _httpServer?.Port ?? 0;

        public string ConnectionStatus
        {
            get
            {
                if (ActiveInterfaces.Count == 0) return "Sconnesso";
                if (ActiveInterfaces.Count == 1)
                {
                    return ActiveInterfaces[0].ConnectionStatus;
                }
                int connectedCount = ActiveInterfaces.Count(i => i.Interface.IsConnected && !i.IsReconnecting);
                int reconnectingCount = ActiveInterfaces.Count(i => i.IsReconnecting);
                return $"{connectedCount}/{ActiveInterfaces.Count} Connessi (Riconnessione: {reconnectingCount})";
            }
        }

        public event EventHandler<DmxEventArgs>? DmxReceived;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<string>? LogMessage;
        public event EventHandler? StatusChanged;

        public ArtnetNodeEngine(
            IDriverFactory driverFactory,
            ILogger<ArtnetNodeEngine> logger,
            ArtnetOptions options)
        {
            _driverFactory = driverFactory;
            _logger = logger;
            _options = options;
            _mergeManager = new UniverseMergeManager(_options.HtpTimeoutMs, _options.DefaultMergeMode);
        }

        public void SetManualOverride(int universe, int channelIndex, byte value)
        {
            if (channelIndex < 0 || channelIndex >= 512) return;
            ManualOverrideActive = true;
            ManualOverrideFlags[channelIndex] = true;
            ManualOverrideValues[channelIndex] = value;

            var targetInterfaces = ActiveInterfaces.Where(i => i.Config.Universe == universe).ToList();
            foreach (var inst in targetInterfaces)
            {
                try
                {
                    byte[] merged = GetCurrentMergedDmx(universe);
                    inst.Interface.SendDmx(merged);
                }
                catch (Exception)
                {
                    HandleDisconnectAndScheduleReconnect(inst);
                }
            }

            byte[] finalDmx = GetCurrentMergedDmx(universe);
            DmxReceived?.Invoke(this, new DmxEventArgs(finalDmx, universe, "LocalOverride", 0));
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearManualOverrides()
        {
            ManualOverrideActive = false;
            Array.Clear(ManualOverrideFlags, 0, 512);
            Array.Clear(ManualOverrideValues, 0, 512);

            foreach (var inst in ActiveInterfaces)
            {
                try
                {
                    byte[] merged = GetCurrentMergedDmx(inst.Config.Universe);
                    inst.Interface.SendDmx(merged);
                    DmxReceived?.Invoke(this, new DmxEventArgs(merged, inst.Config.Universe, "LocalOverride", 0));
                }
                catch (Exception)
                {
                    HandleDisconnectAndScheduleReconnect(inst);
                }
            }
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearManualOverrideChannel(int universe, int channelIndex)
        {
            if (channelIndex < 0 || channelIndex >= 512) return;
            ManualOverrideFlags[channelIndex] = false;
            ManualOverrideValues[channelIndex] = 0;

            bool anyFlags = false;
            for (int i = 0; i < 512; i++)
            {
                if (ManualOverrideFlags[i])
                {
                    anyFlags = true;
                    break;
                }
            }
            if (!anyFlags)
            {
                ManualOverrideActive = false;
            }

            var targetInterfaces = ActiveInterfaces.Where(i => i.Config.Universe == universe).ToList();
            foreach (var inst in targetInterfaces)
            {
                try
                {
                    byte[] merged = GetCurrentMergedDmx(universe);
                    inst.Interface.SendDmx(merged);
                    DmxReceived?.Invoke(this, new DmxEventArgs(merged, universe, "LocalOverride", 0));
                }
                catch (Exception)
                {
                    HandleDisconnectAndScheduleReconnect(inst);
                }
            }
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        public byte[] GetCurrentMergedDmx(int universe)
        {
            byte[] dmx = new byte[512];
            lock (_lastReceivedDmx)
            {
                if (_lastReceivedDmx.TryGetValue(universe, out var cache))
                {
                    Array.Copy(cache, 0, dmx, 0, 512);
                }
            }

            if (ManualOverrideActive)
            {
                for (int i = 0; i < 512; i++)
                {
                    if (ManualOverrideFlags[i])
                    {
                        dmx[i] = ManualOverrideValues[i];
                    }
                }
            }

            if (_blackoutActive)
            {
                Array.Clear(dmx, 0, 512);
            }

            return dmx;
        }

        public void Start()
        {
            if (_isRunning) return;

            try
            {
                if (Interfaces.Count == 0)
                {
                    Interfaces.Add(new DmxInterfaceConfig
                    {
                        Universe = TargetUniverse,
                        DriverType = DriverType,
                        ComPort = ComPort
                    });
                }

                _logger.LogInformation("Inizializzazione di {Count} driver DMX...", Interfaces.Count);

                foreach (var config in Interfaces)
                {
                    IDmxInterface driverInstance = _driverFactory.CreateDriver(config.DriverType, config.Universe);

                    bool needsCom = driverInstance is EnttecProDmxInterface
                        || driverInstance is OpenDmxInterface
                        || driverInstance is EnttecProMk2DmxInterface
                        || driverInstance is FtdiGenericDmxInterface
                        || driverInstance is Dmx4AllUsbInterface
                        || driverInstance is EuroliteUsbDmxInterface;

                    if (needsCom && string.IsNullOrEmpty(config.ComPort))
                    {
                        throw new ArgumentException($"Il nome della porta COM non può essere vuoto per il driver selezionato (Universo {config.Universe}).");
                    }

                    _logger.LogInformation("Connessione driver DMX (Universo {Universe}, {DriverType}) a {ComPort}...", config.Universe, config.DriverType, config.ComPort);
                    driverInstance.Connect(config.ComPort);

                    var instance = new DmxInterfaceInstance(config, driverInstance);
                    ActiveInterfaces.Add(instance);
                    _mergeManager.RegisterUniverse(config.Universe);

                    if (_dmxInterface == null)
                    {
                        _dmxInterface = driverInstance;
                    }

                    _logger.LogInformation("Driver DMX connesso: {Status}", driverInstance.ConnectionStatus);
                }

                _artNetServer = new ArtNetServer(new LoggerAdapter<ArtNetServer>(_logger))
                {
                    BindIpAddress = BindIpAddress,
                    Port = Port
                };

                foreach (var inst in ActiveInterfaces)
                {
                    _artNetServer.TargetUniverses.Add(inst.Config.Universe);
                }

                _artNetServer.DmxReceived += ArtNetServer_DmxReceived;
                _artNetServer.PollReceived += ArtNetServer_PollReceived;
                _artNetServer.ErrorOccurred += ArtNetServer_ErrorOccurred;
                _artNetServer.LogMessage += ArtNetServer_LogMessage;

                _artNetServer.Start();

                if (_artNetServer.IsRunning)
                {
                    _isRunning = true;

                    _httpServer = new ArtnetHttpServer(this, _logger, _options);
                    _httpServer.Start(_options.HttpPort);

                    if (_options.EnableHealthChecks)
                    {
                        _healthCheck = new HealthCheckService(this, _logger, _options.HealthCheckIntervalMs);
                        _healthCheck.Start();
                    }

                    StatusChanged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    throw new Exception("Impossibile avviare il server Art-Net.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore durante l'avvio");
                _isRunning = false;
                CleanupInterfaces();
                _artNetServer = null;
                _httpServer?.Stop();
                _httpServer = null;
                _healthCheck?.Stop();
                _healthCheck?.Dispose();
                _healthCheck = null;
                ErrorOccurred?.Invoke(this, ex.Message);
                throw;
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _logger.LogInformation("Arresto del sistema Art-Net Node...");

            _healthCheck?.Stop();
            _healthCheck?.Dispose();
            _healthCheck = null;

            _httpServer?.Stop();
            _httpServer = null;

            if (_artNetServer != null)
            {
                _artNetServer.Stop();
                _artNetServer.DmxReceived -= ArtNetServer_DmxReceived;
                _artNetServer.PollReceived -= ArtNetServer_PollReceived;
                _artNetServer.ErrorOccurred -= ArtNetServer_ErrorOccurred;
                _artNetServer.LogMessage -= ArtNetServer_LogMessage;
                _artNetServer = null;
            }

            CleanupInterfaces();

            _isRunning = false;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private void CleanupInterfaces()
        {
            foreach (var inst in ActiveInterfaces)
            {
                try
                {
                    lock (_reconnectLock)
                    {
                        if (inst.ReconnectTimer != null)
                        {
                            inst.ReconnectTimer.Dispose();
                            inst.ReconnectTimer = null;
                        }
                    }
                    inst.Interface.Disconnect();
                }
                catch
                {
                }
            }
            ActiveInterfaces.Clear();
            _dmxInterface = null;
        }

        private void ArtNetServer_DmxReceived(object? sender, DmxEventArgs e)
        {
            try
            {
                lock (_lastReceivedDmx)
                {
                    if (!_lastReceivedDmx.TryGetValue(e.Universe, out var cache))
                    {
                        cache = new byte[512];
                        _lastReceivedDmx[e.Universe] = cache;
                    }
                    int copyLen = Math.Min(e.DmxData.Length, 512);
                    Array.Clear(cache, 0, 512);
                    Array.Copy(e.DmxData, 0, cache, 0, copyLen);
                }

                _mergeManager.UpdateUniverse(e.Universe, e.DmxData, e.SenderIp, e.Sequence);

                var targetInterfaces = ActiveInterfaces.Where(i => i.Config.Universe == e.Universe).ToList();
                foreach (var inst in targetInterfaces)
                {
                    try
                    {
                        byte[] finalData = GetCurrentMergedDmx(e.Universe);
                        inst.Interface.SendDmx(finalData);
                    }
                    catch (Exception)
                    {
                        HandleDisconnectAndScheduleReconnect(inst);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore durante la gestione del pacchetto DMX");
            }

            byte[] mergedDmx = GetCurrentMergedDmx(e.Universe);
            DmxReceived?.Invoke(this, new DmxEventArgs(mergedDmx, e.Universe, e.SenderIp, e.Sequence));
        }

        private void ArtNetServer_PollReceived(object? sender, ArtPollEventArgs e)
        {
            if (_artNetServer == null) return;

            try
            {
                byte[] reply = new byte[239];
                byte[] id = Encoding.ASCII.GetBytes("Art-Net\0");
                Array.Copy(id, 0, reply, 0, Math.Min(id.Length, 8));
                reply[8] = 0x00;
                reply[9] = 0x21;

                IPAddress localIp = GetActiveLocalIp(e.SenderIp);
                byte[] ipBytes = localIp.GetAddressBytes();
                Array.Copy(ipBytes, 0, reply, 10, 4);

                reply[14] = 0x3A;
                reply[15] = 0x19;
                reply[16] = 0x01;
                reply[17] = 0x00;

                int firstUniverse = ActiveInterfaces.Count > 0 ? ActiveInterfaces[0].Config.Universe : TargetUniverse;
                reply[18] = (byte)((firstUniverse >> 8) & 0x7F);
                reply[19] = (byte)((firstUniverse >> 4) & 0x0F);

                reply[20] = 0x00;
                reply[21] = 0xFF;
                reply[22] = 0x00;
                reply[23] = 0x08;

                reply[24] = 0x56;
                reply[25] = 0x43;

                string shortName = "Artnet Node";
                byte[] shortNameBytes = Encoding.ASCII.GetBytes(shortName);
                Array.Copy(shortNameBytes, 0, reply, 26, Math.Min(shortNameBytes.Length, 17));

                string longName = "Art-Net to USB DMX Gateway Server";
                byte[] longNameBytes = Encoding.ASCII.GetBytes(longName);
                Array.Copy(longNameBytes, 0, reply, 44, Math.Min(longNameBytes.Length, 63));

                string nodeReport = $"RC_OK - {ActiveInterfaces.Count} port(s) active";
                byte[] reportBytes = Encoding.ASCII.GetBytes(nodeReport);
                Array.Copy(reportBytes, 0, reply, 108, Math.Min(reportBytes.Length, 63));

                int numPorts = Math.Clamp(ActiveInterfaces.Count, 0, 4);
                reply[172] = 0x00;
                reply[173] = (byte)numPorts;

                for (int i = 0; i < 4; i++)
                {
                    reply[174 + i] = i < numPorts ? (byte)0x80 : (byte)0x00;
                }

                for (int i = 0; i < 4; i++)
                {
                    if (i < numPorts)
                    {
                        var inst = ActiveInterfaces[i];
                        reply[182 + i] = (inst.Interface.IsConnected && !inst.IsReconnecting) ? (byte)0x80 : (byte)0x00;
                    }
                    else
                    {
                        reply[182 + i] = 0x00;
                    }
                }

                for (int i = 0; i < 4; i++)
                {
                    if (i < numPorts)
                    {
                        reply[190 + i] = (byte)(ActiveInterfaces[i].Config.Universe & 0x0F);
                    }
                    else
                    {
                        reply[190 + i] = 0x00;
                    }
                }

                reply[197] = 0x01;
                byte[] macBytes = GetMacAddress();
                Array.Copy(macBytes, 0, reply, 198, 6);
                Array.Copy(ipBytes, 0, reply, 204, 4);
                reply[208] = 0x01;
                reply[209] = 0x08;

                _artNetServer.SendPacket(reply, e.SenderIp, e.SenderPort);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERRORE POLL] Errore nell'invio del pacchetto ArtPollReply");
            }
        }

        private void ArtNetServer_ErrorOccurred(object? sender, string errorMessage)
        {
            ErrorOccurred?.Invoke(this, errorMessage);
        }

        private void ArtNetServer_LogMessage(object? sender, string message)
        {
            LogMessage?.Invoke(this, message);
        }

        internal void Log(string message)
        {
            LogMessage?.Invoke(this, message);
        }

        internal void RaiseError(string errorMessage)
        {
            ErrorOccurred?.Invoke(this, errorMessage);
        }

        private IPAddress GetActiveLocalIp(string targetIp)
        {
            if (IPAddress.TryParse(BindIpAddress, out var ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any))
            {
                return ip;
            }

            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect(targetIp, 6454);
                    if (socket.LocalEndPoint is IPEndPoint endPoint)
                    {
                        return endPoint.Address;
                    }
                }
            }
            catch
            {
            }

            try
            {
                string hostName = Dns.GetHostName();
                IPHostEntry host = Dns.GetHostEntry(hostName);
                foreach (IPAddress address in host.AddressList)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                    {
                        return address;
                    }
                }
            }
            catch
            {
            }

            return IPAddress.Loopback;
        }

        private byte[] GetMacAddress()
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                         ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                    {
                        byte[] address = ni.GetPhysicalAddress().GetAddressBytes();
                        if (address.Length == 6)
                        {
                            return address;
                        }
                    }
                }
            }
            catch
            {
            }
            return new byte[] { 0x00, 0x0B, 0xAD, 0xC0, 0xFF, 0xEE };
        }

        public void HandleDisconnectAndScheduleReconnect(DmxInterfaceInstance inst)
        {
            lock (_reconnectLock)
            {
                if (!_isRunning || inst.IsReconnecting) return;

                inst.IsReconnecting = true;
                _logger.LogWarning("Connessione DMX persa per Universo {Universe} ({DriverType}). Avvio riconnessione...", inst.Config.Universe, inst.Config.DriverType);

                int delay = _options.ReconnectBaseDelayMs;
                inst.ReconnectTimer = new Timer(state => ReconnectCallback(inst), null, delay, Timeout.Infinite);
                inst.ReconnectAttempt = 1;
                inst.ReconnectDelay = delay;
            }
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void ReconnectCallback(DmxInterfaceInstance inst)
        {
            lock (_reconnectLock)
            {
                if (!_isRunning)
                {
                    inst.ReconnectTimer?.Dispose();
                    inst.ReconnectTimer = null;
                    inst.IsReconnecting = false;
                    return;
                }
            }

            try
            {
                _logger.LogInformation("Tentativo di riconnessione per Universo {Universe} a {ComPort}...", inst.Config.Universe, inst.Config.ComPort);
                inst.Interface.Connect(inst.Config.ComPort);

                if (inst.Interface.IsConnected)
                {
                    _logger.LogInformation("Riconnessione completata per Universo {Universe}! Stato: {Status}", inst.Config.Universe, inst.Interface.ConnectionStatus);
                    lock (_reconnectLock)
                    {
                        inst.ReconnectTimer?.Dispose();
                        inst.ReconnectTimer = null;
                        inst.IsReconnecting = false;
                    }
                    StatusChanged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    ScheduleNextReconnect(inst);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tentativo di riconnessione fallito per Universo {Universe}", inst.Config.Universe);
                ScheduleNextReconnect(inst);
            }
        }

        private void ScheduleNextReconnect(DmxInterfaceInstance inst)
        {
            lock (_reconnectLock)
            {
                if (!_isRunning) return;

                int nextDelay = Math.Min(
                    (int)(inst.ReconnectDelay * _options.ReconnectBackoffMultiplier),
                    _options.ReconnectMaxDelayMs);

                int jitter = new Random().Next(-nextDelay / 5, nextDelay / 5);
                nextDelay = Math.Max(nextDelay + jitter, _options.ReconnectBaseDelayMs);

                inst.ReconnectAttempt++;
                inst.ReconnectDelay = nextDelay;

                _logger.LogInformation("Prossimo tentativo riconnessione Universo {Universe} tra {Delay}ms (tentativo {Attempt})", inst.Config.Universe, nextDelay, inst.ReconnectAttempt);

                inst.ReconnectTimer?.Dispose();
                inst.ReconnectTimer = new Timer(state => ReconnectCallback(inst), null, nextDelay, Timeout.Infinite);
            }
        }
    }

    public class DmxInterfaceInstance
    {
        public DmxInterfaceConfig Config { get; }
        public IDmxInterface Interface { get; }
        public bool IsReconnecting { get; set; }
        public Timer? ReconnectTimer { get; set; }
        public int ReconnectAttempt { get; set; }
        public int ReconnectDelay { get; set; }
        public string ConnectionStatus => IsReconnecting ? $"Riconnessione in corso... (tentativo {ReconnectAttempt})" : Interface.ConnectionStatus;

        public DmxInterfaceInstance(DmxInterfaceConfig config, IDmxInterface dmxInterface)
        {
            Config = config;
            Interface = dmxInterface;
        }
    }
}
