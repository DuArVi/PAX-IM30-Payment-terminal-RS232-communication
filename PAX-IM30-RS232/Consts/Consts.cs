namespace PAX_IM30_RS232
{
    public static class Consts
    {
        public const byte STX = 0x02;
        public const byte ETX = 0x03;
        public const byte ACK = 0x06;
        public const byte NAK = 0x15;
        public const byte VERSION = 0x01;

        public const int TIMEOUT = 1000;
        public const int SALE_TIMEOUT = 70000;
    }
}
