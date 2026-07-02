using System;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;

namespace ArtnetNode
{
    public class ArtnetNodeEngine
    {
        private ArtNetServer? _artNetServer;
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
                if (_blackoutActive && _isRunning)
                {
                    foreach (var inst in ActiveInterfaces)
                    {
                        try
                        {
                            inst.Interface.SendDmx(new byte[512]);
                        }
                        catch
                        {
                            // Send failures are handled in the packet reception/reconnection flow
                        }
                    }
                }
            }
        }

        public bool IsRunning => _isRunning;
        public long TotalPacketsReceived => _artNetServer?.TotalPacketsReceived ?? 0;
        public string LastSenderIpAddress => _artNetServer?.LastSenderIpAddress ?? "N/A";

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
                _artNetServer.ErrorOccurred += ArtNetServer_ErrorOccurred;
                _artNetServer.LogMessage += ArtNetServer_LogMessage;

                _artNetServer.Start();

                if (_artNetServer.IsRunning)
                {
                    _isRunning = true;
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

            if (_artNetServer != null)
            {
                _artNetServer.Stop();
                _artNetServer.DmxReceived -= ArtNetServer_DmxReceived;
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
                var targetInterfaces = ActiveInterfaces.Where(i => i.Config.Universe == e.Universe).ToList();
                foreach (var inst in targetInterfaces)
                {
                    try
                    {
                        if (_blackoutActive)
                        {
                            inst.Interface.SendDmx(new byte[512]);
                        }
                        else
                        {
                            inst.Interface.SendDmx(e.DmxData);
                        }
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

            // Forward event upwards
            DmxReceived?.Invoke(this, e);
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

        private void Log(string message)
        {
            LogMessage?.Invoke(this, message);
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
