using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OptiFinish.Core
{
    /// <summary>
    /// Clean-room hardware transceiver for stadium scoreboards (RS-232 Serial & UDP network)
    /// and ultrasonic wind gauge telemetry.
    /// </summary>
    public sealed class HardwareTransceiver : IDisposable
    {
        private SerialPort _serialPort;
        private UdpClient _udpClient;
        private IPEndPoint _udpTarget;

        public string SerialPortName { get; set; }
        public int BaudRate { get; set; }
        public bool EnableSerialScoreboard { get; set; }
        public bool EnableUdpBroadcast { get; set; }
        public string UdpHost { get; set; }
        public int UdpPort { get; set; }
        public string LastTransmittedPayload { get; private set; }

        public bool IsSerialConnected
        {
            get { return _serialPort != null && _serialPort.IsOpen; }
        }

        public HardwareTransceiver()
        {
            SerialPortName = "COM1";
            BaudRate = 9600;
            EnableSerialScoreboard = false;
            EnableUdpBroadcast = true;
            UdpHost = "127.0.0.1";
            UdpPort = 41605;
            LastTransmittedPayload = "Idle";

            try
            {
                _udpClient = new UdpClient();
                _udpTarget = new IPEndPoint(IPAddress.Parse(UdpHost), UdpPort);
            }
            catch { }
        }

        public bool TryOpenSerial(string portName, int baudRate, out string error)
        {
            error = "";
            CloseSerial();
            try
            {
                SerialPortName = portName;
                BaudRate = baudRate;
                _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                _serialPort.Handshake = Handshake.None;
                _serialPort.WriteTimeout = 300;
                _serialPort.ReadTimeout = 300;
                _serialPort.Open();
                EnableSerialScoreboard = true;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                EnableSerialScoreboard = false;
                return false;
            }
        }

        public void CloseSerial()
        {
            if (_serialPort != null)
            {
                try
                {
                    if (_serialPort.IsOpen) _serialPort.Close();
                    _serialPort.Dispose();
                }
                catch { }
                _serialPort = null;
            }
            EnableSerialScoreboard = false;
        }

        public void TransmitRunningTime(double elapsedSeconds, string windDisplay)
        {
            string formatted = CompetitorEntry.FormatSeconds(Math.Max(0.001, elapsedSeconds), TimePrecision.Tenths);
            if (string.IsNullOrEmpty(formatted)) formatted = "0.0";
            string frame = string.Format(CultureInfo.InvariantCulture, "\x02RUN|{0,8}|{1}\x03\r\n", formatted, windDisplay ?? "");
            SendRawText(frame);
        }

        public void TransmitOfficialResults(RaceEventDocument doc)
        {
            if (doc == null) return;
            doc.RecalculatePlaces();

            StringBuilder sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "\x02HDR|EV:{0}-{1}-{2}|{3}|WIND:{4}\r\n",
                doc.EventNumber, doc.RoundNumber, doc.HeatNumber, doc.EventName, doc.GetWindFormatted());

            List<CompetitorEntry> sorted = new List<CompetitorEntry>(doc.Competitors);
            sorted.Sort(delegate(CompetitorEntry a, CompetitorEntry b)
            {
                if (a.HasValidNumericTime && b.HasValidNumericTime) return a.RawTimeSeconds.CompareTo(b.RawTimeSeconds);
                if (a.HasValidNumericTime) return -1;
                if (b.HasValidNumericTime) return 1;
                return a.Lane.CompareTo(b.Lane);
            });

            for (int i = 0; i < sorted.Count; i++)
            {
                CompetitorEntry c = sorted[i];
                sb.AppendFormat(CultureInfo.InvariantCulture, "RES|{0,3}|L{1}|{2,-4}|{3,-16}|{4,9}\r\n",
                    c.GetPlaceDisplay(),
                    c.Lane,
                    c.BibId,
                    c.LastName,
                    c.GetFormattedOfficialTime(doc.Precision));
            }
            sb.Append("\x03\r\n");
            SendRawText(sb.ToString());
        }

        private void SendRawText(string payload)
        {
            LastTransmittedPayload = payload.Replace("\x02", "").Replace("\x03", "").Replace("\r\n", " ").Trim();
            byte[] bytes = Encoding.ASCII.GetBytes(payload);

            if (EnableSerialScoreboard && _serialPort != null && _serialPort.IsOpen)
            {
                try { _serialPort.Write(bytes, 0, bytes.Length); } catch { }
            }

            if (EnableUdpBroadcast && _udpClient != null && _udpTarget != null)
            {
                try { _udpClient.Send(bytes, bytes.Length, _udpTarget); } catch { }
            }
        }

        public void Dispose()
        {
            CloseSerial();
            if (_udpClient != null)
            {
                try { _udpClient.Close(); } catch { }
                _udpClient = null;
            }
        }
    }
}
