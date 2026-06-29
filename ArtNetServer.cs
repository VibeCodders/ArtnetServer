using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ArtnetNode
{
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
        
        // Configuration
        public string BindIpAddress { get; set; } = "0.0.0.0";
        public int TargetUniverse { get; set; } = 0;
        public int Port { get; set; } = 6454;

        // Statistics
        public long TotalPacketsReceived { get; private set; }
        public string LastSenderIpAddress { get; private set; } = "N/A";
        
        // Events
        public event EventHandler<DmxEventArgs>? DmxReceived;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<string>? LogMessage;

        public bool IsRunning => _isRunning;

        public void Start()
        {
            if (_isRunning) return;

            _cts = new CancellationTokenSource();
            
            try
            {
                IPAddress ipAddress = IPAddress.Parse(BindIpAddress);
                IPEndPoint localEndPoint = new IPEndPoint(ipAddress, Port);
                
                // Allow sharing port if another app binds to it (optional but good for UDP testing on same machine)
                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(localEndPoint);

                _isRunning = true;
                TotalPacketsReceived = 0;
                LastSenderIpAddress = "N/A";

                LogMessage?.Invoke(this, $"Server Art-Net avviato su {BindIpAddress}:{Port}, in ascolto per Universo {TargetUniverse}");

                // Start receive loop
                Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                _isRunning = false;
                _udpClient?.Close();
                _udpClient = null;
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

                    if (data.Length < 18) 
                        continue; // Packet too short to be Art-Net

                    // 1. Check Header: "Art-Net\0"
                    // Bytes 0 to 7
                    if (data[0] != 'A' || data[1] != 'r' || data[2] != 't' || data[3] != '-' ||
                        data[4] != 'N' || data[5] != 'e' || data[6] != 't' || data[7] != 0)
                    {
                        continue; // Not Art-Net
                    }

                    // 2. Check OpCode (Bytes 8-9, Little Endian, ArtDmx is 0x5000)
                    int opCode = data[8] | (data[9] << 8);
                    if (opCode != 0x5000)
                    {
                        // Ignore other opcodes (e.g. ArtPoll, ArtPollReply) for basic receiver,
                        // but they are valid Art-Net packets
                        continue;
                    }

                    // 3. Check ProtVer (Bytes 10-11, Big Endian, should be 14)
                    int protVer = (data[10] << 8) | data[11];
                    if (protVer < 14)
                    {
                        // Old protocol version, but usually we can still parse it
                    }

                    // 4. Sequence number (Byte 12)
                    byte sequence = data[12];

                    // 5. Physical port (Byte 13)
                    byte physical = data[13];

                    // 6. Universe (Bytes 14-15)
                    // Byte 14: SubUni (lower 8 bits of universe)
                    // Byte 15: Net (upper 7 bits of universe)
                    int universe = data[14] | ((data[15] & 0x7F) << 8);

                    // 7. Data Length (Bytes 16-17, Big Endian)
                    int length = (data[16] << 8) | data[17];
                    if (length < 2 || length > 512)
                        continue; // DMX packet length must be between 2 and 512 channels

                    if (data.Length < 18 + length)
                        continue; // Packet is smaller than declared length

                    TotalPacketsReceived++;
                    LastSenderIpAddress = senderIp;

                    // Parse DMX data
                    byte[] dmxData = new byte[length];
                    Array.Copy(data, 18, dmxData, 0, length);

                    // Filter by target universe
                    if (universe == TargetUniverse)
                    {
                        DmxReceived?.Invoke(this, new DmxEventArgs(dmxData, universe, senderIp, sequence));
                    }
                }
                catch (ObjectDisposedException)
                {
                    // UdpClient closed, exit loop safely
                    break;
                }
                catch (OperationCanceledException)
                {
                    // Task cancelled, exit loop safely
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        ErrorOccurred?.Invoke(this, $"Errore di ricezione UDP: {ex.Message}");
                    }
                }
            }
        }
    }
}
