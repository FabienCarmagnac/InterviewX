using System;
using System.Collections.Generic;
using System.Text;

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
    }

}
