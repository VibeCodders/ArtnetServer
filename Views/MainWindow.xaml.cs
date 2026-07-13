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
using ArtnetNode.Core;
using ArtnetNode.Core.Logging;
using ArtnetNode.Drivers;
using Microsoft.Extensions.DependencyInjection;

namespace Artnet.Views
{
    public partial class MainWindow : Window
    {
        private ArtnetNodeEngine? _engine;
        private readonly IServiceProvider? _serviceProvider;

        private Border[] _channelBorders = new Border[512];
        private TextBlock[] _channelValueTexts = new TextBlock[512];
        public List<DmxInterfaceConfig> InitialDevices { get; } = new List<DmxInterfaceConfig>();
        private readonly System.Collections.ObjectModel.ObservableCollection<DmxInterfaceConfig> _configuredDevices = new();
        private readonly Dictionary<int, byte[]> _universeBuffers = new Dictionary<int, byte[]>();
        private int _monitoredUniverse = 0;
        private readonly object _dmxLock = new object();
        private bool _hasNewDmxData = false;

        private DispatcherTimer _uiUpdateTimer = new DispatcherTimer();
        private DispatcherTimer _statsTimer = new DispatcherTimer();

        private long _fpsPacketCount = 0;
        private long _totalPackets = 0;
        private string _lastSenderIp = "N/A";

        private SolidColorBrush[] _bgBrushes = new SolidColorBrush[256];
        private SolidColorBrush[] _borderBrushes = new SolidColorBrush[256];
        private SolidColorBrush _zeroTextBrush = new SolidColorBrush(Color.FromRgb(90, 90, 105));
        private SolidColorBrush _activeTextBrush = new SolidColorBrush(Color.FromRgb(240, 240, 245));

        public MainWindow() : this(null)
        {
        }

        public MainWindow(IServiceProvider? serviceProvider)
        {
            _serviceProvider = serviceProvider;
            InitializeComponent();
            InitializeBrushes();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private void InitializeBrushes()
        {
            _zeroTextBrush.Freeze();
            _activeTextBrush.Freeze();

            _bgBrushes[0] = new SolidColorBrush(Color.FromRgb(22, 22, 28));
            _bgBrushes[0].Freeze();

            _borderBrushes[0] = new SolidColorBrush(Color.FromRgb(42, 42, 53));
            _borderBrushes[0].Freeze();

            for (int i = 1; i < 256; i++)
            {
                float factor = (i - 1) / 254.0f;
                double hue = 0.6666 * (1.0 - factor);
                double saturation = 0.85;
                double bgLightness = 0.12 + 0.28 * factor;
                Color bgColor = ColorFromHsl(hue, saturation, bgLightness);
                _bgBrushes[i] = new SolidColorBrush(bgColor);
                _bgBrushes[i].Freeze();

                double borderLightness = 0.22 + 0.38 * factor;
                Color borderColor = ColorFromHsl(hue, saturation, borderLightness);
                _borderBrushes[i] = new SolidColorBrush(borderColor);
                _borderBrushes[i].Freeze();
            }
        }

        private static Color ColorFromHsl(double h, double s, double l)
        {
            double r = 0, g = 0, b = 0;
            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
                double p = 2.0 * l - q;
                r = HueToRgb(p, q, h + 1.0 / 3.0);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3.0);
            }
            return Color.FromRgb((byte)Math.Clamp(r * 255, 0, 255), (byte)Math.Clamp(g * 255, 0, 255), (byte)Math.Clamp(b * 255, 0, 255));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1.0;
            if (t > 1) t -= 1.0;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Log("Inizializzazione sistema in corso...");

            ListConfiguredDevices.ItemsSource = _configuredDevices;

            if (InitialDevices.Count > 0)
            {
                foreach (var dev in InitialDevices)
                {
                    _configuredDevices.Add(dev);
                }
            }
            else
            {
                _configuredDevices.Add(new DmxInterfaceConfig
                {
                    Universe = 0,
                    DriverType = "simulation",
                    ComPort = ""
                });
            }

