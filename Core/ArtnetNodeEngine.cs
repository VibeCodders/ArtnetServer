using System;
using System.IO.Ports;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using ArtnetNode.Drivers;

namespace ArtnetNode.Core
{
    public class ArtnetNodeEngine
    {
        private ArtNetServer? _artNetServer;
        private ArtnetHttpServer? _httpServer;
        internal IDmxInterface? _dmxInterface; // Primary/first interface for backward compatibility
        private bool _isRunning;

        // Configuration
        public string BindIpAddress { get; set; } = "0.0.0.0";
        public int TargetUniverse { get; set; } = 0;
        public int Port { get; set; } = 6454;
        public string DriverType { get; set; } = "simulation"; // "simulation", "enttec", "open"
        public string ComPort { get; set; } = "";

        public List<DmxInterfaceConfig> Interfaces { get; } = new List<DmxInterfaceConfig>();
        internal List<DmxInterfaceInstance> ActiveInterfaces { get; } = new List<DmxInterfaceInstance>();

        // Cached DMX status for overrides & dashboard
        private readonly Dictionary<int, byte[]> _lastReceivedDmx = new Dictionary<int, byte[]>();

        // Manual Override properties
        public bool ManualOverrideActive { get; private set; } = false;
        public byte[] ManualOverrideValues { get; } = new byte[512];
        public bool[] ManualOverrideFlags { get; } = new bool[512];

        // Events
        public event EventHandler<DmxEventArgs>? DmxReceived;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<string>? LogMessage;
        public event EventHandler? StatusChanged;

        private bool _blackoutActive = false;
        private readonly object _reconnectLock = new object();

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
                            // Send failures are handled in the packet reception/reconnection flow
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

