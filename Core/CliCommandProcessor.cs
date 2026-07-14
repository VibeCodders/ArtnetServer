using System;
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

        string cmd = input.Trim().ToLowerInvariant();

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
                if (_options.EnableCliBlackout)
                {
                    _engine.BlackoutActive = !_engine.BlackoutActive;
                    Console.WriteLine($"Blackout: {(_engine.BlackoutActive ? "ATTIVO" : "DISATTIVO")}");
                }
                else
                {
                    Console.WriteLine("Blackout non abilitato. Usa --enable-cli-blackout per attivarlo.");
                }
                break;
            case "o":
            case "override":
            case "clear-override":
                if (_options.EnableCliOverride)
                {
                    _engine.ClearManualOverrides();
                    Console.WriteLine("Override manuale cancellato.");
                }
                else
                {
                    Console.WriteLine("Override non abilitato. Usa --enable-cli-override per attivarlo.");
                }
                break;
            case "h":
            case "help":
                PrintHelp();
                break;
            case "r":
            case "reconnect":
                Console.WriteLine("Riconnessione non supportata da CLI.");
                break;
            default:
                Console.WriteLine($"Comando non riconosciuto: {input}. Digita 'h' per aiuto.");
                break;
        }

        return false;
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
