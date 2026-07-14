using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using ArtnetNode.Core;

namespace Artnet;

public class CliCommandProcessor
{
    private readonly ArtnetNodeEngine _engine;
    private readonly ILogger _logger;
    private readonly ArtnetOptions _options;

    public CliCommandProcessor(ArtnetNodeEngine engine, ILogger<CliCommandProcessor> logger, ArtnetOptions options)
    {
        _engine = engine;
        _logger = logger;
        _options = options;
    }

    public bool ProcessCommand(string input, bool quiet)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        var tokens = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string cmd = tokens[0].ToLowerInvariant();
        string args = input.Substring(tokens[0].Length).Trim();

        switch (cmd)
        {
            case "q":
            case "quit":
            case "exit":
                return true;
            case "s":
            case "stats":
                PrintStats();
                break;
            case "c":
            case "clear":
                Console.Clear();
                PrintHeader();
                break;
            case "b":
            case "blackout":
                HandleBlackout();
                break;
            case "o":
            case "override":
            case "clear-override":
                HandleClearOverride();
                break;
            case "set":
            case "override-set":
                HandleOverrideSet(args);
                break;
            case "clear-channel":
            case "override-clear-channel":
                HandleOverrideClearChannel(args);
                break;
            case "h":
            case "help":
                PrintHelp();
                break;
            case "r":
            case "reconnect":
                Console.WriteLine("Riconnessione non supportata da CLI.");
                break;
            case "interfaces":
            case "if":
                PrintInterfaces();
                break;
            case "universe":
            case "uni":
                HandleUniverseCommand(args);
                break;
            case "merge":
            case "merge-mode":
                HandleMergeMode(args);
                break;
            case "rate":
            case "rate-limit":
                PrintRateLimitStats();
                break;
            case "health":
            case "health-check":
                PrintHealthStatus();
                break;
            case "log":
            case "log-level":
                HandleLogLevel(args);
                break;
            default:
                Console.WriteLine($"Comando non riconosciuto: {input}. Digita 'h' per aiuto.");
                break;
        }

