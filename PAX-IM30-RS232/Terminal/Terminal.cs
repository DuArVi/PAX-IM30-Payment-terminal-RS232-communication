using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;
using static PAX_IM30_RS232.Consts;
using static PAX_IM30_RS232.Frame;

namespace PAX_IM30_RS232
{
    public class Terminal: IDisposable
    {
        private SerialPort _serialPort;
        private ConcurrentQueue<byte> _buffer = new ConcurrentQueue<byte>();
        private readonly object _lockObject = new object();

        private CancellationToken _token;

        private bool isSynced = false;


        public Terminal(SerialPort serialPort, CancellationToken token)
        {
            _token = token;
            _serialPort = serialPort ?? throw new ArgumentNullException(nameof(serialPort));
            _serialPort.Open();
            _serialPort.DataReceived += new SerialDataReceivedEventHandler(OnDataReceived);
        }

        public void Dispose()
        {
            _serialPort.Close();
        }

        public void Sync()
        {
            try
            {
                Console.WriteLine("Syncing..");

                byte[] syncFrame = [STX, VERSION, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x03, ETX];
                SendFrame(syncFrame);

                if (!IsAckReceived([0x00, 0x01]))
                    throw new Exception("Failed to receive ACK for sync frame");

                var frame = GetNextReceivedFrame();

                if (
                    frame.Length == 18 &&
                    frame.Take(8).SequenceEqual(new byte[] { STX, VERSION, 0x00, 0x01, 0x00, 0x01, 0x00, 0x08 })
                )
                {
                    SendAck([frame[4], frame[5]]);

                    isSynced = true;

                    var paSize = frame.Skip(8).Take(4).ToArray();
                    var frSize = frame.Skip(12).Take(4).ToArray();
                    Console.WriteLine($"Sync OK\r\nMaximum supported packet size = {BinaryPrimitives.ReadInt32BigEndian(paSize)}\r\n" +
                        $"Maximum supported frame size = {BinaryPrimitives.ReadInt32BigEndian(frSize)}");

                    return;
                }
                
                throw new Exception($"Invalid sync responce received: {BitConverter.ToString(frame)}");
            }
            catch (Exception ex)
            {
                isSynced = false;
                Console.WriteLine($"Sync error: {ex.Message}");
            }
        }

        public string Sale(int amount)
        {
            try
            {
                Sync();

                Console.WriteLine($"Operation: SALE, amount: {amount}");

                if (!isSynced)
                    throw new InvalidOperationException("Terminal is not synced. Call Sync() method first.");
                if (amount <= 0)
                    throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");

                var data = $"{{\"task\":\"sale\",\"data\":{{\"amount\":\"{amount}\"}}}}";

                return SendDataFrame(data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Sale: {ex.Message}");
                return "";
            }
        }

        public string Refund(string transactionId, int amount)
        {
            try
            {
                Sync();

                Console.WriteLine($"Operation: REFUND, transactionId: {transactionId}, amount: {amount}");

                if (!isSynced)
                    throw new InvalidOperationException("Terminal is not synced. Call Sync() method first.");
                if (amount <= 0)
                    throw new ArgumentOutOfRangeException("Amount must be greater than zero.");
                if (string.IsNullOrEmpty(transactionId))
                    throw new ArgumentNullException("Transaction ID cannot be null or empty.");

                var data = $"{{\"task\":\"refund\",\"data\":{{\"transId\":\"{transactionId}\",\"amount\":\"{amount}\"}}}}";
                
                return SendDataFrame(data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Refund: {ex.Message}");
                return "";
            }
        }

        private string SendDataFrame(string data)
        {
            var frame = ComposeFrame([0x03, 0xE8], [0x00, 0x01], Encoding.ASCII.GetBytes(data));
            SendFrame(frame);

            if (!IsAckReceived([0x00, 0x01]))
                throw new Exception("Transaction failed: no ACK received from device");

            Console.WriteLine("Awaiting response...");
            var responseFrame = GetNextReceivedFrame(SALE_TIMEOUT);
            if (responseFrame.Length > 10 && responseFrame[2] == 0x03 && responseFrame[3] == 0xE8)
            {
                SendAck([responseFrame[4], responseFrame[5]]);
                var dataLength = (int)BinaryPrimitives.ReadInt16BigEndian(responseFrame.Skip(6).Take(2).ToArray());
                var responseData = Encoding.ASCII.GetString(responseFrame.Skip(8).Take(dataLength).ToArray());
                Console.WriteLine($"Transaction response: {responseData}");
                return responseData;
            }

            throw new Exception($"Invalid transaction response frame received: {BitConverter.ToString(responseFrame)}");
        }

        private void SendAck(byte[] FrNo)
        {
            var frame = ComposeFrame([0x00, 0x00], FrNo, [0x06]);
            SendFrame(frame);
        }

        private void SendFrame(byte[] frame)
        {
            lock (_lockObject)
            {
                _serialPort.BaseStream.Write(frame);
            }
        }

        private bool IsAckReceived(byte[] FrNo)
        {
            if (FrNo.Length != 2)
                throw new ArgumentException("FrNo must be 2 bytes long");
            
            var frame = GetNextReceivedFrame();

            var ret = frame.Length == 11 && 
                frame.Take(9).SequenceEqual(new byte[] { STX, VERSION, 0x00, 0x00, FrNo[0], FrNo[1], 0x00, 0x01, 0x06 });

            if (ret)
                Console.WriteLine($"Request sent");

            return ret;
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                byte[] buf = new byte[_serialPort.BytesToRead];

                var cnt = _serialPort.Read(buf, 0, buf.Length);

                bool stxFound = false;
                int frameLength = 0;

                foreach (var b in buf)
                    _buffer.Enqueue(b);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading from serial port: {ex.Message}");
            }
        }

        private byte[] GetNextReceivedFrame(int timeout = TIMEOUT)
        {
            var now = DateTime.Now;

            byte[] frame = [];
            bool stxFound = false;

            while (DateTime.Now - now < TimeSpan.FromMilliseconds(timeout) && !_token.IsCancellationRequested)
            {
                while (_buffer.TryDequeue(out byte b))
                {
                    switch (b) 
                    {
                        case STX:
                            if (stxFound)
                            {
                                Console.WriteLine($"Received unexpected STX, dropping previous frame: {BitConverter.ToString(frame)}");
                                frame = [];
                            }
                            stxFound = true;
                            frame = [b];
                            
                            break;

                        case ETX:
                            if (!stxFound)
                            {
                                Console.WriteLine($"Received ETX without STX, dropping: {BitConverter.ToString(frame)}");
                                frame = [];

                                break;
                            }

                            frame = frame.Append(b).ToArray();

                            if (frame.Length < 10)
                                break;

                            if (isValidChecksum(frame))
                            {
                                return frame;
                            }
                            else
                            {
                                Console.WriteLine($"Invalid checksum, dropping {BitConverter.ToString(frame)}");
                                frame = [];
                                stxFound = false;
                            }
                            
                            break;

                        default:
                            if (stxFound)
                                frame = frame.Append(b).ToArray();
                            else
                                Console.WriteLine($"Unknown data in buffer: {BitConverter.ToString([b])}");

                            break;
                    }
                }
                Thread.Sleep(100);
            }

            throw new TimeoutException("Timeout while waiting for responce");
        }
    }
}
