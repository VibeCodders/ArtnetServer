# Art-Net to DMX Node Server

Questo progetto è un server **Art-Net Node** scritto in C# (.NET 10.0) che riceve pacchetti DMX via rete (protocollo Art-Net) e li inoltra a un'interfaccia hardware DMX. Il codice è progettato per massimizzare la condivisione logica ed è eseguibile in tre modalità distinte: **GUI** (Grafica), **CLI** (Console interattiva) e **Headless** (Silenziosa in background).

---

## 🛠️ Come compilare il progetto

Per compilare il progetto è necessario avere installato [.NET 10 SDK](https://dotnet.microsoft.com/download).

Apri il terminale nella cartella del progetto ed esegui:
```bash
dotnet build
```

---

## 🚀 Modalità di Esecuzione

Puoi selezionare la modalità desiderata passando il parametro `-m` o `--mode` all'avvio.

### 1. 🖥️ Modalità GUI (Interfaccia Grafica) - *Predefinita*
Avvia l'interfaccia utente grafica (WPF) ricca di animazioni, che mostra in tempo reale i valori dei 512 canali DMX, le statistiche dei pacchetti e permette la configurazione visuale dei parametri.
*   **Come avviarla:**
    ```bash
    dotnet run
    ```
    *(oppure semplicemente facendo doppio clic sull'eseguibile compilato `Artnet.exe` in Esplora File).*

### 2. 📟 Modalità CLI (Riga di Comando)
Avvia un terminale testuale interattivo direttamente all'interno della console corrente (o ne alloca una nuova se avviato esternamente). Mostra i log dell'engine in tempo reale ed espone statistiche aggregate.

Per default, la modalità CLI avvia il server in modo **veloce e leggero**, con tutte le feature opzionali disabilitate. Puoi abilitarle tramite flag dedicate.

*   **Comandi da tastiera disponibili durante l'esecuzione:**
    *   `S` / `stats` - Mostra le statistiche dettagliate correnti.
    *   `C` / `clear` - Pulisce la schermata della console.
    *   `H` / `help` - Mostra l'elenco dei comandi disponibili.
    *   `Q` / `quit` / `exit` - Ferma in sicurezza l'engine Art-Net e disconnette il driver DMX per poi uscire.
*   **Come avviarla:**
    ```bash
    dotnet run -- --mode cli --driver simulation
    ```

### 3. 👤 Modalità Headless (Silenziosa / Background)
Esegue il server in background in modo totalmente trasparente e silenzioso, senza mostrare alcuna GUI o stampare log in console. Ideale per essere integrato come servizio o script di avvio automatico.

*   **Come arrestarla:**
    *   Inviare un segnale di interruzione standard come `Ctrl+C` nel terminale, oppure terminare il processo tramite Task Manager / terminale.
*   **Come avviarla:**
    ```bash
    dotnet run -- --mode headless --driver simulation --universe 0
    ```

---

## ⚙️ Parametri della Linea di Comando

I parametri possono essere forniti in formato breve o esteso.

### Parametri Base

| Parametro Breve | Parametro Esteso | Descrizione | Valore Predefinito |
| :--- | :--- | :--- | :--- |
| `-m` | `--mode` | Definisce la modalità di esecuzione: `gui`, `cli`, `headless` | `gui` |
| `-i` | `--ip` | Indirizzo IP di bind della scheda di rete su cui ricevere Art-Net. Usare `0.0.0.0` per restare in ascolto su tutte le interfacce. | `0.0.0.0` |
| `-p` | `--port` | Porta UDP di ascolto del server Art-Net. | `6454` |
| `-u` | `--universe` | Numero dell'universo Art-Net target da intercettare (da `0` in poi). | `0` |
| `-d` | `--driver` | Tipo di interfaccia driver DMX di uscita. | `simulation` |
| `-c` | `--com` | Porta seriale COM fisica a cui è connesso l'adattatore hardware DMX (es. `COM3`, `COM4`). | *Nessuno* |
| `-dev` | `--device` | Configura un'interfaccia DMX (formato: `universo,driver,com`). Ripetibile. | - |
| `-h` | `--help` | Mostra la guida all'uso dei parametri e termina l'esecuzione. | - |

### Feature Flags (disabilitate di default in CLI/Headless)

Queste flag abilitano funzionalità aggiuntive che restano **spente** di default per mantenere il server veloce e leggero.

| Flag | Descrizione |
| :--- | :--- |
| `--enable-http` | Abilita la dashboard web e l'API HTTP. |
| `--enable-health-checks` | Abilita i controlli periodici di salute sulle connessioni DMX. |
| `--enable-merge` | Abilita il merge HTP/LTP tra sorgenti multiple. |
| `--enable-logging` | Abilita il logging su file nella cartella `logs/`. |
| `--enable-heartbeat` | Abilita l'invio periodico di heartbeat DMX per validare il collegamento. |
| `--enable-rate-limit` | Abilita il rate limiting sulle richieste HTTP. |
| `--enable-cli-blackout` | Abilita il comando `blackout` da CLI. |
| `--enable-cli-override` | Abilita i comandi di override manuale da CLI. |
| `--verbose` | Abilita i log verbosi a livello Debug. |
| `--quiet` | Riduce l'output della CLI al minimo. |

---

## 🎛️ Esempi Pratici

1.  **Avvio minimale in CLI (solo Art-Net receiver, senza feature aggiuntive):**
    ```bash
    Artnet.exe --mode cli --driver simulation
    ```

2.  **Avvio in CLI con dashboard web e health checks:**
    ```bash
    Artnet.exe --mode cli --driver simulation --enable-http --enable-health-checks
    ```

3.  **Avvio in Headless con merge e logging su file:**
    ```bash
    Artnet.exe --mode headless --universe 2 --enable-merge --enable-logging --verbose
    ```

4.  **Avvio in CLI con interfaccia hardware Enttec Pro e blackout abilitato:**
    ```bash
    Artnet.exe --mode cli --driver enttec --com COM3 --universe 1 --enable-cli-blackout
    ```

5.  **Multi-universo con dispositivi multipli:**
    ```bash
    Artnet.exe --mode cli --device 0,simulation --device 1,enttec,COM3 --enable-http
    ```

6.  **Visualizzare l'aiuto in linea da console:**
    ```bash
    Artnet.exe --help
    ```

---

## 📁 Struttura del Progetto

| Cartella | Descrizione |
| :--- | :--- |
| `Core/` | Motore principale, server HTTP, merge manager, health check, logging |
| `Drivers/` | Driver di uscita DMX hardware (Enttec, OpenDMX, FTDI, ecc.) |
| `Views/` | Interfaccia grafica WPF |
| `Tests/` | Test unitari xUnit |

---

## 🔧 Configurazione (appsettings.json)

Oltre ai flag da CLI, puoi configurare il server tramite `appsettings.json` o variabili d'ambiente. Le opzioni disponibili sono documentate in `Core/ArtnetOptions.cs`.

Per default, tutte le feature opzionali sono **disattivate** nel file di configurazione.

---

## 📊 API HTTP (quando abilitata con `--enable-http`)

Quando la dashboard web è abilitata, sono disponibili i seguenti endpoint:

| Metodo | Endpoint | Descrizione |
| :--- | :--- | :--- |
| `GET` | `/` | Dashboard HTML |
| `GET` | `/api/status` | Stato del server e delle interfacce |
| `GET` | `/api/dmx?universe=` | Dati DMX correnti |
| `GET` | `/api/events` | Eventi in tempo reale (SSE) |
| `GET` | `/api/universes` | Lista universi attivi |
| `POST` | `/api/blackout` | Attiva/disattiva blackout |
| `POST` | `/api/override/set` | Imposta override canale |
| `POST` | `/api/override/clear-channel` | Cancella override canale |
| `POST` | `/api/override/clear` | Cancella tutti gli override |

L'autenticazione API è supportata tramite header `Authorization: Bearer <token>` configurando `ApiToken` in `appsettings.json`.
