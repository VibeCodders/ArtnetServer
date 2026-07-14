using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ArtnetNode.Core
{
    public class ArtPollEventArgs : EventArgs
    {
        public string SenderIp { get; }
        public int SenderPort { get; }

        public ArtPollEventArgs(string senderIp, int senderPort)
        {
            SenderIp = senderIp;
            SenderPort = senderPort;
        }
    }

    public class DmxEventArgs : EventArgs
    {
        public byte[] DmxData { get; }
        public int Universe { get; }
        public string SenderIp { get; }
        public byte Sequence { get; }

        public DmxEventArgs(byte[] dmxData, int universe, string senderIp, byte sequence)
        {
            DmxData = dmxData;
            Universe = universe;
            SenderIp = senderIp;
            Sequence = sequence;
        }
    }

    public class ArtNetServer
    {
        private UdpClient? _udpClient;
        private CancellationTokenSource? _cts;
        private bool _isRunning;
        private readonly ILogger _logger;

        public string BindIpAddress { get; set; } = "0.0.0.0";
        public int TargetUniverse { get; set; } = 0;
        public HashSet<int> TargetUniverses { get; } = new HashSet<int>();
        public int Port { get; set; } = 6454;

        public long TotalPacketsReceived { get; private set; }
        public string LastSenderIpAddress { get; private set; } = "N/A";

        public event EventHandler<DmxEventArgs>? DmxReceived;
        public event EventHandler<ArtPollEventArgs>? PollReceived;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<string>? LogMessage;

        public bool IsRunning => _isRunning;

        public ArtNetServer(ILogger<ArtNetServer> logger)
        {
            _logger = logger;
        }

        public void SendPacket(byte[] data, string targetIp, int targetPort)
        {
            if (_udpClient == null || !_isRunning) return;
            try
            {
                _udpClient.Send(data, data.Length, targetIp, targetPort);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nell'invio del pacchetto Art-Net");
                LogMessage?.Invoke(this, $"Errore nell'invio del pacchetto Art-Net: {ex.Message}");
            }
        }

        public static bool TryParseDmxPacket(ReadOnlySpan<byte> data, out int universe, out byte sequence, out byte[] dmx)
        {
            universe = 0;
            sequence = 0;
            dmx = Array.Empty<byte>();

            if (data.Length < 14)
                return false;

            if (data[0] != 'A' || data[1] != 'r' || data[2] != 't' || data[3] != '-' ||
                data[4] != 'N' || data[5] != 'e' || data[6] != 't' || data[7] != 0)
                return false;

            int opCode = data[8] | (data[9] << 8);
            if (opCode != 0x5000)
                return false;

            if (data.Length < 18)
                return false;

            sequence = data[12];
            universe = data[14] | ((data[15] & 0x7F) << 8);
            int length = (data[16] << 8) | data[17];

            if (length < 2 || length > 512)
                return false;

            if (data.Length < 18 + length)
                return false;

            dmx = new byte[length];
            data.Slice(18, length).CopyTo(dmx);
            return true;
        }

        public void Start()
        {
            if (_isRunning) return;

            _cts = new CancellationTokenSource();

            try
            {
                IPAddress ipAddress = IPAddress.Parse(BindIpAddress);
                IPEndPoint localEndPoint = new IPEndPoint(ipAddress, Port);

                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(localEndPoint);

                _isRunning = true;
                TotalPacketsReceived = 0;
                LastSenderIpAddress = "N/A";

                if (TargetUniverses.Count == 0)
                {
                    TargetUniverses.Add(TargetUniverse);
                }

                string universesStr = string.Join(", ", TargetUniverses);
                _logger.LogInformation("Server Art-Net avviato su {Bind}:{Port}, universi [{Universes}]", BindIpAddress, Port, universesStr);
                LogMessage?.Invoke(this, $"Server Art-Net avviato su {BindIpAddress}:{Port}, in ascolto per Universi [{universesStr}]");

                Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore durante l'avvio del server");
                _isRunning = false;
                _udpClient?.Close();
                _udpClient = null;
                _cts?.Dispose();
                _cts = null;
                ErrorOccurred?.Invoke(this, $"Errore durante l'avvio del server: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cts?.Cancel();
            _udpClient?.Close();
            _udpClient = null;
            _cts?.Dispose();
            _cts = null;

            _logger.LogInformation("Server Art-Net arrestato");
            LogMessage?.Invoke(this, "Server Art-Net arrestato.");
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            byte[] headerBytes = new byte[8];

            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    UdpReceiveResult result = await _udpClient!.ReceiveAsync(token);
                    byte[] data = result.Buffer;
                    string senderIp = result.RemoteEndPoint.Address.ToString();

                    if (data.Length < 14)
                        continue;

                    if (data[0] != 'A' || data[1] != 'r' || data[2] != 't' || data[3] != '-' ||
                         data[4] != 'N' || data[5] != 'e' || data[6] != 't' || data[7] != 0)
                    {
                        continue;
                    }

                    int opCode = data[8] | (data[9] << 8);
                    if (opCode == 0x2000)
                    {
                        PollReceived?.Invoke(this, new ArtPollEventArgs(senderIp, result.RemoteEndPoint.Port));
                        continue;
                    }
                    if (opCode != 0x5000)
                    {
                        continue;
                    }

                    if (TryParseDmxPacket(data, out int universe, out byte sequence, out byte[] dmxData))
                    {
                        TotalPacketsReceived++;
                        LastSenderIpAddress = senderIp;

                        if (TargetUniverses.Contains(universe))
                        {
                            DmxReceived?.Invoke(this, new DmxEventArgs(dmxData, universe, senderIp, sequence));
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Errore di ricezione UDP");
                        ErrorOccurred?.Invoke(this, $"Errore di ricezione UDP: {ex.Message}");
                    }
                }
            }
        }
    }
}
