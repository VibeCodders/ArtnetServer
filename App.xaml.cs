using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ArtnetNode.Core;
using Artnet.Views;

namespace Artnet;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    private const int ATTACH_PARENT_PROCESS = -1;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Parse command line arguments
        string mode = "gui";
        string ip = "0.0.0.0";
        int port = 6454;
        int universe = 0;
        string driver = "simulation";
        string comPort = "";
        var parsedDevices = new List<DmxInterfaceConfig>();

        for (int i = 0; i < e.Args.Length; i++)
        {
            string arg = e.Args[i].ToLowerInvariant();
            if ((arg == "--mode" || arg == "-m") && i + 1 < e.Args.Length)
            {
                mode = e.Args[++i].ToLowerInvariant();
            }
            else if ((arg == "--ip" || arg == "-i") && i + 1 < e.Args.Length)
            {
                ip = e.Args[++i];
            }
            else if ((arg == "--port" || arg == "-p") && i + 1 < e.Args.Length)
            {
                if (int.TryParse(e.Args[++i], out int parsedPort))
                    port = parsedPort;
            }
            else if ((arg == "--universe" || arg == "-u") && i + 1 < e.Args.Length)
            {
                if (int.TryParse(e.Args[++i], out int parsedUniverse))
                    universe = parsedUniverse;
            }
            else if ((arg == "--driver" || arg == "-d") && i + 1 < e.Args.Length)
            {
                driver = e.Args[++i].ToLowerInvariant();
            }
            else if ((arg == "--com" || arg == "-c") && i + 1 < e.Args.Length)
            {
                comPort = e.Args[++i];
            }
            else if ((arg == "--device" || arg == "-dev") && i + 1 < e.Args.Length)
            {
                string devVal = e.Args[++i];
                var parts = devVal.Split(',');
                if (parts.Length >= 2)
                {
                    if (int.TryParse(parts[0], out int parsedUniverse))
                    {
                        var devConfig = new DmxInterfaceConfig
                        {
                            Universe = parsedUniverse,
                            DriverType = parts[1]
                        };
                        if (parts.Length >= 3)
                        {
                            devConfig.ComPort = parts[2];
                        }
                        parsedDevices.Add(devConfig);
                    }
                }
            }
            else if (arg == "--help" || arg == "-h")
            {
                ShowHelp();
                Shutdown();
                return;
            }
        }

        if (mode == "cli")
        {
            RunCliMode(ip, port, universe, driver, comPort, parsedDevices);
        }
        else if (mode == "headless")
        {
            RunHeadlessMode(ip, port, universe, driver, comPort, parsedDevices);
        }
        else
        {
            // Default: GUI Mode
            var mainWindow = new MainWindow();
            if (parsedDevices.Count > 0)
            {
                mainWindow.InitialDevices.AddRange(parsedDevices);
            }
            mainWindow.Show();
        }
    }

    private void ShowHelp()
    {
        InitializeConsole();
        Console.WriteLine("\n=== Art-Net Node Server - Aiuto ===");
        Console.WriteLine("Parametri disponibili:");
        Console.WriteLine("  -m, --mode [gui|cli|headless]  Modalità di avvio (Predefinito: gui)");
        Console.WriteLine("  -i, --ip [ip_address]          Indirizzo IP su cui bindare il server Art-Net (Predefinito: 0.0.0.0)");
        Console.WriteLine("  -p, --port [port_number]       Porta UDP per Art-Net (Predefinito: 6454)");
        Console.WriteLine("  -u, --universe [num]           Universo DMX target (Predefinito: 0)");
        Console.WriteLine("  -d, --driver [sim|pro|open]    Driver di uscita DMX (simulation, enttec, open) (Predefinito: simulation)");
        Console.WriteLine("  -c, --com [port_name]          Porta COM seriale per driver hardware (es. COM3)");
        Console.WriteLine("  -dev, --device [uni,drv,com]   Configura un'interfaccia DMX (es: 0,simulation o 1,enttec,COM3)");
        Console.WriteLine("                                 Può essere ripetuto per configurare molteplici USB.");
        Console.WriteLine("  -h, --help                     Mostra questo messaggio di aiuto");
        Console.WriteLine("\nEsempi:");
        Console.WriteLine("  Artnet.exe --mode cli --driver open --com COM3");
        Console.WriteLine("  Artnet.exe --mode cli --device 0,simulation --device 1,enttec,COM3");
        Console.WriteLine("  Artnet.exe --mode headless --universe 2\n");
    }

    private void InitializeConsole()
    {
        if (!AttachConsole(ATTACH_PARENT_PROCESS))
        {
            AllocConsole();
        }

        // Re-initialize standard output/input streams
        var standardOutput = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        Console.SetOut(standardOutput);
        var standardError = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
        Console.SetError(standardError);
        var standardInput = new StreamReader(Console.OpenStandardInput());
        Console.SetIn(standardInput);
    }

    private void RunCliMode(string ip, int port, int universe, string driver, string comPort, List<DmxInterfaceConfig> devices)
    {
        InitializeConsole();

        Console.Clear();
        PrintCliHeader();

        var engine = new ArtnetNodeEngine
        {
            BindIpAddress = ip,
            TargetUniverse = universe,
            Port = port,
            DriverType = driver,
            ComPort = comPort
        };

        if (devices.Count > 0)
        {
            engine.Interfaces.AddRange(devices);
        }

        engine.LogMessage += (s, msg) => {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
            Console.ResetColor();
        };

        engine.ErrorOccurred += (s, err) => {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [ERRORE] {err}");
            Console.ResetColor();
        };

        try
        {
            engine.Start();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Errore fatale all'avvio dell'engine: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine(Console.IsInputRedirected ? "Uscita in corso..." : "Premere un tasto per uscire...");
            if (!Console.IsInputRedirected)
            {
                Console.ReadKey();
            }
            Shutdown();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Engine avviato con successo.");
        Console.ResetColor();

        var cts = new CancellationTokenSource();

        // Stats timer display task
        if (!Console.IsInputRedirected)
        {
            Task.Run(async () => {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(2000);
                    if (engine.IsRunning && !cts.Token.IsCancellationRequested)
                    {
                        // Print small inline status
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.Write($"\r[STAT] Pacchetti ricevuti: {engine.TotalPacketsReceived} | Ultimo IP: {engine.LastSenderIpAddress} | Stato DMX: {engine.ConnectionStatus}    ");
                        Console.ResetColor();
                    }
                }
            }, cts.Token);
        }

        bool exit = false;
        while (!exit)
        {
            if (Console.IsInputRedirected)
            {
                string? line = Console.ReadLine();
                if (line == null) // EOF
                {
                    exit = true;
                    break;
                }
                line = line.Trim().ToLowerInvariant();
                if (line == "q" || line == "quit" || line == "exit")
                {
                    exit = true;
                }
                else if (line == "s" || line == "stats")
                {
                    PrintCliStats(engine);
                }
                else if (line == "c" || line == "clear")
                {
                    Console.Clear();
                    PrintCliHeader();
                }
            }
            else
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Q)
                    {
                        exit = true;
                    }
                    else if (key == ConsoleKey.S)
                    {
                        PrintCliStats(engine);
                    }
                    else if (key == ConsoleKey.C)
                    {
                        Console.Clear();
                        PrintCliHeader();
                    }
                }
                Thread.Sleep(100);
            }
        }

        cts.Cancel();
        engine.Stop();
        FreeConsole();
        Shutdown();
    }

    private void PrintCliHeader()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("====================================================");
        Console.WriteLine("           ART-NET NODE - MODALITA' CLI             ");
        Console.WriteLine("====================================================");
        Console.ResetColor();
        Console.WriteLine("Comandi CLI disponibili:");
        Console.WriteLine("  [S] Mostra Statistiche attuali");
        Console.WriteLine("  [C] Pulisci Schermata");
        Console.WriteLine("  [Q] Disconnetti ed Esci");
        Console.WriteLine("----------------------------------------------------");
    }

    private void PrintCliStats(ArtnetNodeEngine engine)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n--- STATISTICHE ATTUALI (ore {DateTime.Now:HH:mm:ss}) ---");
        Console.WriteLine($"  - Indirizzo Bind: {engine.BindIpAddress}:{engine.Port}");
        Console.WriteLine($"  - Pacchetti Totali: {engine.TotalPacketsReceived}");
        Console.WriteLine($"  - Ultimo Mittente IP: {engine.LastSenderIpAddress}");
        Console.WriteLine($"  - Stato Connessione DMX: {engine.ConnectionStatus}");
        Console.WriteLine("  - Interfacce DMX Configurate:");
        foreach (var inst in engine.ActiveInterfaces)
        {
            string portText = string.IsNullOrEmpty(inst.Config.ComPort) ? "" : $" ({inst.Config.ComPort})";
            Console.WriteLine($"    * Universo {inst.Config.Universe}: {inst.Config.DriverType}{portText} -> Stato: {inst.ConnectionStatus}");
        }
        Console.WriteLine("-----------------------------------------");
        Console.ResetColor();
    }

    private void RunHeadlessMode(string ip, int port, int universe, string driver, string comPort, List<DmxInterfaceConfig> devices)
    {
        var engine = new ArtnetNodeEngine
        {
            BindIpAddress = ip,
            TargetUniverse = universe,
            Port = port,
            DriverType = driver,
            ComPort = comPort
        };

        if (devices.Count > 0)
        {
            engine.Interfaces.AddRange(devices);
        }

        engine.LogMessage += (s, msg) => System.Diagnostics.Debug.WriteLine($"[HEADLESS LOG] {msg}");
        engine.ErrorOccurred += (s, err) => System.Diagnostics.Debug.WriteLine($"[HEADLESS ERRORE] {err}");

        try
        {
            engine.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Errore fatale headless: {ex.Message}");
            Shutdown();
            return;
        }

        var exitEvent = new ManualResetEvent(false);

        try
        {
            Console.CancelKeyPress += (s, ev) => {
                ev.Cancel = true;
                exitEvent.Set();
            };
        }
        catch
        {
            // Ignore if console is not attached
        }

        AppDomain.CurrentDomain.ProcessExit += (s, ev) => {
            engine.Stop();
            exitEvent.Set();
        };

        // Wait for exit signal
        exitEvent.WaitOne();

        engine.Stop();
        Shutdown();
    }
}

