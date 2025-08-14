using System.IO.Ports;

namespace PAX_IM30_RS232
{
    class Program
    {

        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Port not set. Exiting.");
                Console.WriteLine("Usage: PAX_IM30_RS232 <port> <baud rate>");
                return;
            }

            int baud = 0;
            if (!int.TryParse(args[1], out baud))
            {
                Console.WriteLine("Baud rate must be a number");
                return;
            }

            var serialPort = new SerialPort(
                    portName: args[0],
                    baudRate: baud,
                    parity: Parity.None,
                    dataBits: 8,
                    stopBits: StopBits.One
                );

            serialPort.Handshake = Handshake.None;
            serialPort.ReadTimeout = SerialPort.InfiniteTimeout;

            var cancellationTokenSource = new CancellationTokenSource();

            using (var device = new Terminal(serialPort, cancellationTokenSource.Token))
            {
                device.Sale(100);

                Console.ReadLine();

                device.Refund("123", 50);

                Console.ReadLine();
            }
            ;
        }
    }
}



