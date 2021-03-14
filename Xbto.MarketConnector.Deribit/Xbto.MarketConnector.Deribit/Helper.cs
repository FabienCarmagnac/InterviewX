using System;
using System.Collections.Generic;
using System.Text;

namespace Xbto.MarketConnector.Deribit
{
    public static class Helper
    {
        // deribit ts vs datetime

        static System.DateTime DtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
        public static DateTime ToDateTime(this long deribitTs)
        {
            // Unix timestamp is seconds past epoch
            return DtDateTime.AddMilliseconds(deribitTs);
        }
        public static long ToDeribitTs(this DateTime d)
        {
            return Convert.ToInt64((d - DtDateTime).TotalMilliseconds);
        }

        public static void AddBuffer(byte[] dst, byte[] src, ref int dstOffset)
        {
            var n = src.Length;
            Buffer.BlockCopy(src,0, dst, dstOffset, n);
            dstOffset += n;
        }
        // decimal

        public const int DecimalSize = 4 * 4;
        public static byte[] SerialDecimal(this decimal d)
        {
            var b2 = Decimal.GetBits(d);
            List<byte> l = new List<byte>(DecimalSize);
            foreach (var b in b2)
            {
                //Console.WriteLine("SerialDecimal " + b);
                var bb = BitConverter.GetBytes(b);
                //if (BitConverter.IsLittleEndian)
                //    Array.Reverse(bb);
                l.AddRange(bb);
            }
            return l.ToArray();
        }
        public static decimal ToDecimal(this byte[] d, int offset = 0)
        {
            int[] ix = new int[4];
            ix[0] = BitConverter.ToInt32(d, offset + 0);
            ix[1] = BitConverter.ToInt32(d, offset + 4);
            ix[2] = BitConverter.ToInt32(d, offset + 8);
            ix[3] = BitConverter.ToInt32(d, offset + 12);

            //foreach(int i in ix)
            //    Console.WriteLine("ToDecimal " + i);

            return new Decimal(ix);

        }
        public static decimal ToDecimal(this byte[] d, ref int offset)
        {
            var res = ToDecimal(d, offset);
            offset += DecimalSize;
            return res;

        }
    }
}
