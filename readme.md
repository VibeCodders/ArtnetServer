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
*   **Comandi da tastiera disponibili durante l'esecuzione:**
    *   `S` - Mostra le statistiche dettagliate correnti (IP, porta, driver, universo, pacchetti totali ricevuti).
    *   `C` - Pulisce la schermata della console.
    *   `Q` - Ferma in sicurezza l'engine Art-Net e disconnette il driver DMX per poi uscire.
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

| Parametro Breve | Parametro Esteso | Descrizione | Valore Predefinito |
| :--- | :--- | :--- | :--- |
| `-m` | `--mode` | Definisce la modalità di esecuzione: `gui`, `cli`, `headless` | `gui` |
| `-i` | `--ip` | Indirizzo IP di bind della scheda di rete su cui ricevere Art-Net. Usare `0.0.0.0` per restare in ascolto su tutte le interfacce. | `0.0.0.0` |
| `-p` | `--port` | Porta UDP di ascolto del server Art-Net. | `6454` |
| `-u` | `--universe` | Numero dell'universo Art-Net target da intercettare (da `0` in poi). | `0` |
| `-d` | `--driver` | Tipo di interfaccia driver DMX di uscita: `simulation` (mock), `enttec` (Enttec Pro), `open` (Open DMX). | `simulation` |
| `-c` | `--com` | Porta seriale COM fisica a cui è connesso l'adattatore hardware DMX (es. `COM3`, `COM4`). *Richiesta per driver `enttec` e `open`.* | *Nessuno* |
| `-h` | `--help` | Mostra la guida all'uso dei parametri e termina l'esecuzione. | - |

---

## 💡 Esempi Pratici

1.  **Avvio in CLI con interfaccia hardware Enttec Pro sulla porta COM3 nell'universo DMX 1:**
    ```bash
    Artnet.exe --mode cli --driver enttec --com COM3 --universe 1
    ```

2.  **Avvio in Headless (silenzioso) sul bind IP `192.168.1.50` in ascolto sull'universo 0:**
    ```bash
    Artnet.exe --mode headless --ip 192.168.1.50 --driver simulation
    ```

3.  **Visualizzare l'aiuto in linea da console:**
    ```bash
    Artnet.exe --help
    ```
