using System;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ArtnetNode;

namespace Artnet
{
    public partial class MainWindow : Window
    {
        // Core Components
        private ArtNetServer? _artNetServer;
        private IDmxInterface? _dmxInterface;

        // UI Performance & State
        private Border[] _channelBorders = new Border[512];
        private TextBlock[] _channelValueTexts = new TextBlock[512];
        private byte[] _dmxDataBuffer = new byte[512];
        private readonly object _dmxLock = new object();
        private bool _hasNewDmxData = false;
        
        // Throttled UI Timer & FPS Timer
        private DispatcherTimer _uiUpdateTimer = new DispatcherTimer();
        private DispatcherTimer _statsTimer = new DispatcherTimer();
        
        // Statistics counters
        private long _fpsPacketCount = 0;
        private long _totalPackets = 0;
        private string _lastSenderIp = "N/A";

        // Performance Optimized Brushes (Pre-cached)
        private SolidColorBrush[] _bgBrushes = new SolidColorBrush[256];
        private SolidColorBrush[] _borderBrushes = new SolidColorBrush[256];
        private SolidColorBrush _zeroTextBrush = new SolidColorBrush(Color.FromRgb(90, 90, 105));
        private SolidColorBrush _activeTextBrush = new SolidColorBrush(Color.FromRgb(240, 240, 245));

