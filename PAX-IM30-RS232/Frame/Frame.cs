using static PAX_IM30_RS232.Consts;

namespace PAX_IM30_RS232
{
    public static class Frame
    {
        public static byte[] ComposeFrame(byte[] PaNo, byte[] FrNo, byte[] data)
        {

            if (PaNo.Length != 2 || FrNo.Length != 2)
                throw new ArgumentException("PaNo and FrNo must be 2 bytes long");

            short shortDataLen = (short)data.Length;
            byte[] dataLength = BitConverter.GetBytes(shortDataLen);

            if (BitConverter.IsLittleEndian)
                Array.Reverse(dataLength);

            var frame = new byte[]
                {
                    STX,
                    VERSION,
                    PaNo[0],
                    PaNo[1],
                    FrNo[0],
                    FrNo[1],
                    dataLength[0],
                    dataLength[1]
                };

            byte checksum = CalculateChecksum(frame, data);

            if (data.Length > 0)
                frame = frame.Concat(data).ToArray();

            return frame.Concat(new byte[] { checksum, ETX }).ToArray();
        }

        public static bool isValidChecksum(byte[] frame)
        {
            var framePayload = frame.Take(frame.Length - 2).ToArray();
            var checksum = frame.Skip(frame.Length - 2).Take(1).First();
            var calculatedChecksum = CalculateChecksum(framePayload);
            return checksum == calculatedChecksum;
        }

        public static byte CalculateChecksum(byte[] frame, byte[]? data = null)
        {
            byte lrc = frame[0];
            
            for (int i = 1; i < frame.Length; i++)
            {
                lrc ^= frame[i];
            }
            
            if (data == null || data.Length == 0)
                return lrc;

            for (int i = 0; i < data.Length; i++)
            {
                lrc ^= data[i];
            }
            return lrc;
        }
    }
}