            BuildDmxVisualGrid();
            LoadNetworkInterfaces();
            LoadComPorts();
            RefreshMonitorUniverses();

            _uiUpdateTimer.Interval = TimeSpan.FromMilliseconds(30);
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

            int idx = ComboDmxDriver.SelectedIndex;
            bool needsCom = idx == 1 || idx == 2 || idx == 3 || idx == 4 || idx == 6 || idx == 8;
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
                if (_configuredDevices.Count == 0)
                {
                    MessageBox.Show("Configurare almeno un'interfaccia DMX prima di avviare il server.", "Nessuna Interfaccia", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Log("[ERRORE] Avvio annullato: nessuna interfaccia DMX configurata.");
                    return;
                }

                Log("Avvio del server in corso...");

                string selectedIp = ComboIpAddress.SelectedItem?.ToString() ?? "0.0.0.0";
                if (selectedIp.Contains(" "))
                {
                    selectedIp = selectedIp.Split(' ')[0];
                }

                _engine = _serviceProvider?.GetRequiredService<ArtnetNodeEngine>();
                if (_engine == null)
                {
                    _engine = new ArtnetNodeEngine(
                        new ArtnetNode.Drivers.DriverFactory(),
                        new SimpleLoggerAdapter(msg => LogMessageInternal(msg)),
                        new ArtnetOptions());
                }

                _engine.BindIpAddress = selectedIp;
                _engine.Port = 6454;
                _engine.Interfaces.AddRange(_configuredDevices);

                _engine.DmxReceived += ArtNetServer_DmxReceived;
                _engine.ErrorOccurred += ArtNetServer_ErrorOccurred;
                _engine.LogMessage += ArtNetServer_LogMessage;

                Log($"Connessione a {_configuredDevices.Count} interfacce DMX...");
                _engine.Start();
                Log($"Interfacce DMX in stato: {_engine.ConnectionStatus}");

                if (_engine.IsRunning)
                {
                    ComboIpAddress.IsEnabled = false;
                    ComboUniverse.IsEnabled = false;
                    ComboDmxDriver.IsEnabled = false;
                    ComboComPort.IsEnabled = false;
                    BtnRefreshCom.IsEnabled = false;
                    BtnAddDevice.IsEnabled = false;
                    ListConfiguredDevices.IsEnabled = false;

                    BtnPlay.IsEnabled = false;
                    BtnStop.IsEnabled = true;
                    BtnBlackout.IsEnabled = true;

                    _uiUpdateTimer.Start();
                    _statsTimer.Start();

                    string ip = _engine.BindIpAddress;
                    if (ip == "0.0.0.0")
                    {
                        TxtWebUrl.Text = $"http://localhost:{_engine.HttpPort}/";
                    }
                    else
                    {
                        TxtWebUrl.Text = $"http://{ip}:{_engine.HttpPort}/";
                    }

                    UpdateLedState("running");
                }
                else
                {
                    UpdateLedState("error");
                    _engine = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Si e verificato un errore durante l'avvio:\n{ex.Message}", "Errore Avvio", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"[ERRORE] Inizializzazione fallita: {ex.Message}");
                UpdateLedState("error");
                _engine = null;
            }
        }