        return false;
    }

    private void HandleBlackout()
    {
        if (!_options.EnableCliBlackout)
        {
            Console.WriteLine("Blackout non abilitato. Usa --enable-cli-blackout per attivarlo.");
            return;
        }

        _engine.BlackoutActive = !_engine.BlackoutActive;
        Console.WriteLine($"Blackout: {(_engine.BlackoutActive ? "ATTIVO" : "DISATTIVO")}");
        _logger.LogInformation("CLI Blackout toggled to {State}", _engine.BlackoutActive);
    }

    private void HandleClearOverride()
    {
        if (!_options.EnableCliOverride)
        {
            Console.WriteLine("Override non abilitato. Usa --enable-cli-override per attivarlo.");
            return;
        }

        _engine.ClearManualOverrides();
        Console.WriteLine("Override manuale cancellato.");
        _logger.LogInformation("CLI Manual overrides cleared");
    }

    private void HandleOverrideSet(string args)
    {
        if (!_options.EnableCliOverride)
        {
            Console.WriteLine("Override non abilitato. Usa --enable-cli-override per attivarlo.");
            return;
        }

        var parts = args.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            Console.WriteLine("Formato: set <universo>,<canale>,<valore>  (es: set 0,1,255)");
            Console.WriteLine("Canale: 1-512, Valore: 0-255");
            return;
        }

        if (!int.TryParse(parts[0], out int universe) ||
            !int.TryParse(parts[1], out int channel) ||
            !byte.TryParse(parts[2], out byte value))
        {
            Console.WriteLine("Parametri non validi. Universo: int, Canale: 1-512, Valore: 0-255");
            return;
        }

        if (channel < 1 || channel > 512)
        {
            Console.WriteLine("Canale deve essere tra 1 e 512");
            return;
        }

        _engine.SetManualOverride(universe, channel - 1, value);
        Console.WriteLine($"Override impostato: Universo {universe}, Canale {channel} = {value}");
        _logger.LogInformation("CLI Override set: Universe={Universe}, Channel={Channel}, Value={Value}", universe, channel, value);
    }

    private void HandleOverrideClearChannel(string args)
    {
        if (!_options.EnableCliOverride)
        {
            Console.WriteLine("Override non abilitato. Usa --enable-cli-override per attivarlo.");
            return;
        }

        var parts = args.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            Console.WriteLine("Formato: clear-channel <universo>,<canale>  (es: clear-channel 0,1)");
            return;
        }

        if (!int.TryParse(parts[0], out int universe) ||
            !int.TryParse(parts[1], out int channel))
        {
            Console.WriteLine("Parametri non validi. Universo: int, Canale: 1-512");
            return;
        }

        if (channel < 1 || channel > 512)
        {
            Console.WriteLine("Canale deve essere tra 1 e 512");
            return;
        }

        _engine.ClearManualOverrideChannel(universe, channel - 1);
        Console.WriteLine($"Override canale cancellato: Universo {universe}, Canale {channel}");
        _logger.LogInformation("CLI Override channel cleared: Universe={Universe}, Channel={Channel}", universe, channel);
    }

    private void PrintInterfaces()
    {
        Console.WriteLine($"\n--- INTERFACCE ATTIVE ({_engine.ActiveInterfaces.Count}) ---");
        foreach (var inst in _engine.ActiveInterfaces)
        {
            string portText = string.IsNullOrEmpty(inst.Config.ComPort) ? "" : $" ({inst.Config.ComPort})";
            Console.WriteLine($"  U{inst.Config.Universe}: {inst.Config.DriverType}{portText} -> {inst.ConnectionStatus}");
            if (inst.IsReconnecting)
            {
                Console.WriteLine($"    Tentativo riconnessione: {inst.ReconnectAttempt}, Prossimo tra: {inst.ReconnectDelay}ms");
            }
        }
        Console.WriteLine("-----------------------------------------");
    }

    private void HandleUniverseCommand(string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            Console.WriteLine($"Universo corrente: {_engine.TargetUniverse}");
            Console.WriteLine("Uso: universe <numero>  |  universe list");
            return;
        }

        if (parts[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            var universes = _engine.ActiveInterfaces.Select(i => i.Config.Universe).Distinct().OrderBy(u => u);
            Console.WriteLine("Universi configurati: " + string.Join(", ", universes));
            return;
        }

        if (int.TryParse(parts[0], out int universe))
        {
            Console.WriteLine($"Comando universe {universe} ricevuto (impostazione runtime non supportata, usare --device all'avvio)");
        }
        else
        {
            Console.WriteLine("Numero universo non valido");
        }
    }

    private void HandleMergeMode(string args)
    {
        if (!_options.EnableHtpMerge)
        {
            Console.WriteLine("Merge non abilitato. Usa --enable-merge all'avvio.");
            return;
        }

        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            Console.WriteLine($"Modalità merge corrente: {_options.DefaultMergeMode}");
            Console.WriteLine("Uso: merge-mode [htp|ltp]");
            return;
        }

        if (Enum.TryParse<MergeMode>(parts[0], true, out var mode))
        {
            _options.DefaultMergeMode = mode;
            Console.WriteLine($"Modalità merge impostata su: {mode}");
            _logger.LogInformation("CLI Merge mode changed to {Mode}", mode);
        }
        else
        {
            Console.WriteLine("Modalità non valida. Usa: htp o ltp");
        }
    }

    private void PrintRateLimitStats()
    {
        if (!_options.EnableRateLimiting)
        {
            Console.WriteLine("Rate limiting non abilitato. Usa --enable-rate-limit all'avvio.");
            return;
        }

        Console.WriteLine("\n--- RATE LIMIT STATS ---");
        Console.WriteLine($"Max richieste: {_options.RateLimitMaxRequests} per {_options.RateLimitWindowSeconds}s");
        Console.WriteLine("-----------------------------------------");
    }

    private void PrintHealthStatus()
    {
        if (!_options.EnableHealthChecks)
        {
            Console.WriteLine("Health checks non abilitato. Usa --enable-health-checks all'avvio.");
            return;
        }

        Console.WriteLine("\n--- HEALTH STATUS ---");
        Console.WriteLine($"Engine running: {_engine.IsRunning}");
        Console.WriteLine($"Interfacce: {_engine.ActiveInterfaces.Count}");
        int connected = _engine.ActiveInterfaces.Count(i => i.Interface.IsConnected && !i.IsReconnecting);
        int reconnecting = _engine.ActiveInterfaces.Count(i => i.IsReconnecting);
        Console.WriteLine($"Connesse: {connected}, In riconnessione: {reconnecting}");
        Console.WriteLine($"Intervallo check: {_options.HealthCheckIntervalMs}ms");
        Console.WriteLine("-----------------------------------------");
    }

    private void HandleLogLevel(string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            Console.WriteLine($"Livello log corrente: {(_options.VerboseLogging ? "Debug" : "Information")}");
            Console.WriteLine("Uso: log-level [debug|information|warning|error]");
            return;
        }

        if (Enum.TryParse<LogLevel>(parts[0], true, out var level))
        {
            _options.VerboseLogging = level == LogLevel.Debug;
            Console.WriteLine($"Livello log impostato su: {level}");
            _logger.LogInformation("CLI Log level changed to {Level}", level);
        }
        else
        {
            Console.WriteLine("Livello non valido. Usa: debug, information, warning, error");
        }
    }

    private void PrintHeader()
    {
        Console.WriteLine("====================================================");
        Console.WriteLine("           ART-NET NODE - MODALITA' CLI             ");
        Console.WriteLine("====================================================");
        Console.WriteLine("Comandi base:");
        Console.WriteLine("  [S] Mostra Statistiche          [C] Pulisci Schermata");
        Console.WriteLine("  [H] Mostra Aiuto                [Q] Disconnetti ed Esci");
        if (_options.EnableCliBlackout)
        {
            Console.WriteLine("  [B] Toggle Blackout");
        }
        if (_options.EnableCliOverride)
        {
            Console.WriteLine("  [O] Cancella Override Manuale");
            Console.WriteLine("  set <uni>,<ch>,<val>   Imposta override (es: set 0,1,255)");
            Console.WriteLine("  clear-channel <uni>,<ch>  Cancella override canale");
        }
        Console.WriteLine("  interfaces              Mostra interfacce attive");
        Console.WriteLine("  universe [num|list]     Info universi");
        if (_options.EnableHtpMerge)
        {
            Console.WriteLine("  merge-mode [htp|ltp]    Modalità merge");
        }
        if (_options.EnableRateLimiting)
        {
            Console.WriteLine("  rate-limit              Statistiche rate limiting");
        }
        if (_options.EnableHealthChecks)
        {
            Console.WriteLine("  health-check            Stato health checks");
        }
        Console.WriteLine("  log-level [level]       Livello logging");
        Console.WriteLine("----------------------------------------------------");
    }

    private void PrintHelp()
    {
        Console.WriteLine("\nComandi disponibili:");
        Console.WriteLine("  s, stats                 Mostra statistiche dettagliate");
        Console.WriteLine("  c, clear                 Pulisci schermata");
        Console.WriteLine("  q, quit, exit            Esci");
        Console.WriteLine("  h, help                  Mostra questo aiuto");
        if (_options.EnableCliBlackout)
        {
            Console.WriteLine("  b, blackout              Attiva/disattiva blackout");
        }
        if (_options.EnableCliOverride)
        {
            Console.WriteLine("  o, override, clear-override  Cancella override manuale");
            Console.WriteLine("  set <uni>,<ch>,<val>     Imposta override canale (1-512, 0-255)");
            Console.WriteLine("  clear-channel <uni>,<ch> Cancella override singolo canale");
        }
        Console.WriteLine("  interfaces               Lista interfacce DMX attive");
        Console.WriteLine("  universe [num|list]      Info universi configurati");
        if (_options.EnableHtpMerge)
        {
            Console.WriteLine("  merge-mode [htp|ltp]     Imposta/visualizza modalità merge");
        }
        if (_options.EnableRateLimiting)
        {
            Console.WriteLine("  rate-limit               Mostra stats rate limiting");
        }
        if (_options.EnableHealthChecks)
        {
            Console.WriteLine("  health-check             Mostra stato health checks");
        }
        Console.WriteLine("  log-level [level]        Imposta livello log (debug/info/warning/error)");
        Console.WriteLine();
    }

    private void PrintStats()
    {
        Console.WriteLine($"\n--- STATISTICHE (ore {DateTime.Now:HH:mm:ss}) ---");
        Console.WriteLine($"  Bind: {_engine.BindIpAddress}:{_engine.Port}");
        Console.WriteLine($"  HTTP: {(_options.EnableHttpServer ? $"http://localhost:{_engine.HttpPort}/" : "disabilitato")}");
        Console.WriteLine($"  Pacchetti Totali: {_engine.TotalPacketsReceived}");
        Console.WriteLine($"  Ultimo IP: {_engine.LastSenderIpAddress}");
        Console.WriteLine($"  Stato DMX: {_engine.ConnectionStatus}");
        Console.WriteLine($"  Blackout: {(_engine.BlackoutActive ? "SI" : "NO")}");
        Console.WriteLine($"  Override: {(_engine.ManualOverrideActive ? "SI" : "NO")}");
        Console.WriteLine($"  Merge: {(_options.EnableHtpMerge ? _options.DefaultMergeMode.ToString() : "DISABILITATO")}");
        Console.WriteLine($"  Rate Limit: {(_options.EnableRateLimiting ? "ON" : "OFF")}");
        Console.WriteLine($"  Health Check: {(_options.EnableHealthChecks ? "ON" : "OFF")}");
        Console.WriteLine($"  Heartbeat: {(_options.EnableDmxHeartbeat ? "ON" : "OFF")}");
        Console.WriteLine("  Interfacce:");
        foreach (var inst in _engine.ActiveInterfaces)
        {
            string portText = string.IsNullOrEmpty(inst.Config.ComPort) ? "" : $" ({inst.Config.ComPort})";
            Console.WriteLine($"    * U{inst.Config.Universe}: {inst.Config.DriverType}{portText} -> {inst.ConnectionStatus}");
        }
        Console.WriteLine("-----------------------------------------");
    }
}

    private void PrintHeader()
    {
        Console.WriteLine("====================================================");
        Console.WriteLine("           ART-NET NODE - MODALITA' CLI             ");
        Console.WriteLine("====================================================");
        Console.WriteLine("Comandi disponibili:");
        Console.WriteLine("  [S] Mostra Statistiche");
        Console.WriteLine("  [C] Pulisci Schermata");
        Console.WriteLine("  [H] Mostra Aiuto");
        Console.WriteLine("  [Q] Disconnetti ed Esci");
        if (_options.EnableCliBlackout)
        {
            Console.WriteLine("  [B] Toggle Blackout");
        }
        if (_options.EnableCliOverride)
        {
            Console.WriteLine("  [O] Cancella Override Manuale");
        }
        Console.WriteLine("----------------------------------------------------");
    }

    private void PrintHelp()
    {
        Console.WriteLine("\nComandi disponibili:");
        Console.WriteLine("  s, stats     Mostra statistiche dettagliate");
        Console.WriteLine("  c, clear     Pulisci schermata");
        Console.WriteLine("  q, quit, exit Esci");
        Console.WriteLine("  h, help      Mostra questo aiuto");
        if (_options.EnableCliBlackout)
        {
            Console.WriteLine("  b, blackout  Attiva/disattiva blackout");
        }
        if (_options.EnableCliOverride)
        {
            Console.WriteLine("  o, override  Cancella override manuale");
        }
        Console.WriteLine();
    }

    private void PrintStats()
    {
        Console.WriteLine($"\n--- STATISTICHE (ore {DateTime.Now:HH:mm:ss}) ---");
        Console.WriteLine($"  Bind: {_engine.BindIpAddress}:{_engine.Port}");
        Console.WriteLine($"  HTTP: {( _options.EnableHttpServer ? $"http://localhost:{_engine.HttpPort}/" : "disabilitato" )}");
        Console.WriteLine($"  Pacchetti Totali: {_engine.TotalPacketsReceived}");
        Console.WriteLine($"  Ultimo IP: {_engine.LastSenderIpAddress}");
        Console.WriteLine($"  Stato DMX: {_engine.ConnectionStatus}");
        Console.WriteLine($"  Blackout: {(_engine.BlackoutActive ? "SI" : "NO")}");
        Console.WriteLine($"  Override: {(_engine.ManualOverrideActive ? "SI" : "NO")}");
        Console.WriteLine("  Interfacce:");
        foreach (var inst in _engine.ActiveInterfaces)
        {
            string portText = string.IsNullOrEmpty(inst.Config.ComPort) ? "" : $" ({inst.Config.ComPort})";
            Console.WriteLine($"    * U{inst.Config.Universe}: {inst.Config.DriverType}{portText} -> {inst.ConnectionStatus}");
        }
        Console.WriteLine("-----------------------------------------");
    }
}
