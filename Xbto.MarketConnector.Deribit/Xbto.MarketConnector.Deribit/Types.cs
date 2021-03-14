using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Diagnostics;

namespace Xbto.MarketConnector.Deribit
{
    public enum InstruKind
    {
        future, option
    }
    public enum Currency
    {
        BTC, ETH, USDT, USD
    }
    public enum SettlementPeriod
    {
        week, perpetual, month, day
    }

    public class InstrumentDef
    {
        public decimal tick_size = 0.01M;
        public decimal taker_commission = 0.0005M;
        public SettlementPeriod settlement_period;
        public Currency quote_currency;
        public long min_trade_amount = 1;
        public decimal maker_commission = 0.0001M;
        public long leverage = 100;
        public InstruKind kind;
        public bool is_active = true;
        public string instrument_name = "BTC-26JUL19";
        public long expiration_timestamp = 1564153200000;
        public long creation_timestamp = 1563522420000;
        public long contract_size = 10;
        public Currency base_currency;
    }
    public class QuoteData
    {
        public long timestamp;
        public decimal best_bid_price;
        public decimal best_bid_amount;
        public decimal best_ask_price;
        public decimal best_ask_amount;

        public const int SizeInBytes = 8 + 4 * Helper.DecimalSize;

        public QuoteData()
        {

        }
        public QuoteData(byte[] d)
        {
            int offset =0;
            timestamp = BitConverter.ToInt64(d, offset);
            offset += 8;
            best_bid_price = d.ToDecimal(ref offset);
            best_bid_amount = d.ToDecimal(ref offset);
            best_ask_price = d.ToDecimal(ref offset);
            best_ask_amount = d.ToDecimal(ref offset);

        }
        public QuoteData(byte[] d, ref int offset)
        {
            timestamp =BitConverter.ToInt64(d, offset);
            offset += 8;
            best_bid_price = d.ToDecimal(ref offset);
            best_bid_amount = d.ToDecimal(ref offset);
            best_ask_price = d.ToDecimal(ref offset);
            best_ask_amount = d.ToDecimal(ref offset);

        }

        // dateTime.Now - this.timestamp, converted in ms
        public long FromNowInMs()
        {
            return (DateTime.UtcNow.ToDeribitTs() - timestamp);
        }


        public byte[] BinSerialize()
        {
            byte[] b = new byte[QuoteData.SizeInBytes];

            int offset = 0;
            Helper.AddBuffer(b, BitConverter.GetBytes(timestamp), ref offset);
            Helper.AddBuffer(b, best_bid_price.SerialDecimal(), ref offset);
            Helper.AddBuffer(b, best_bid_amount.SerialDecimal(), ref offset);
            Helper.AddBuffer(b, best_ask_price.SerialDecimal(), ref offset);
            Helper.AddBuffer(b, best_ask_amount.SerialDecimal(), ref offset);
            Debug.Assert(offset == QuoteData.SizeInBytes);
            return b;
        }
        void ToStream(Stream s)
        {
            s.Write(BinSerialize());
        }
        public static QuoteData FromBinStream(Stream s)
        {
            byte[] b = new byte[QuoteData.SizeInBytes];

            if (s.Read(b, 0, QuoteData.SizeInBytes) == QuoteData.SizeInBytes)
                return new QuoteData(b);

            return null;

        }
    }

}