        private void StopServer()
        {
            Log("Arresto del server in corso...");

            _uiUpdateTimer.Stop();
            _statsTimer.Stop();

            if (_engine != null)
            {
                _engine.Stop();
                _engine.DmxReceived -= ArtNetServer_DmxReceived;
                _engine.ErrorOccurred -= ArtNetServer_ErrorOccurred;
                _engine.LogMessage -= ArtNetServer_LogMessage;
                _engine = null;
            }

            lock (_dmxLock)
            {
                _universeBuffers.Clear();
                _hasNewDmxData = true;
            }

            UiUpdateTimer_Tick(null, null!);

            ComboIpAddress.IsEnabled = true;
            ComboUniverse.IsEnabled = true;
            ComboDmxDriver.IsEnabled = true;
            BtnAddDevice.IsEnabled = true;
            ListConfiguredDevices.IsEnabled = true;

            int idx = ComboDmxDriver.SelectedIndex;
            bool needsCom = idx == 1 || idx == 2 || idx == 3 || idx == 4 || idx == 6 || idx == 8;
            ComboComPort.IsEnabled = needsCom;
            BtnRefreshCom.IsEnabled = needsCom;

            BtnPlay.IsEnabled = true;
            BtnStop.IsEnabled = false;

            if (BtnBlackout != null)
            {
                BtnBlackout.IsChecked = false;
                BtnBlackout.IsEnabled = false;
                BtnBlackout.Background = new SolidColorBrush(Color.FromRgb(38, 28, 8));
                BtnBlackout.BorderBrush = new SolidColorBrush(Color.FromRgb(178, 106, 0));
            }

            TxtFps.Text = "0";
            _fpsPacketCount = 0;
            _totalPackets = 0;
            TxtTotalPackets.Text = "0";
            TxtLastSender.Text = "N/A";
            TxtWebUrl.Text = "Sconnesso";

            UpdateLedState("stopped");
            Log("Server arrestato con successo.");
        }

        private void ArtNetServer_DmxReceived(object? sender, DmxEventArgs e)
        {
            lock (_dmxLock)
            {
                if (!_universeBuffers.TryGetValue(e.Universe, out byte[]? buffer))
                {
                    buffer = new byte[512];
                    _universeBuffers[e.Universe] = buffer;
                }
                int copyLength = Math.Min(e.DmxData.Length, 512);
                Array.Clear(buffer, 0, 512);
                Array.Copy(e.DmxData, 0, buffer, 0, copyLength);

                _hasNewDmxData = true;
            }

            _fpsPacketCount++;
            _totalPackets++;
            _lastSenderIp = e.SenderIp;
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

        private void LogMessageInternal(string message)
        {
            Dispatcher.BeginInvoke(() => Log(message));
        }

        private void UiUpdateTimer_Tick(object? sender, EventArgs e)
        {
            bool updateNeeded = false;
            byte[] localData = new byte[512];

            lock (_dmxLock)
            {
                if (_hasNewDmxData)
                {
                    if (_universeBuffers.TryGetValue(_monitoredUniverse, out byte[]? buffer))
                    {
                        Array.Copy(buffer, 0, localData, 0, 512);
                    }
                    _hasNewDmxData = false;
                    updateNeeded = true;
                }
            }

            if (updateNeeded)
            {
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
                StatusLed.Background = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                LedGlow.Color = Color.FromRgb(0, 230, 118);
                TxtStatusLabel.Text = "IN FUNZIONE";
                TxtStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
            }
            else if (state == "stopped")
            {
                StatusLed.Background = new SolidColorBrush(Color.FromRgb(62, 62, 74));
                LedGlow.Color = Color.FromRgb(62, 62, 74);
                TxtStatusLabel.Text = "SPENTO";
                TxtStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(143, 143, 163));
            }
            else if (state == "error")
            {
                StatusLed.Background = new SolidColorBrush(Color.FromRgb(255, 23, 68));
                LedGlow.Color = Color.FromRgb(255, 23, 68);
                TxtStatusLabel.Text = "ERRORE";
                TxtStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(255, 23, 68));
            }
        }

