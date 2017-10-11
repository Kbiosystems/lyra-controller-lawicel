using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.IO;

namespace LyraElectronics.Controller
{
    public class LawicelCanSerialController : CanController
    {
        private const byte carriageReturn = 0x0D;
        private const byte versionCommand = 0x56;
        private const byte serialNumCommand = 0x4E;

        private List<byte[]> cmdResponses;

        private SerialPort Port;
        private CancellationTokenSource CancelSource;

        public string PortName { get; private set; }
        public bool IsConnected { get; private set; }
        public bool IsChannelOpened { get; private set; }
        public string HardwareVersion { get; private set; }
        public string SoftwareVersion { get; private set; }
        public string SerialNumber { get; private set; }

        public event EventHandler<ControllerMessageRecievedEventArgs> ControllerMessageRecieved;

        public LawicelCanSerialController(string portName)
        {
            PortName = portName;

            cmdResponses = new List<byte[]>();

            ControllerMessageRecieved += (s, b) =>
            {
                cmdResponses.Add(b.Data);
            };
        }


        public override void CloseChannel()
        {
            SendCommand("C");

            IsChannelOpened = false;
        }

        public override void OpenChannel(int baudRate = 250)
        {
            // turn auto polling on
            SendCommand("X1");

            //set baud rate
            string command = "S" + BaudToNumeric(baudRate).ToString();
            SendCommand(command);

            // open can channel
            SendCommand("O");

            IsChannelOpened = true;
        }

        public override void SendMessage(CanMessage message)
        {
            string command = "t";
            command += (Convert.ToString(message.Address, 16));
            command += (Convert.ToString(message.DataLength, 16));

            command += BitConverter.ToString(message.Data).Replace("-", "");

            SendCommand(command);
        }

        public async Task<string> GetVersion()
        {
            SendCommand("V");
            return await WaitForResponse(versionCommand).ConfigureAwait(false);
        }

        public async Task<string> GetSerialNumber()
        {
            SendCommand("N");
            return await WaitForResponse(serialNumCommand).ConfigureAwait(false);
        }

        internal async Task Connect()
        {
            if (Port != null) { Disconnect(); }

            Port = new SerialPort(PortName, 57600, Parity.None, 8, StopBits.One);
            Port.NewLine = "\r";

            Port.Open();

            Port.DiscardInBuffer();

            // clean buffer a little bit
            SendCommand("\r");
            SendCommand("\r");

            CancelSource = new CancellationTokenSource();

            ListenAsync();

            // request version
            //var result = await GetVersion().ConfigureAwait(false);
            //HardwareVersion = result[0].ToString() + result[1].ToString();
            //SoftwareVersion = result[2].ToString() + result[3].ToString();

            ////request serial number
            //SerialNumber = await GetSerialNumber().ConfigureAwait(false);

            IsConnected = true;
        }

        internal void Disconnect()
        {
            if (IsChannelOpened) { CloseChannel(); }

            if (Port != null && Port.IsOpen)
            {
                // stop listener task
                CancelSource?.Cancel(false);

                Thread.Sleep(500);

                Port.DiscardInBuffer();

                Thread.Sleep(500);

                Port.Close();
                Port.Dispose();
            }

            IsConnected = false;
        }

        private async Task<string> WaitForResponse(byte command)
        {
            var start = DateTime.Now;
            while (true)
            {
                if (start + TimeSpan.FromSeconds(2) < DateTime.Now)
                {
                    throw new TimeoutException("Timeout waiting for response from controller.");
                }

                for (int i = 0; i < cmdResponses.Count(); i++)
                {
                    if (cmdResponses[i][0] == command)
                    {
                        var response = Encoding.ASCII.GetString(cmdResponses[i]).Substring(1);
                        cmdResponses = new List<byte[]>();
                        return response;
                    }
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
        }

        private void SendCommand(string command)
        {
            lock (this)
            {
                Port.WriteLine(command);
            }
        }

        private async void ListenAsync()
        {
            try
            {
                byte[] buffer = new byte[4096];
                if (Port.IsOpen)
                {
                    var result = await Port.BaseStream.ReadAsync(buffer, 0, buffer.Length, CancelSource.Token).ConfigureAwait(false);
                    if (!CancelSource.Token.IsCancellationRequested)
                    {
                        try
                        {
                            byte[] received = new byte[result];
                            Buffer.BlockCopy(buffer, 0, received, 0, result);
                            Parse(received);
                        }
                        catch (IOException exc)
                        { }

                        if (!CancelSource.Token.IsCancellationRequested)
                        {
                            ListenAsync();
                        }
                    }
                }
            }
            finally
            { }
        }


        private List<byte> _buffer = new List<byte>();
        private void Parse(byte[] bytes)
        {
            try
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    if (bytes[i] == carriageReturn)
                    {
                        if (_buffer.Count > 0 && _buffer[0] == 0x74)
                        {
                            if (_buffer.Count == 21)
                            {
                                _buffer.RemoveAt(0);
                                OnCanMessageRecieved(CanMessage.Parse(_buffer.ToArray()));
                            }
                        }
                        else
                        {
                            ControllerMessageRecieved?.Invoke(this, new ControllerMessageRecievedEventArgs(_buffer.ToArray()));
                        }

                        _buffer = new List<byte>();
                    }
                    else
                    {
                        _buffer.Add(bytes[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Write(ex.Message);
            }
        }

        private int BaudToNumeric(int baud)
        {
            switch (baud)
            {
                case 10:
                    return 0;
                case 20:
                    return 1;
                case 50:
                    return 2;
                case 100:
                    return 3;
                case 125:
                    return 4;
                case 250:
                    return 5;
                case 500:
                    return 6;
                case 800:
                    return 7;
                case 1000:
                    return 8;
                default:
                    return 5;
            }
        }
    }
}