        public void SetManualOverride(int universe, int channelIndex, byte value)
        {
            if (channelIndex < 0 || channelIndex >= 512) return;
            ManualOverrideActive = true;
            ManualOverrideFlags[channelIndex] = true;
            ManualOverrideValues[channelIndex] = value;
            
            // Update DMX interface immediately
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
            
            // Trigger DmxReceived to keep WPF and Web client grids in sync
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
                // Fallback for single interface backward compatibility
                if (Interfaces.Count == 0)
                {
                    Interfaces.Add(new DmxInterfaceConfig
                    {
                        Universe = TargetUniverse,
                        DriverType = DriverType,
                        ComPort = ComPort
                    });
                }

                Log($"Inizializzazione di {Interfaces.Count} driver DMX...");

                foreach (var config in Interfaces)
                {
                    IDmxInterface driverInstance;
                    switch (config.DriverType.ToLowerInvariant())
                    {
                        case "simulation":
                        case "sim":
                            driverInstance = new SimulationDmxInterface();
                            break;
                        case "enttec":
                        case "pro":
                        case "enttecpro":
                            driverInstance = new EnttecProDmxInterface();
                            break;
                        case "open":
                        case "opendmx":
                            driverInstance = new OpenDmxInterface();
                            break;
                        case "enttec_mk2":
                        case "enttecmk2":
                            driverInstance = new EnttecProMk2DmxInterface
                            {
                                UniversePort = (config.Universe % 2) == 0 ? 1 : 2
                            };
                            break;
                        case "ftdi_generic":
                        case "ftdigeneric":
                            driverInstance = new FtdiGenericDmxInterface();
                            break;
                        case "udmx":
                            driverInstance = new UDmxInterface();
                            break;
                        case "dmx4all":
                            driverInstance = new Dmx4AllUsbInterface();
                            break;
                        case "chauvet":
                            driverInstance = new ChauvetUsbDmxInterface();
                            break;
                        case "eurolite_pro":
                        case "eurolitepro":
                            driverInstance = new EuroliteUsbDmxInterface();
                            break;
                        case "hid_dmx":
                        case "hiddmx":
                            driverInstance = new HidDmxInterface();
                            break;
                        default:
                            throw new ArgumentException($"Driver DMX non riconosciuto: '{config.DriverType}'");
                    }

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

                    Log($"Connessione driver DMX (Universo {config.Universe}, {config.DriverType}) a {config.ComPort}...");
                    driverInstance.Connect(config.ComPort);
                    
                    var instance = new DmxInterfaceInstance(config, driverInstance);
                    ActiveInterfaces.Add(instance);

                    // Set primary interface reference for backwards compatibility tests
                    if (_dmxInterface == null)
                    {
                        _dmxInterface = driverInstance;
                    }

                    Log($"Driver DMX connesso: {driverInstance.ConnectionStatus}");
                }

                _artNetServer = new ArtNetServer
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
                    
                    // Start embedded HTTP Web Dashboard Server
                    _httpServer = new ArtnetHttpServer(this);
                    _httpServer.Start(8080);

                    StatusChanged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    throw new Exception("Impossibile avviare il server Art-Net.");
                }
            }
            catch (Exception ex)
            {
                _isRunning = false;
                CleanupInterfaces();
                _artNetServer = null;
                if (_httpServer != null)
                {
                    _httpServer.Stop();
                    _httpServer = null;
                }
                ErrorOccurred?.Invoke(this, ex.Message);
                throw;
            }
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
                    // Ignore exceptions during cleanup
                }
            }
            ActiveInterfaces.Clear();
            _dmxInterface = null;
        }

        public void Stop()
        {
            if (!_isRunning) return;

            Log("Arresto del sistema Art-Net Node...");

            if (_httpServer != null)
            {
                _httpServer.Stop();
                _httpServer = null;
            }

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

        private void ArtNetServer_DmxReceived(object? sender, DmxEventArgs e)
        {
            try
            {
                // Update cache
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
            catch (Exception)
            {
                // General error
            }

            // Forward event upwards with merged data
            byte[] mergedDmx = GetCurrentMergedDmx(e.Universe);
            DmxReceived?.Invoke(this, new DmxEventArgs(mergedDmx, e.Universe, e.SenderIp, e.Sequence));
        }

        private void ArtNetServer_PollReceived(object? sender, ArtPollEventArgs e)
        {
            if (_artNetServer == null) return;
            
            try
            {
                // Construct ArtPollReply packet (239 bytes)
                byte[] reply = new byte[239];
                
                // 1. ID: "Art-Net\0"
                byte[] id = Encoding.ASCII.GetBytes("Art-Net\0");
                Array.Copy(id, 0, reply, 0, Math.Min(id.Length, 8));
                
                // 2. OpCode: OpPollReply (0x2100 -> little endian: 0x00, 0x21)
                reply[8] = 0x00;
                reply[9] = 0x21;
                
                // 3. IpAddress: 4 bytes
                IPAddress localIp = GetActiveLocalIp(e.SenderIp);
                byte[] ipBytes = localIp.GetAddressBytes();
                Array.Copy(ipBytes, 0, reply, 10, 4);
                
                // 4. Port: 2 bytes (little endian: 6454 -> 0x3A, 0x19)
                reply[14] = 0x3A;
                reply[15] = 0x19;
                
                // 5. VersInfo: 2 bytes (Firmware version, big endian: 1.0 -> 0x01, 0x00)
                reply[16] = 0x01;
                reply[17] = 0x00;
                
                // 6. NetSwitch: 1 byte (Net universe, bits 8-14 of first active universe or 0)
                int firstUniverse = ActiveInterfaces.Count > 0 ? ActiveInterfaces[0].Config.Universe : TargetUniverse;
                reply[18] = (byte)((firstUniverse >> 8) & 0x7F);
                
                // 7. SubSwitch: 1 byte (Subnet, bits 4-7. Usually 0)
                reply[19] = (byte)((firstUniverse >> 4) & 0x0F);
                
                // 8. Oem: 2 bytes (OEM code, e.g. 0x00FF -> big endian: 0x00, 0xFF)
                reply[20] = 0x00;
                reply[21] = 0xFF;
                
                // 9. UbeaVersion: 1 byte (0)
                reply[22] = 0x00;
                
                // 10. Status1: 1 byte (Indicator normal state -> 0x08)
                reply[23] = 0x08;
                
                // 11. EstaMan: 2 bytes (ESTA code, little-endian: VibeCodders -> 0x56, 0x43)
                reply[24] = 0x56;
                reply[25] = 0x43;
                
                // 12. ShortName: 18 bytes (Null-terminated ASCII)
                string shortName = "Artnet Node";
                byte[] shortNameBytes = Encoding.ASCII.GetBytes(shortName);
                Array.Copy(shortNameBytes, 0, reply, 26, Math.Min(shortNameBytes.Length, 17));
                
                // 13. LongName: 64 bytes (Null-terminated ASCII)
                string longName = "Art-Net to USB DMX Gateway Server";
                byte[] longNameBytes = Encoding.ASCII.GetBytes(longName);
                Array.Copy(longNameBytes, 0, reply, 44, Math.Min(longNameBytes.Length, 63));
                
                // 14. NodeReport: 64 bytes
                string nodeReport = $"RC_OK - {ActiveInterfaces.Count} port(s) active";
                byte[] reportBytes = Encoding.ASCII.GetBytes(nodeReport);
                Array.Copy(reportBytes, 0, reply, 108, Math.Min(reportBytes.Length, 63));
                
                // 15. NumPorts: 2 bytes (Big endian. Max 4)
                int numPorts = Math.Clamp(ActiveInterfaces.Count, 0, 4);
                reply[172] = 0x00;
                reply[173] = (byte)numPorts;
                
                // 16. PortTypes: 4 bytes (For each of the 4 ports. DMX output: 0x80)
                for (int i = 0; i < 4; i++)
                {
                    reply[174 + i] = i < numPorts ? (byte)0x80 : (byte)0x00;
                }
                
                // 17. GoodInput: 4 bytes (all 0)
                // 18. GoodOutput: 4 bytes (If active: 0x80)
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
                
                // 19. PortAddressIn: 4 bytes (all 0)
                // 20. PortAddressOut: 4 bytes (Lower 4 bits of universe: universe & 0x0F)
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
                
                // 21. Video: 1 byte (0)
                // 22. Macro: 1 byte (0)
                // 23. Bind: 1 byte (0)
                // 24. Style: 1 byte (Style of node: 0x01 = StNode)
                reply[197] = 0x01;
                
                // 25. Mac: 6 bytes
                byte[] macBytes = GetMacAddress();
                Array.Copy(macBytes, 0, reply, 198, 6);
                
                // 26. BindIp: 4 bytes
                Array.Copy(ipBytes, 0, reply, 204, 4);
                
                // 27. BindIndex: 1 byte (1)
                reply[208] = 0x01;
                
                // 28. Status2: 1 byte (0x08)
                reply[209] = 0x08;
                
                // Send unicast reply back to sender
                _artNetServer.SendPacket(reply, e.SenderIp, e.SenderPort);
            }
            catch (Exception ex)
            {
                Log($"[ERRORE POLL] Errore nell'invio del pacchetto ArtPollReply: {ex.Message}");
            }
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
                // Ignore socket connect
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
                // Ignore DNS
            }

            return IPAddress.Loopback;
        }

        private byte[] GetMacAddress()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
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
                // Ignore
            }
            return new byte[] { 0x00, 0x0B, 0xAD, 0xC0, 0xFF, 0xEE };
        }

        private void HandleDisconnectAndScheduleReconnect(DmxInterfaceInstance inst)
        {
            lock (_reconnectLock)
            {
                if (!_isRunning || inst.IsReconnecting) return;
                
                inst.IsReconnecting = true;
                Log($"[WARNING] Connessione DMX persa per Universo {inst.Config.Universe} ({inst.Config.DriverType}). Stato driver: {inst.Interface.ConnectionStatus}. Avvio del loop di riconnessione automatica...");
                
                inst.ReconnectTimer = new Timer(state => ReconnectCallback(inst), null, 1000, 3000);
            }
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ReconnectCallback(DmxInterfaceInstance inst)
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
                Log($"Tentativo di riconnessione per Universo {inst.Config.Universe} a {inst.Config.ComPort}...");
                inst.Interface.Connect(inst.Config.ComPort);

                if (inst.Interface.IsConnected)
                {
                    Log($"[SUCCESSO] Riconnessione completata con successo per Universo {inst.Config.Universe}! Stato: {inst.Interface.ConnectionStatus}");
                    lock (_reconnectLock)
                    {
                        inst.ReconnectTimer?.Dispose();
                        inst.ReconnectTimer = null;
                        inst.IsReconnecting = false;
                    }
                    StatusChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Log($"Tentativo di riconnessione fallito per Universo {inst.Config.Universe}: {ex.Message}");
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
    }

    public class DmxInterfaceInstance
    {
        public DmxInterfaceConfig Config { get; }
        public IDmxInterface Interface { get; }
        public bool IsReconnecting { get; set; }
        public Timer? ReconnectTimer { get; set; }
        public string ConnectionStatus => IsReconnecting ? "Riconnessione in corso..." : Interface.ConnectionStatus;

        public DmxInterfaceInstance(DmxInterfaceConfig config, IDmxInterface dmxInterface)
        {
            Config = config;
            Interface = dmxInterface;
        }
    }
}
