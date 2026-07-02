using System;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;

namespace ArtnetNode
{
    public class ArtnetNodeEngine
    {
        private ArtNetServer? _artNetServer;
        internal IDmxInterface? _dmxInterface;
        private bool _isRunning;

        // Configuration
        public string BindIpAddress { get; set; } = "0.0.0.0";
        public int TargetUniverse { get; set; } = 0;
        public int Port { get; set; } = 6454;
        public string DriverType { get; set; } = "simulation"; // "simulation", "enttec", "open"
        public string ComPort { get; set; } = "";

        // Events
        public event EventHandler<DmxEventArgs>? DmxReceived;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<string>? LogMessage;
        public event EventHandler? StatusChanged;

        private bool _blackoutActive = false;
        private Timer? _reconnectTimer;
        private readonly object _reconnectLock = new object();
        private bool _isReconnecting = false;

        public bool BlackoutActive
        {
            get => _blackoutActive;
            set
            {
                _blackoutActive = value;
                if (_blackoutActive && _isRunning)
                {
                    try
                    {
                        _dmxInterface?.SendDmx(new byte[512]);
                    }
                    catch
                    {
                        // Send failures are handled in the packet reception/reconnection flow
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
                if (_isReconnecting)
                    return "Riconnessione in corso...";
                return _dmxInterface?.ConnectionStatus ?? "Sconnesso";
            }
        }

        public void Start()
        {
            if (_isRunning) return;

            try
            {
                Log($"Inizializzazione driver DMX '{DriverType}'...");
                
                switch (DriverType.ToLowerInvariant())
                {
                    case "simulation":
                    case "sim":
                        _dmxInterface = new SimulationDmxInterface();
                        break;
                    case "enttec":
                    case "pro":
                    case "enttecpro":
                        _dmxInterface = new EnttecProDmxInterface();
                        break;
                    case "open":
                    case "opendmx":
                        _dmxInterface = new OpenDmxInterface();
                        break;
                    case "enttec_mk2":
                    case "enttecmk2":
                        _dmxInterface = new EnttecProMk2DmxInterface
                        {
                            UniversePort = (TargetUniverse % 2) == 0 ? 1 : 2
                        };
                        break;
                    case "ftdi_generic":
                    case "ftdigeneric":
                        _dmxInterface = new FtdiGenericDmxInterface();
                        break;
                    case "udmx":
                        _dmxInterface = new UDmxInterface();
                        break;
                    case "dmx4all":
                        _dmxInterface = new Dmx4AllUsbInterface();
                        break;
                    case "chauvet":
                        _dmxInterface = new ChauvetUsbDmxInterface();
                        break;
                    case "eurolite_pro":
                    case "eurolitepro":
                        _dmxInterface = new EuroliteUsbDmxInterface();
                        break;
                    case "hid_dmx":
                    case "hiddmx":
                        _dmxInterface = new HidDmxInterface();
                        break;
                    default:
                        throw new ArgumentException($"Driver DMX non riconosciuto: '{DriverType}'");
                }

                bool needsCom = _dmxInterface is EnttecProDmxInterface 
                    || _dmxInterface is OpenDmxInterface
                    || _dmxInterface is EnttecProMk2DmxInterface
                    || _dmxInterface is FtdiGenericDmxInterface
                    || _dmxInterface is Dmx4AllUsbInterface
                    || _dmxInterface is EuroliteUsbDmxInterface;

                if (needsCom && string.IsNullOrEmpty(ComPort))
                {
                    throw new ArgumentException("Il nome della porta COM non può essere vuoto per il driver selezionato.");
                }

                _dmxInterface.Connect(ComPort);
                Log($"Driver DMX connesso: {_dmxInterface.ConnectionStatus}");

                _artNetServer = new ArtNetServer
                {
                    BindIpAddress = BindIpAddress,
                    TargetUniverse = TargetUniverse,
                    Port = Port
                };

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
                    _dmxInterface.Disconnect();
                    _dmxInterface = null;
                    _artNetServer = null;
                    throw new Exception("Impossibile avviare il server Art-Net.");
                }
            }
            catch (Exception ex)
            {
                _isRunning = false;
                if (_dmxInterface != null)
                {
                    _dmxInterface.Disconnect();
                    _dmxInterface = null;
                }
                _artNetServer = null;
                ErrorOccurred?.Invoke(this, ex.Message);
                throw;
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            Log("Arresto del sistema Art-Net Node...");

            lock (_reconnectLock)
            {
                if (_reconnectTimer != null)
                {
                    _reconnectTimer.Dispose();
                    _reconnectTimer = null;
                }
                _isReconnecting = false;
            }

            if (_artNetServer != null)
            {
                _artNetServer.Stop();
                _artNetServer.DmxReceived -= ArtNetServer_DmxReceived;
                _artNetServer.ErrorOccurred -= ArtNetServer_ErrorOccurred;
                _artNetServer.LogMessage -= ArtNetServer_LogMessage;
                _artNetServer = null;
            }

            if (_dmxInterface != null)
            {
                _dmxInterface.Disconnect();
                Log($"Driver DMX disconnesso. Stato: {_dmxInterface.ConnectionStatus}");
                _dmxInterface = null;
            }

            _isRunning = false;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ArtNetServer_DmxReceived(object? sender, DmxEventArgs e)
        {
            try
            {
                if (_dmxInterface != null)
                {
                    if (_blackoutActive)
                    {
                        _dmxInterface.SendDmx(new byte[512]);
                    }
                    else
                    {
                        _dmxInterface.SendDmx(e.DmxData);
                    }
                }
            }
            catch (Exception)
            {
                HandleDisconnectAndScheduleReconnect();
            }

            // Forward event upwards
            DmxReceived?.Invoke(this, e);
        }

        private void HandleDisconnectAndScheduleReconnect()
        {
            lock (_reconnectLock)
            {
                if (!_isRunning || _isReconnecting) return;
                
                _isReconnecting = true;
                Log($"[WARNING] Connessione DMX persa. Stato driver: {_dmxInterface?.ConnectionStatus}. Avvio del loop di riconnessione automatica...");
                
                _reconnectTimer = new Timer(ReconnectCallback, null, 1000, 3000);
            }
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ReconnectCallback(object? state)
        {
            lock (_reconnectLock)
            {
                if (!_isRunning)
                {
                    _reconnectTimer?.Dispose();
                    _reconnectTimer = null;
                    _isReconnecting = false;
                    return;
                }
            }

            try
            {
                Log($"Tentativo di riconnessione a {ComPort}...");
                _dmxInterface?.Connect(ComPort);

                if (_dmxInterface != null && _dmxInterface.IsConnected)
                {
                    Log($"[SUCCESSO] Riconnessione completata con successo! Stato: {_dmxInterface.ConnectionStatus}");
                    lock (_reconnectLock)
                    {
                        _reconnectTimer?.Dispose();
                        _reconnectTimer = null;
                        _isReconnecting = false;
                    }
                    StatusChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Log($"Tentativo di riconnessione fallito: {ex.Message}");
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
}