        private void BtnBlackout_Click(object sender, RoutedEventArgs e)
        {
            if (_engine != null && BtnBlackout != null)
            {
                bool isActive = BtnBlackout.IsChecked ?? false;
                _engine.BlackoutActive = isActive;

                if (isActive)
                {
                    BtnBlackout.Background = new SolidColorBrush(Color.FromRgb(128, 60, 0));
                    BtnBlackout.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 145, 0));
                    Log("[INFO] Modalita BLACKOUT attivata. Tutti i canali DMX sono stati azzerati.");
                }
                else
                {
                    BtnBlackout.Background = new SolidColorBrush(Color.FromRgb(38, 28, 8));
                    BtnBlackout.BorderBrush = new SolidColorBrush(Color.FromRgb(178, 106, 0));
                    Log("[INFO] Modalita BLACKOUT disattivata. Ripristino controllo Art-Net.");
                }
            }
        }

        private void BtnAddDevice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int universe = ComboUniverse.SelectedIndex;
                if (universe < 0) universe = 0;

                int driverIndex = ComboDmxDriver.SelectedIndex;
                if (driverIndex < 0) driverIndex = 0;

                string portName = ComboComPort.SelectedItem?.ToString() ?? "";

                if ((driverIndex == 1 || driverIndex == 2 || driverIndex == 3 || driverIndex == 4 || driverIndex == 6 || driverIndex == 8) && string.IsNullOrEmpty(portName))
                {
                    MessageBox.Show("Selezionare una porta COM valida per l'interfaccia hardware scelta.", "Errore COM", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string driverType = driverIndex switch
                {
                    0 => "simulation",
                    1 => "enttec",
                    2 => "open",
                    3 => "enttec_mk2",
                    4 => "ftdi_generic",
                    5 => "udmx",
                    6 => "dmx4all",
                    7 => "chauvet",
                    8 => "eurolite_pro",
                    9 => "hid_dmx",
                    _ => "simulation"
                };

                if (_configuredDevices.Any(d => d.Universe == universe && d.DriverType == driverType && d.ComPort == portName))
                {
                    MessageBox.Show("Questa interfaccia DMX e gia configurata per questo universo.", "Duplicato", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var newDevice = new DmxInterfaceConfig
                {
                    Universe = universe,
                    DriverType = driverType,
                    ComPort = portName
                };

                _configuredDevices.Add(newDevice);
                RefreshMonitorUniverses();
                Log($"Aggiunta interfaccia: {newDevice}");
            }
            catch (Exception ex)
            {
                Log($"[ERRORE] Impossibile aggiungere l'interfaccia: {ex.Message}");
            }
        }

        private void BtnRemoveDevice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DmxInterfaceConfig device)
            {
                _configuredDevices.Remove(device);
                RefreshMonitorUniverses();
                Log($"Rimossa interfaccia: {device}");
            }
        }

        private void RefreshMonitorUniverses()
        {
            int selectedUniverse = -1;
            if (ComboMonitorUniverse.SelectedItem is int u)
            {
                selectedUniverse = u;
            }

            ComboMonitorUniverse.SelectionChanged -= ComboMonitorUniverse_SelectionChanged;
            ComboMonitorUniverse.Items.Clear();

            var universes = _configuredDevices.Select(d => d.Universe).Distinct().OrderBy(u => u).ToList();
            foreach (var uni in universes)
            {
                ComboMonitorUniverse.Items.Add(uni);
            }

            if (universes.Count > 0)
            {
                if (universes.Contains(selectedUniverse))
                {
                    ComboMonitorUniverse.SelectedItem = selectedUniverse;
                }
                else
                {
                    ComboMonitorUniverse.SelectedIndex = 0;
                }
            }
            ComboMonitorUniverse.SelectionChanged += ComboMonitorUniverse_SelectionChanged;

            _monitoredUniverse = ComboMonitorUniverse.SelectedItem is int val ? val : 0;
        }

        private void ComboMonitorUniverse_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboMonitorUniverse.SelectedItem is int val)
            {
                _monitoredUniverse = val;
                lock (_dmxLock)
                {
                    _hasNewDmxData = true;
                }
            }
        }

        private void TxtWebUrl_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (TxtWebUrl.Text != "Sconnesso" && TxtWebUrl.Text.StartsWith("http"))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = TxtWebUrl.Text,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Log($"[ERRORE] Impossibile aprire il browser: {ex.Message}");
                }
            }
        }
    }

    public class SimpleLoggerAdapter
    {
        private readonly Action<string> _logAction;

        public SimpleLoggerAdapter(Action<string> logAction)
        {
            _logAction = logAction;
        }

        public void Log(string message)
        {
            _logAction(message);
        }
    }
}
