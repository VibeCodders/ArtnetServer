using System;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;

namespace ArtnetNode
{
    public class ArtnetNodeEngine
    {
        private ArtNetServer? _artNetServer;
        private IDmxInterface? _dmxInterface;
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

        public bool IsRunning => _isRunning;
        public long TotalPacketsReceived => _artNetServer?.TotalPacketsReceived ?? 0;
        public string LastSenderIpAddress => _artNetServer?.LastSenderIpAddress ?? "N/A";
        public string ConnectionStatus => _dmxInterface?.ConnectionStatus ?? "Sconnesso";

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
                    default:
                        throw new ArgumentException($"Driver DMX non riconosciuto: '{DriverType}'");
                }

                if ((_dmxInterface is EnttecProDmxInterface || _dmxInterface is OpenDmxInterface) && string.IsNullOrEmpty(ComPort))
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
            // Forward DMX to physical DMX interface immediately
            _dmxInterface?.SendDmx(e.DmxData);

            // Forward event upwards
            DmxReceived?.Invoke(this, e);
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
