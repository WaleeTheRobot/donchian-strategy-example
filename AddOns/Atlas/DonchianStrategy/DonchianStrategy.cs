using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.Indicators;
using System;
using System.ComponentModel.DataAnnotations;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class DonchianStrategy : Strategy
    {
        // Order pending flags
        private bool _buyUpperPending = false;
        private bool _sellUpperPending = false;
        private bool _buyMeanPending = false;
        private bool _sellMeanPending = false;
        private bool _buyLowerPending = false;
        private bool _sellLowerPending = false;

        private ATR _ATR;
        private DonchianChannel _donchianChannel;

        // Tracking for 3-price stability check
        private double _lastUpperLevel = 0;
        private double _lastMeanLevel = 0;
        private double _lastLowerLevel = 0;
        private int _upperStableCount = 0;
        private int _meanStableCount = 0;
        private int _lowerStableCount = 0;

        // Stable levels (only update when 3 same prices in a row)
        private double _stableUpperLevel = 0;
        private double _stableMeanLevel = 0;
        private double _stableLowerLevel = 0;

        // Order references
        private Order _buyUpperOrder = null;
        private Order _sellUpperOrder = null;
        private Order _buyMeanOrder = null;
        private Order _sellMeanOrder = null;
        private Order _buyLowerOrder = null;
        private Order _sellLowerOrder = null;

        // Position tracking
        private string _ocoId = string.Empty;
        private bool _hasActivePosition = false;

        // Per-bar guards to avoid duplicate entries
        private int _lastTradeBar = -1;

        private double _eps; // price epsilon from TickSize


        public const string GROUPNAME = "1. DonchianStrategy";

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Donchian Period", Description = "Period for Donchian Channel", Order = 1, GroupName = GROUPNAME)]
        public int DonchianPeriod { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "ATR Period", Description = "Period for ATR indicator", Order = 2, GroupName = GROUPNAME)]
        public int ATRPeriod { get; set; }

        [Range(0.1, 10.0), NinjaScriptProperty]
        [Display(Name = "Target Multiplier", Description = "ATR multiplier for profit target", Order = 3, GroupName = GROUPNAME)]
        public double TargetMultiplier { get; set; }

        [Range(0.1, 10.0), NinjaScriptProperty]
        [Display(Name = "Stop Multiplier", Description = "ATR multiplier for stop loss", Order = 4, GroupName = GROUPNAME)]
        public double StopMultiplier { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Quantity", Description = "Number of contracts/shares to trade", Order = 5, GroupName = GROUPNAME)]
        public int Quantity { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Donchian Strategy - 6 entry types organized by level (Upper/Mean/Lower)";
                Name = "_DonchianStrategy";
                Calculate = Calculate.OnEachTick;

                BarsRequiredToTrade = 14;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                IsInstantiatedOnEachOptimizationIteration = false;
                IsUnmanaged = true; // Required for unmanaged orders

                DonchianPeriod = 14;
                ATRPeriod = 9;
                TargetMultiplier = 1.3;
                StopMultiplier = 0.7;
                Quantity = 3;
            }
            else if (State == State.DataLoaded)
            {
                _eps = Math.Max(1e-6, TickSize * 0.25);

                // Initialize indicators
                _ATR = ATR(ATRPeriod);
                _donchianChannel = DonchianChannel(DonchianPeriod);

                // Initialize stable levels
                _stableUpperLevel = 0;
                _stableMeanLevel = 0;
                _stableLowerLevel = 0;
                _upperStableCount = 0;
                _meanStableCount = 0;
                _lowerStableCount = 0;
            }
            else if (State == State.Historical)
            {
                if (ChartControl != null)
                    InitializeUIManager();
            }
            else if (State == State.Realtime)
            {
                ReadyControlPanel();
                RefreshButtonsUI();
            }
            else if (State == State.Terminated)
            {
                // Cancel any pending orders
                CancelPendingOrders();

                // Close any active position
                CloseActivePosition();

                // Remove UI control panel
                UnloadControlPanel();
            }
        }

        protected override void OnBarUpdate()
        {
            try
            {
                if (CurrentBar < BarsRequiredToTrade)
                    return;

                // Check position status in realtime
                if (State == State.Realtime && _hasActivePosition)
                    CheckPositionStatus();

                // Update stable Donchian levels (but don't auto-update orders)
                UpdateStableDonchianLevels();
            }
            catch (Exception ex)
            {
                Print($"[{Time[0]}] Unexpected error: {ex}");
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            // Track our orders by level
            if (order.Name == "BuyUpperEntry")
                _buyUpperOrder = order;
            else if (order.Name == "SellUpperEntry")
                _sellUpperOrder = order;
            else if (order.Name == "BuyMeanEntry")
                _buyMeanOrder = order;
            else if (order.Name == "SellMeanEntry")
                _sellMeanOrder = order;
            else if (order.Name == "BuyLowerEntry")
                _buyLowerOrder = order;
            else if (order.Name == "SellLowerEntry")
                _sellLowerOrder = order;

            // Handle order rejections - don't kill the strategy
            if (orderState == OrderState.Rejected)
            {
                string[] entryOrders = { "BuyUpperEntry", "SellUpperEntry", "BuyMeanEntry", "SellMeanEntry", "BuyLowerEntry", "SellLowerEntry" };
                if (Array.IndexOf(entryOrders, order.Name) >= 0)
                {
                    Print($"[Order] {order.Name} rejected: {error} - {nativeError}. Strategy continues running.");
                    ClearOrderByName(order.Name);
                    RefreshButtonsUI();
                    return;
                }
            }

            // When entry order FILLS, create profit target and stop loss
            string[] validEntries = { "BuyUpperEntry", "SellUpperEntry", "BuyMeanEntry", "SellMeanEntry", "BuyLowerEntry", "SellLowerEntry" };
            if (orderState == OrderState.Filled && Array.IndexOf(validEntries, order.Name) >= 0)
            {
                if (!_hasActivePosition && _ocoId.Length == 0)
                {
                    CreateTargetAndStop(order, averageFillPrice);
                }
            }

            // Handle order cancellations
            if (orderState == OrderState.Cancelled)
            {
                ClearOrderByName(order.Name);
            }
        }

        private void ClearOrderByName(string orderName)
        {
            switch (orderName)
            {
                case "BuyUpperEntry":
                    _buyUpperOrder = null;
                    _buyUpperPending = false;
                    break;
                case "SellUpperEntry":
                    _sellUpperOrder = null;
                    _sellUpperPending = false;
                    break;
                case "BuyMeanEntry":
                    _buyMeanOrder = null;
                    _buyMeanPending = false;
                    break;
                case "SellMeanEntry":
                    _sellMeanOrder = null;
                    _sellMeanPending = false;
                    break;
                case "BuyLowerEntry":
                    _buyLowerOrder = null;
                    _buyLowerPending = false;
                    break;
                case "SellLowerEntry":
                    _sellLowerOrder = null;
                    _sellLowerPending = false;
                    break;
            }
        }

        private void CreateTargetAndStop(Order entryOrder, double fillPrice)
        {
            // Calculate ATR-based dynamic targets
            double atr = _ATR[0];
            if (atr <= 0)
                atr = TickSize * 10; // fallback

            double roundedATR = Math.Floor(atr / TickSize) * TickSize;
            int targetTicks = (int)Math.Floor(roundedATR * TargetMultiplier / TickSize);
            int stopTicks = (int)Math.Floor(roundedATR * StopMultiplier / TickSize);

            // Ensure minimum values
            if (targetTicks < 4) targetTicks = 4;
            if (stopTicks < 2) stopTicks = 2;

            // Generate unique OCO ID for linking target and stop
            _ocoId = GetAtmStrategyUniqueId();

            bool isLong = entryOrder.OrderAction == OrderAction.Buy;

            // Calculate profit target and stop loss prices
            double profitTargetPrice = isLong ? fillPrice + (targetTicks * TickSize) : fillPrice - (targetTicks * TickSize);
            double stopLossPrice = isLong ? fillPrice - (stopTicks * TickSize) : fillPrice + (stopTicks * TickSize);

            // Submit profit target order (unmanaged, movable limit order in OCO group)
            SubmitOrderUnmanaged(0,
                isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                OrderType.Limit,
                Quantity,
                profitTargetPrice,
                0,
                _ocoId,
                "ProfitTarget");

            // Submit stop loss order (unmanaged, movable stop market order in OCO group)
            SubmitOrderUnmanaged(0,
                isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                OrderType.StopMarket,
                Quantity,
                0,
                stopLossPrice,
                _ocoId,
                "StopLoss");

            _hasActivePosition = true;
            _lastTradeBar = CurrentBar;

            // Cancel all opposite pending orders
            bool isBuy = entryOrder.Name.StartsWith("Buy");
            if (isBuy)
            {
                CancelAllSellOrders();
            }
            else
            {
                CancelAllBuyOrders();
            }

            Print($"[Orders] Entry @ {fillPrice:F2} | Target @ {profitTargetPrice:F2} ({targetTicks} ticks) | Stop @ {stopLossPrice:F2} ({stopTicks} ticks)");
        }

        private void UpdateStableDonchianLevels()
        {
            double currentUpper = _donchianChannel.Upper[0];
            double currentMean = _donchianChannel[0]; // Middle band
            double currentLower = _donchianChannel.Lower[0];

            // Debug: Print current levels every bar for verification
            if (State == State.Realtime && CurrentBar % 10 == 0)
            {
                Print($"[Debug] Upper: {currentUpper:F2} | Mean: {currentMean:F2} | Lower: {currentLower:F2} | Current Price: {Close[0]:F2}");
            }

            // Check if upper level is stable (same price for 3 bars in a row)
            if (Math.Abs(currentUpper - _lastUpperLevel) < _eps)
            {
                _upperStableCount++;
                if (_upperStableCount >= 3)
                {
                    // Update stable level if it changed
                    if (Math.Abs(_stableUpperLevel - currentUpper) > _eps)
                    {
                        _stableUpperLevel = currentUpper;
                        Print($"[Donchian] Upper level updated to {_stableUpperLevel:F2} (stable for {_upperStableCount} bars)");
                    }
                }
            }
            else
            {
                _upperStableCount = 1;
            }
            _lastUpperLevel = currentUpper;

            // Check if mean level is stable (same price for 3 bars in a row)
            if (Math.Abs(currentMean - _lastMeanLevel) < _eps)
            {
                _meanStableCount++;
                if (_meanStableCount >= 3)
                {
                    // Update stable level if it changed
                    if (Math.Abs(_stableMeanLevel - currentMean) > _eps)
                    {
                        _stableMeanLevel = currentMean;
                        Print($"[Donchian] Mean level updated to {_stableMeanLevel:F2} (stable for {_meanStableCount} bars)");
                    }
                }
            }
            else
            {
                _meanStableCount = 1;
            }
            _lastMeanLevel = currentMean;

            // Check if lower level is stable (same price for 3 bars in a row)
            if (Math.Abs(currentLower - _lastLowerLevel) < _eps)
            {
                _lowerStableCount++;
                if (_lowerStableCount >= 3)
                {
                    // Update stable level if it changed
                    if (Math.Abs(_stableLowerLevel - currentLower) > _eps)
                    {
                        _stableLowerLevel = currentLower;
                        Print($"[Donchian] Lower level updated to {_stableLowerLevel:F2} (stable for {_lowerStableCount} bars)");
                    }
                }
            }
            else
            {
                _lowerStableCount = 1;
            }
            _lastLowerLevel = currentLower;
        }


        // ==================== BUTTON HANDLERS (6 total) ====================

        public void HandleBuyUpperClick()
        {
            if (State != State.Realtime)
            {
                Print("[Order] Cannot place orders in historical mode.");
                return;
            }

            if (_hasActivePosition || Position.MarketPosition != MarketPosition.Flat)
            {
                Print("[Order] Position already active. Close existing position first.");
                return;
            }

            if (_stableUpperLevel <= 0)
            {
                Print("[Order] No stable upper Donchian level available yet (need 3 same prices in a row).");
                return;
            }

            CancelAllSellOrders();
            PlaceBuyUpperOrder();
        }

        public void HandleSellUpperClick()
        {
            if (State != State.Realtime)
            {
                Print("[Order] Cannot place orders in historical mode.");
                return;
            }

            if (_hasActivePosition || Position.MarketPosition != MarketPosition.Flat)
            {
                Print("[Order] Position already active. Close existing position first.");
                return;
            }

            if (_stableUpperLevel <= 0)
            {
                Print("[Order] No stable upper Donchian level available yet (need 3 same prices in a row).");
                return;
            }

            CancelAllBuyOrders();
            PlaceSellUpperOrder();
        }

        public void HandleBuyMeanClick()
        {
            if (State != State.Realtime)
            {
                Print("[Order] Cannot place orders in historical mode.");
                return;
            }

            if (_hasActivePosition || Position.MarketPosition != MarketPosition.Flat)
            {
                Print("[Order] Position already active. Close existing position first.");
                return;
            }

            if (_stableMeanLevel <= 0)
            {
                Print("[Order] No stable mean Donchian level available yet (need 3 same prices in a row).");
                return;
            }

            CancelAllSellOrders();
            PlaceBuyMeanOrder();
        }

        public void HandleSellMeanClick()
        {
            if (State != State.Realtime)
            {
                Print("[Order] Cannot place orders in historical mode.");
                return;
            }

            if (_hasActivePosition || Position.MarketPosition != MarketPosition.Flat)
            {
                Print("[Order] Position already active. Close existing position first.");
                return;
            }

            if (_stableMeanLevel <= 0)
            {
                Print("[Order] No stable mean Donchian level available yet (need 3 same prices in a row).");
                return;
            }

            CancelAllBuyOrders();
            PlaceSellMeanOrder();
        }

        public void HandleBuyLowerClick()
        {
            if (State != State.Realtime)
            {
                Print("[Order] Cannot place orders in historical mode.");
                return;
            }

            if (_hasActivePosition || Position.MarketPosition != MarketPosition.Flat)
            {
                Print("[Order] Position already active. Close existing position first.");
                return;
            }

            if (_stableLowerLevel <= 0)
            {
                Print("[Order] No stable lower Donchian level available yet (need 3 same prices in a row).");
                return;
            }

            CancelAllSellOrders();
            PlaceBuyLowerOrder();
        }

        public void HandleSellLowerClick()
        {
            if (State != State.Realtime)
            {
                Print("[Order] Cannot place orders in historical mode.");
                return;
            }

            if (_hasActivePosition || Position.MarketPosition != MarketPosition.Flat)
            {
                Print("[Order] Position already active. Close existing position first.");
                return;
            }

            if (_stableLowerLevel <= 0)
            {
                Print("[Order] No stable lower Donchian level available yet (need 3 same prices in a row).");
                return;
            }

            CancelAllBuyOrders();
            PlaceSellLowerOrder();
        }

        // ==================== CANCEL HANDLERS ====================

        public void HandleCancelBuyUpperClick()
        {
            if (State != State.Realtime) return;
            CancelBuyUpperOrder();
            Print("[Order] Buy Upper order cancelled by user.");
        }

        public void HandleCancelSellUpperClick()
        {
            if (State != State.Realtime) return;
            CancelSellUpperOrder();
            Print("[Order] Sell Upper order cancelled by user.");
        }

        public void HandleCancelBuyMeanClick()
        {
            if (State != State.Realtime) return;
            CancelBuyMeanOrder();
            Print("[Order] Buy Mean order cancelled by user.");
        }

        public void HandleCancelSellMeanClick()
        {
            if (State != State.Realtime) return;
            CancelSellMeanOrder();
            Print("[Order] Sell Mean order cancelled by user.");
        }

        public void HandleCancelBuyLowerClick()
        {
            if (State != State.Realtime) return;
            CancelBuyLowerOrder();
            Print("[Order] Buy Lower order cancelled by user.");
        }

        public void HandleCancelSellLowerClick()
        {
            if (State != State.Realtime) return;
            CancelSellLowerOrder();
            Print("[Order] Sell Lower order cancelled by user.");
        }

        // ==================== ORDER PLACEMENT METHODS ====================

        private void PlaceBuyUpperOrder()
        {
            double currentPrice = Close[0];
            if (_stableUpperLevel <= currentPrice)
            {
                Print($"[Order] Cannot place BUY UPPER (Stop) at {_stableUpperLevel:F2} - must be above current price {currentPrice:F2}");
                return;
            }

            if (_buyUpperPending && _buyUpperOrder != null && Math.Abs(_buyUpperOrder.StopPrice - _stableUpperLevel) < _eps)
            {
                Print($"[Order] Buy Upper order already at {_stableUpperLevel:F2}");
                return;
            }

            if (_buyUpperPending) CancelBuyUpperOrder();

            _buyUpperPending = true;
            _buyUpperOrder = SubmitOrderUnmanaged(0, OrderAction.Buy, OrderType.StopMarket, Quantity, 0, _stableUpperLevel, "", "BuyUpperEntry");
            Print($"[Order] BUY UPPER (Stop) placed at {_stableUpperLevel:F2} - Qty: {Quantity}");
        }

        private void PlaceSellUpperOrder()
        {
            double currentPrice = Close[0];
            if (_stableUpperLevel < currentPrice)
            {
                Print($"[Order] Cannot place SELL UPPER (Limit) at {_stableUpperLevel:F2} - must be above current price {currentPrice:F2}");
                return;
            }

            if (_sellUpperPending && _sellUpperOrder != null && Math.Abs(_sellUpperOrder.LimitPrice - _stableUpperLevel) < _eps)
            {
                Print($"[Order] Sell Upper order already at {_stableUpperLevel:F2}");
                return;
            }

            if (_sellUpperPending) CancelSellUpperOrder();

            _sellUpperPending = true;
            _sellUpperOrder = SubmitOrderUnmanaged(0, OrderAction.SellShort, OrderType.Limit, Quantity, _stableUpperLevel, 0, "", "SellUpperEntry");
            Print($"[Order] SELL UPPER (Limit) placed at {_stableUpperLevel:F2} - Qty: {Quantity}");
        }

        private void PlaceBuyMeanOrder()
        {
            double currentPrice = Close[0];

            // Smart logic:
            // If price is ABOVE mean, use LIMIT (wait for pullback)
            // If price is BELOW mean, use STOP (breakout above)

            if (currentPrice > _stableMeanLevel)
            {
                // Price above mean - use limit order (buy on pullback)
                if (_buyMeanPending && _buyMeanOrder != null && Math.Abs(_buyMeanOrder.LimitPrice - _stableMeanLevel) < _eps)
                {
                    Print($"[Order] Buy Mean (Limit) order already at {_stableMeanLevel:F2}");
                    return;
                }

                if (_buyMeanPending) CancelBuyMeanOrder();

                _buyMeanPending = true;
                _buyMeanOrder = SubmitOrderUnmanaged(0, OrderAction.Buy, OrderType.Limit, Quantity, _stableMeanLevel, 0, "", "BuyMeanEntry");
                Print($"[Order] BUY MEAN (Limit - pullback) placed at {_stableMeanLevel:F2} - Qty: {Quantity}");
            }
            else
            {
                // Price below mean - use stop order (buy on breakout above)
                if (_buyMeanPending && _buyMeanOrder != null && Math.Abs(_buyMeanOrder.StopPrice - _stableMeanLevel) < _eps)
                {
                    Print($"[Order] Buy Mean (Stop) order already at {_stableMeanLevel:F2}");
                    return;
                }

                if (_buyMeanPending) CancelBuyMeanOrder();

                _buyMeanPending = true;
                _buyMeanOrder = SubmitOrderUnmanaged(0, OrderAction.Buy, OrderType.StopMarket, Quantity, 0, _stableMeanLevel, "", "BuyMeanEntry");
                Print($"[Order] BUY MEAN (Stop - breakout) placed at {_stableMeanLevel:F2} - Qty: {Quantity}");
            }
        }

        private void PlaceSellMeanOrder()
        {
            double currentPrice = Close[0];

            // Smart logic:
            // If price is BELOW mean, use LIMIT (wait for rally to mean)
            // If price is ABOVE mean, use STOP (breakdown below mean)

            if (currentPrice < _stableMeanLevel)
            {
                // Price below mean - use limit order (sell on rally)
                if (_sellMeanPending && _sellMeanOrder != null && Math.Abs(_sellMeanOrder.LimitPrice - _stableMeanLevel) < _eps)
                {
                    Print($"[Order] Sell Mean (Limit) order already at {_stableMeanLevel:F2}");
                    return;
                }

                if (_sellMeanPending) CancelSellMeanOrder();

                _sellMeanPending = true;
                _sellMeanOrder = SubmitOrderUnmanaged(0, OrderAction.SellShort, OrderType.Limit, Quantity, _stableMeanLevel, 0, "", "SellMeanEntry");
                Print($"[Order] SELL MEAN (Limit - rally) placed at {_stableMeanLevel:F2} - Qty: {Quantity}");
            }
            else
            {
                // Price above mean - use stop order (sell on breakdown below)
                if (_sellMeanPending && _sellMeanOrder != null && Math.Abs(_sellMeanOrder.StopPrice - _stableMeanLevel) < _eps)
                {
                    Print($"[Order] Sell Mean (Stop) order already at {_stableMeanLevel:F2}");
                    return;
                }

                if (_sellMeanPending) CancelSellMeanOrder();

                _sellMeanPending = true;
                _sellMeanOrder = SubmitOrderUnmanaged(0, OrderAction.SellShort, OrderType.StopMarket, Quantity, 0, _stableMeanLevel, "", "SellMeanEntry");
                Print($"[Order] SELL MEAN (Stop - breakdown) placed at {_stableMeanLevel:F2} - Qty: {Quantity}");
            }
        }

        private void PlaceBuyLowerOrder()
        {
            double currentPrice = Close[0];
            if (_stableLowerLevel > currentPrice)
            {
                Print($"[Order] Cannot place BUY LOWER (Limit) at {_stableLowerLevel:F2} - must be below current price {currentPrice:F2}");
                return;
            }

            if (_buyLowerPending && _buyLowerOrder != null && Math.Abs(_buyLowerOrder.LimitPrice - _stableLowerLevel) < _eps)
            {
                Print($"[Order] Buy Lower order already at {_stableLowerLevel:F2}");
                return;
            }

            if (_buyLowerPending) CancelBuyLowerOrder();

            _buyLowerPending = true;
            _buyLowerOrder = SubmitOrderUnmanaged(0, OrderAction.Buy, OrderType.Limit, Quantity, _stableLowerLevel, 0, "", "BuyLowerEntry");
            Print($"[Order] BUY LOWER (Limit) placed at {_stableLowerLevel:F2} - Qty: {Quantity}");
        }

        private void PlaceSellLowerOrder()
        {
            double currentPrice = Close[0];
            if (_stableLowerLevel > currentPrice)
            {
                Print($"[Order] Cannot place SELL LOWER (Stop) at {_stableLowerLevel:F2} - must be below current price {currentPrice:F2}");
                return;
            }

            if (_sellLowerPending && _sellLowerOrder != null && Math.Abs(_sellLowerOrder.StopPrice - _stableLowerLevel) < _eps)
            {
                Print($"[Order] Sell Lower order already at {_stableLowerLevel:F2}");
                return;
            }

            if (_sellLowerPending) CancelSellLowerOrder();

            _sellLowerPending = true;
            _sellLowerOrder = SubmitOrderUnmanaged(0, OrderAction.SellShort, OrderType.StopMarket, Quantity, 0, _stableLowerLevel, "", "SellLowerEntry");
            Print($"[Order] SELL LOWER (Stop) placed at {_stableLowerLevel:F2} - Qty: {Quantity}");
        }

        // ==================== CANCEL METHODS ====================

        private void CancelBuyUpperOrder()
        {
            if (_buyUpperOrder != null)
            {
                if (_buyUpperOrder.OrderState == OrderState.Working ||
                    _buyUpperOrder.OrderState == OrderState.Accepted ||
                    _buyUpperOrder.OrderState == OrderState.Submitted)
                {
                    CancelOrder(_buyUpperOrder);
                }
            }
            _buyUpperOrder = null;
            _buyUpperPending = false;
        }

        private void CancelSellUpperOrder()
        {
            if (_sellUpperOrder != null)
            {
                if (_sellUpperOrder.OrderState == OrderState.Working ||
                    _sellUpperOrder.OrderState == OrderState.Accepted ||
                    _sellUpperOrder.OrderState == OrderState.Submitted)
                {
                    CancelOrder(_sellUpperOrder);
                }
            }
            _sellUpperOrder = null;
            _sellUpperPending = false;
        }

        private void CancelBuyMeanOrder()
        {
            if (_buyMeanOrder != null)
            {
                if (_buyMeanOrder.OrderState == OrderState.Working ||
                    _buyMeanOrder.OrderState == OrderState.Accepted ||
                    _buyMeanOrder.OrderState == OrderState.Submitted)
                {
                    CancelOrder(_buyMeanOrder);
                }
            }
            _buyMeanOrder = null;
            _buyMeanPending = false;
        }

        private void CancelSellMeanOrder()
        {
            if (_sellMeanOrder != null)
            {
                if (_sellMeanOrder.OrderState == OrderState.Working ||
                    _sellMeanOrder.OrderState == OrderState.Accepted ||
                    _sellMeanOrder.OrderState == OrderState.Submitted)
                {
                    CancelOrder(_sellMeanOrder);
                }
            }
            _sellMeanOrder = null;
            _sellMeanPending = false;
        }

        private void CancelBuyLowerOrder()
        {
            if (_buyLowerOrder != null)
            {
                if (_buyLowerOrder.OrderState == OrderState.Working ||
                    _buyLowerOrder.OrderState == OrderState.Accepted ||
                    _buyLowerOrder.OrderState == OrderState.Submitted)
                {
                    CancelOrder(_buyLowerOrder);
                }
            }
            _buyLowerOrder = null;
            _buyLowerPending = false;
        }

        private void CancelSellLowerOrder()
        {
            if (_sellLowerOrder != null)
            {
                if (_sellLowerOrder.OrderState == OrderState.Working ||
                    _sellLowerOrder.OrderState == OrderState.Accepted ||
                    _sellLowerOrder.OrderState == OrderState.Submitted)
                {
                    CancelOrder(_sellLowerOrder);
                }
            }
            _sellLowerOrder = null;
            _sellLowerPending = false;
        }

        private void CancelAllBuyOrders()
        {
            CancelBuyUpperOrder();
            CancelBuyMeanOrder();
            CancelBuyLowerOrder();
        }

        private void CancelAllSellOrders()
        {
            CancelSellUpperOrder();
            CancelSellMeanOrder();
            CancelSellLowerOrder();
        }

        private void CancelPendingOrders()
        {
            CancelAllBuyOrders();
            CancelAllSellOrders();
        }

        private void CheckPositionStatus()
        {
            // Check if position is flat (target or stop was hit)
            if (_hasActivePosition && Position.MarketPosition == MarketPosition.Flat)
            {
                ResetPositionState();
            }
        }

        private void ResetPositionState()
        {
            Print($"[Position] Exited. Resetting (OCO id={_ocoId}).");
            _ocoId = string.Empty;
            _hasActivePosition = false;

            // Reset all order flags
            _buyUpperPending = false;
            _sellUpperPending = false;
            _buyMeanPending = false;
            _sellMeanPending = false;
            _buyLowerPending = false;
            _sellLowerPending = false;
            _buyUpperOrder = null;
            _sellUpperOrder = null;
            _buyMeanOrder = null;
            _sellMeanOrder = null;
            _buyLowerOrder = null;
            _sellLowerOrder = null;

            RefreshButtonsUI();
        }

        private void CloseActivePosition()
        {
            if (_hasActivePosition && State == State.Realtime)
            {
                try
                {
                    // Close position with market order
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Market, Position.Quantity, 0, 0, "", "ExitLong");
                        Print("[Position] Closing long position with market order");
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.Market, Position.Quantity, 0, 0, "", "ExitShort");
                        Print("[Position] Closing short position with market order");
                    }
                }
                catch (Exception ex)
                {
                    Print($"[Position] Error closing: {ex.Message}");
                }
                ResetPositionState();
            }

            // Also cancel any pending orders
            CancelPendingOrders();
        }
    }
}