        public MainWindow()
        {
            InitializeComponent();
            InitializeBrushes();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private void InitializeBrushes()
        {
            // Cache zero value brushes
            _zeroTextBrush.Freeze();
            _activeTextBrush.Freeze();

            _bgBrushes[0] = new SolidColorBrush(Color.FromRgb(22, 22, 28)); // Deep dark grey/black
            _bgBrushes[0].Freeze();

            _borderBrushes[0] = new SolidColorBrush(Color.FromRgb(42, 42, 53)); // Soft border
            _borderBrushes[0].Freeze();

            // Pre-calculate color gradients for 1-255 DMX values
            for (int i = 1; i < 256; i++)
            {
                float factor = i / 255.0f;

                // Background: Interpolate from Base Dark Grey (#16161C) to Deep Glowing Cyan (#005573)
                byte bgR = (byte)(22 + (0 - 22) * factor);
                byte bgG = (byte)(22 + (85 - 22) * factor);
                byte bgB = (byte)(28 + (115 - 28) * factor);
                _bgBrushes[i] = new SolidColorBrush(Color.FromRgb(bgR, bgG, bgB));
                _bgBrushes[i].Freeze();

                // Border: Interpolate from Soft Grey (#2A2A35) to Neon Cyan (#00E5FF)
                byte borderR = (byte)(42 + (0 - 42) * factor);
                byte borderG = (byte)(42 + (229 - 42) * factor);
                byte borderB = (byte)(53 + (255 - 53) * factor);
                _borderBrushes[i] = new SolidColorBrush(Color.FromRgb(borderR, borderG, borderB));
                _borderBrushes[i].Freeze();
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Log("Inizializzazione sistema in corso...");
            
            // Build visual DMX Channel grid
            BuildDmxVisualGrid();
            
            // Load configuration options
            LoadNetworkInterfaces();
            LoadComPorts();
            
            // Setup timers
            _uiUpdateTimer.Interval = TimeSpan.FromMilliseconds(30); // ~33 FPS refresh rate
            _uiUpdateTimer.Tick += UiUpdateTimer_Tick;
            
            _statsTimer.Interval = TimeSpan.FromSeconds(1);
            _statsTimer.Tick += StatsTimer_Tick;

            Log("Sistema pronto. Configura i parametri e premi AVVIA.");
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            StopServer();
        }

        private void BuildDmxVisualGrid()
        {
            GridDmxChannels.Children.Clear();

            for (int i = 0; i < 512; i++)
            {
                // Visual block container
                Border cellBorder = new Border
                {
                    Background = _bgBrushes[0],
                    BorderBrush = _borderBrushes[0],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(1.5),
                    Height = 40
                };

                Grid cellGrid = new Grid();
                cellGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                cellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                // Channel number label
                TextBlock lblNum = new TextBlock
                {
                    Text = (i + 1).ToString("D3"),
                    FontSize = 8,
                    Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 125)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                Grid.SetRow(lblNum, 0);
                cellGrid.Children.Add(lblNum);

                // DMX value label
                TextBlock lblVal = new TextBlock
                {
                    Text = "0",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = _zeroTextBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(lblVal, 1);
                cellGrid.Children.Add(lblVal);

                cellBorder.Child = cellGrid;

                _channelBorders[i] = cellBorder;
                _channelValueTexts[i] = lblVal;

                GridDmxChannels.Children.Add(cellBorder);
            }
        }

        private void LoadNetworkInterfaces()
        {
            ComboIpAddress.Items.Clear();
            ComboIpAddress.Items.Add("0.0.0.0 (Tutte le interfacce)");

            try
            {
                string hostName = Dns.GetHostName();
                IPHostEntry host = Dns.GetHostEntry(hostName);
                
                foreach (IPAddress ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ComboIpAddress.Items.Add(ip.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[ERRORE] Impossibile recuperare gli indirizzi IP: {ex.Message}");
            }

            ComboIpAddress.SelectedIndex = 0;
        }

        private void LoadComPorts()
        {
            ComboComPort.Items.Clear();
            
            try
            {
                string[] ports = SerialPort.GetPortNames();
                foreach (string port in ports)
                {
                    ComboComPort.Items.Add(port);
                }

                if (ComboComPort.Items.Count > 0)
                {
                    ComboComPort.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Log($"[ERRORE] Impossibile recuperare le porte COM: {ex.Message}");
            }
        }

        private void ComboDmxDriver_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboComPort == null || BtnRefreshCom == null) return;

            // Enable COM selection only if a physical USB DMX interface is selected
            bool needsCom = ComboDmxDriver.SelectedIndex == 1 || ComboDmxDriver.SelectedIndex == 2;
            ComboComPort.IsEnabled = needsCom;
            BtnRefreshCom.IsEnabled = needsCom;
        }

        private void BtnRefreshCom_Click(object sender, RoutedEventArgs e)
        {
            LoadComPorts();
            Log("Elenco delle porte seriali COM aggiornato.");
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            StartServer();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopServer();
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLogConsole.Clear();
        }

        private void StartServer()
        {
            try
            {
                Log("Avvio del server in corso...");

                // 1. Get configuration
                string selectedIp = ComboIpAddress.SelectedItem?.ToString() ?? "0.0.0.0";
                if (selectedIp.Contains(" "))
                {
                    selectedIp = selectedIp.Split(' ')[0]; // Strip the friendly name
                }

                int universe = ComboUniverse.SelectedIndex;
                int driverIndex = ComboDmxDriver.SelectedIndex;
                string portName = ComboComPort.SelectedItem?.ToString() ?? "";

                if ((driverIndex == 1 || driverIndex == 2) && string.IsNullOrEmpty(portName))
                {
                    MessageBox.Show("Selezionare una porta COM valida per l'interfaccia hardware scelta.", "Errore COM", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Log("[ERRORE] Avvio annullato: nessuna porta COM selezionata.");
                    return;
                }

                // 2. Initialize and connect DMX Interface
                switch (driverIndex)
                {
                    case 0:
                        _dmxInterface = new SimulationDmxInterface();
                        break;
                    case 1:
                        _dmxInterface = new EnttecProDmxInterface();
                        break;
                    case 2:
                        _dmxInterface = new OpenDmxInterface();
                        break;
                    default:
                        _dmxInterface = new SimulationDmxInterface();
                        break;
                }

                Log($"Connessione all'interfaccia DMX ({_dmxInterface.GetType().Name})...");
                _dmxInterface.Connect(portName);
                Log($"Interfaccia DMX in stato: {_dmxInterface.ConnectionStatus}");

                // 3. Initialize and start Art-Net server
                _artNetServer = new ArtNetServer
                {
                    BindIpAddress = selectedIp,
                    TargetUniverse = universe,
                    Port = 6454
                };

                _artNetServer.DmxReceived += ArtNetServer_DmxReceived;
                _artNetServer.ErrorOccurred += ArtNetServer_ErrorOccurred;
                _artNetServer.LogMessage += ArtNetServer_LogMessage;

                _artNetServer.Start();

                if (_artNetServer.IsRunning)
                {
                    // Update UI Controls
                    ComboIpAddress.IsEnabled = false;
                    ComboUniverse.IsEnabled = false;
                    ComboDmxDriver.IsEnabled = false;
                    ComboComPort.IsEnabled = false;
                    BtnRefreshCom.IsEnabled = false;
                    
                    BtnPlay.IsEnabled = false;
                    BtnStop.IsEnabled = true;

                    // Start UI timers
                    _uiUpdateTimer.Start();
                    _statsTimer.Start();

                    UpdateLedState("running");
                }
                else
                {
                    UpdateLedState("error");
                    _dmxInterface.Disconnect();
                    _dmxInterface = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Si è verificato un errore durante l'avvio:\n{ex.Message}", "Errore Avvio", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"[ERRORE] Inizializzazione fallita: {ex.Message}");
                UpdateLedState("error");
                
                if (_dmxInterface != null)
                {
                    _dmxInterface.Disconnect();
                    _dmxInterface = null;
                }
            }
        }

        private void StopServer()
        {
            Log("Arresto del server in corso...");

            // Stop timers
            _uiUpdateTimer.Stop();
            _statsTimer.Stop();

            // Stop Art-Net Server
            if (_artNetServer != null)
            {
                _artNetServer.Stop();
                _artNetServer.DmxReceived -= ArtNetServer_DmxReceived;
                _artNetServer.ErrorOccurred -= ArtNetServer_ErrorOccurred;
                _artNetServer.LogMessage -= ArtNetServer_LogMessage;
                _artNetServer = null;
            }

            // Disconnect DMX Hardware
            if (_dmxInterface != null)
            {
                _dmxInterface.Disconnect();
                Log($"Interfaccia DMX scollegata. {_dmxInterface.ConnectionStatus}");
                _dmxInterface = null;
            }

            // Zero out local buffer and reset channels on GUI
            lock (_dmxLock)
            {
                Array.Clear(_dmxDataBuffer, 0, _dmxDataBuffer.Length);
                _hasNewDmxData = true;
            }
            
            // Execute one manual UI update to visual-zero the channels
            UiUpdateTimer_Tick(null, null!);

            // Reset UI controls
            ComboIpAddress.IsEnabled = true;
            ComboUniverse.IsEnabled = true;
            ComboDmxDriver.IsEnabled = true;
            
            bool needsCom = ComboDmxDriver.SelectedIndex == 1 || ComboDmxDriver.SelectedIndex == 2;
            ComboComPort.IsEnabled = needsCom;
            BtnRefreshCom.IsEnabled = needsCom;

            BtnPlay.IsEnabled = true;
            BtnStop.IsEnabled = false;

            TxtFps.Text = "0";
            _fpsPacketCount = 0;
            _totalPackets = 0;
            TxtTotalPackets.Text = "0";
            TxtLastSender.Text = "N/A";

            UpdateLedState("stopped");
            Log("Server arrestato con successo.");
        }

        private void ArtNetServer_DmxReceived(object? sender, DmxEventArgs e)
        {
            // Thread-safe copy of incoming data
            lock (_dmxLock)
            {
                int copyLength = Math.Min(e.DmxData.Length, 512);
                Array.Copy(e.DmxData, 0, _dmxDataBuffer, 0, copyLength);
                
                // If the packet has fewer channels, clear the remaining
                if (copyLength < 512)
                {
                    Array.Clear(_dmxDataBuffer, copyLength, 512 - copyLength);
                }

                _hasNewDmxData = true;
            }

            // Update stats (atomic threadsafety is not critical here since it's just GUI display)
            _fpsPacketCount++;
            _totalPackets++;
            _lastSenderIp = e.SenderIp;

            // Route to physical DMX adapter immediately on the network thread to minimize jitter/latency
            _dmxInterface?.SendDmx(e.DmxData);
        }

        private void ArtNetServer_ErrorOccurred(object? sender, string errorMessage)
        {
            Dispatcher.BeginInvoke(() => {
                Log($"[ERRORE SERVER] {errorMessage}");
                UpdateLedState("error");
            });
        }

        private void ArtNetServer_LogMessage(object? sender, string message)
        {
            Dispatcher.BeginInvoke(() => Log(message));
        }

        private void UiUpdateTimer_Tick(object? sender, EventArgs e)
        {
            bool updateNeeded = false;
            byte[] localData = new byte[512];

            // Safely fetch snapshot of current DMX values
            lock (_dmxLock)
            {
                if (_hasNewDmxData)
                {
                    Array.Copy(_dmxDataBuffer, 0, localData, 0, 512);
                    _hasNewDmxData = false;
                    updateNeeded = true;
                }
            }

            if (updateNeeded)
            {
                // Perform fast visual updates using our frozen brushes
                for (int i = 0; i < 512; i++)
                {
                    byte val = localData[i];
                    
                    _channelValueTexts[i].Text = val.ToString();
                    _channelBorders[i].Background = _bgBrushes[val];
                    _channelBorders[i].BorderBrush = _borderBrushes[val];
                    
                    if (val == 0)
                    {
                        _channelValueTexts[i].Foreground = _zeroTextBrush;
                    }
                    else
                    {
                        _channelValueTexts[i].Foreground = _activeTextBrush;
                    }
                }
            }
        }

        private void StatsTimer_Tick(object? sender, EventArgs e)
        {
            // Show stats
            TxtFps.Text = _fpsPacketCount.ToString();
            TxtTotalPackets.Text = _totalPackets.ToString();
            TxtLastSender.Text = _lastSenderIp;
            _fpsPacketCount = 0;
        }

        private void Log(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            TxtLogConsole.AppendText($"[{timestamp}] {message}\n");
            TxtLogConsole.ScrollToEnd();
        }

        private void UpdateLedState(string state)
        {
            if (state == "running")
            {
                StatusLed.Background = new SolidColorBrush(Color.FromRgb(0, 230, 118)); // Vivid Green
                LedGlow.Color = Color.FromRgb(0, 230, 118);
                TxtStatusLabel.Text = "IN FUNZIONE";
                TxtStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
            }
            else if (state == "stopped")
            {
                StatusLed.Background = new SolidColorBrush(Color.FromRgb(62, 62, 74)); // Muted Gray
                LedGlow.Color = Color.FromRgb(62, 62, 74);
                TxtStatusLabel.Text = "SPENTO";
                TxtStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(143, 143, 163));
            }
            else if (state == "error")
            {
                StatusLed.Background = new SolidColorBrush(Color.FromRgb(255, 23, 68)); // Vivid Red
                LedGlow.Color = Color.FromRgb(255, 23, 68);
                TxtStatusLabel.Text = "ERRORE";
                TxtStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(255, 23, 68));
            }
        }
    }
}