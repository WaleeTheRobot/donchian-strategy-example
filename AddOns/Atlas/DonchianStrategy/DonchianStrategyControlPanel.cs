using NinjaTrader.Gui.Chart;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class DonchianStrategy : Strategy
    {
        private ChartTab _chartTab;
        private Chart _chartWindow;
        private Grid _chartTraderGrid, _mainGrid;
        private bool _panelActive;
        private TabItem _tabItem;
        private TextBlock _strategyLabel;

        // 6 main buttons
        private Button _buyUpperButton;
        private Button _sellUpperButton;
        private Button _buyMeanButton;
        private Button _sellMeanButton;
        private Button _buyLowerButton;
        private Button _sellLowerButton;

        // 6 cancel buttons
        private Button _cancelBuyUpperButton;
        private Button _cancelSellUpperButton;
        private Button _cancelBuyMeanButton;
        private Button _cancelSellMeanButton;
        private Button _cancelBuyLowerButton;
        private Button _cancelSellLowerButton;

        public void InitializeUIManager()
        {
            LoadControlPanel();
        }

        private void LoadControlPanel()
        {
            ChartControl?.Dispatcher.InvokeAsync(CreateWPFControls);
        }

        private void UnloadControlPanel()
        {
            ChartControl?.Dispatcher.InvokeAsync(DisposeWPFControls);
        }

        private void ReadyControlPanel()
        {
            ChartControl?.Dispatcher.InvokeAsync(() => UpdateControlPanelLabel("Donchian Strategy"));
        }

        private void UpdateControlPanelLabel(string text)
        {
            if (_strategyLabel == null)
                return;

            if (_strategyLabel.Dispatcher.CheckAccess())
                _strategyLabel.Text = text;
            else
                _strategyLabel.Dispatcher.Invoke(() => _strategyLabel.Text = text);
        }

        private void RefreshButtonsUI()
        {
            if (_buyUpperButton == null || _sellUpperButton == null || _buyMeanButton == null ||
                _sellMeanButton == null || _buyLowerButton == null || _sellLowerButton == null)
                return;

            void UpdateButtons()
            {
                bool hasPosition = _hasActivePosition;

                // ========== BUY UPPER ==========
                if (_buyUpperPending)
                {
                    _buyUpperButton.Content = "UPDATE UPPER";
                    _buyUpperButton.Background = Brushes.LimeGreen;
                    _cancelBuyUpperButton.Visibility = Visibility.Visible;
                }
                else
                {
                    _buyUpperButton.Content = "BUY UPPER";
                    _buyUpperButton.Background = Brushes.ForestGreen;
                    _cancelBuyUpperButton.Visibility = Visibility.Collapsed;
                }

                // ========== SELL UPPER ==========
                if (_sellUpperPending)
                {
                    _sellUpperButton.Content = "UPDATE UPPER";
                    _sellUpperButton.Background = Brushes.OrangeRed;
                    _cancelSellUpperButton.Visibility = Visibility.Visible;
                }
                else
                {
                    _sellUpperButton.Content = "SELL UPPER";
                    _sellUpperButton.Background = Brushes.Crimson;
                    _cancelSellUpperButton.Visibility = Visibility.Collapsed;
                }

                // ========== BUY MEAN ==========
                if (_buyMeanPending)
                {
                    _buyMeanButton.Content = "UPDATE MEAN";
                    _buyMeanButton.Background = Brushes.LightSeaGreen;
                    _cancelBuyMeanButton.Visibility = Visibility.Visible;
                }
                else
                {
                    _buyMeanButton.Content = "BUY MEAN";
                    _buyMeanButton.Background = Brushes.MediumSeaGreen;
                    _cancelBuyMeanButton.Visibility = Visibility.Collapsed;
                }

                // ========== SELL MEAN ==========
                if (_sellMeanPending)
                {
                    _sellMeanButton.Content = "UPDATE MEAN";
                    _sellMeanButton.Background = Brushes.LightCoral;
                    _cancelSellMeanButton.Visibility = Visibility.Visible;
                }
                else
                {
                    _sellMeanButton.Content = "SELL MEAN";
                    _sellMeanButton.Background = Brushes.Coral;
                    _cancelSellMeanButton.Visibility = Visibility.Collapsed;
                }

                // ========== BUY LOWER ==========
                if (_buyLowerPending)
                {
                    _buyLowerButton.Content = "UPDATE LOWER";
                    _buyLowerButton.Background = Brushes.MediumSeaGreen;
                    _cancelBuyLowerButton.Visibility = Visibility.Visible;
                }
                else
                {
                    _buyLowerButton.Content = "BUY LOWER";
                    _buyLowerButton.Background = Brushes.DarkSeaGreen;
                    _cancelBuyLowerButton.Visibility = Visibility.Collapsed;
                }

                // ========== SELL LOWER ==========
                if (_sellLowerPending)
                {
                    _sellLowerButton.Content = "UPDATE LOWER";
                    _sellLowerButton.Background = Brushes.Tomato;
                    _cancelSellLowerButton.Visibility = Visibility.Visible;
                }
                else
                {
                    _sellLowerButton.Content = "SELL LOWER";
                    _sellLowerButton.Background = Brushes.IndianRed;
                    _cancelSellLowerButton.Visibility = Visibility.Collapsed;
                }

                // Disable all buttons if position is active
                _buyUpperButton.IsEnabled = !hasPosition;
                _sellUpperButton.IsEnabled = !hasPosition;
                _buyMeanButton.IsEnabled = !hasPosition;
                _sellMeanButton.IsEnabled = !hasPosition;
                _buyLowerButton.IsEnabled = !hasPosition;
                _sellLowerButton.IsEnabled = !hasPosition;
                _cancelBuyUpperButton.IsEnabled = !hasPosition;
                _cancelSellUpperButton.IsEnabled = !hasPosition;
                _cancelBuyMeanButton.IsEnabled = !hasPosition;
                _cancelSellMeanButton.IsEnabled = !hasPosition;
                _cancelBuyLowerButton.IsEnabled = !hasPosition;
                _cancelSellLowerButton.IsEnabled = !hasPosition;

                double opacity = hasPosition ? 0.5 : 1.0;
                _buyUpperButton.Opacity = opacity;
                _sellUpperButton.Opacity = opacity;
                _buyMeanButton.Opacity = opacity;
                _sellMeanButton.Opacity = opacity;
                _buyLowerButton.Opacity = opacity;
                _sellLowerButton.Opacity = opacity;
                _cancelBuyUpperButton.Opacity = opacity;
                _cancelSellUpperButton.Opacity = opacity;
                _cancelBuyMeanButton.Opacity = opacity;
                _cancelSellMeanButton.Opacity = opacity;
                _cancelBuyLowerButton.Opacity = opacity;
                _cancelSellLowerButton.Opacity = opacity;
            }

            if (_buyUpperButton.Dispatcher.CheckAccess())
                UpdateButtons();
            else
                _buyUpperButton.Dispatcher.Invoke(UpdateButtons);
        }

        private void CreateWPFControls()
        {
            _chartWindow = Window.GetWindow(ChartControl?.Parent) as Gui.Chart.Chart;
            if (_chartWindow == null)
                return;

            var chartTrader = _chartWindow.FindFirst("ChartWindowChartTraderControl") as ChartTrader;
            _chartTraderGrid = chartTrader?.Content as Grid;
            if (_chartTraderGrid == null)
                return;

            _mainGrid = new Grid
            {
                Margin = new Thickness(0, 50, 0, 0),
                Background = Brushes.Transparent
            };

            // 10 rows: Label + 3 sections (Upper/Mean/Lower) with 2 buttons each + 2 separators
            _mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Label
            _mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Buy Upper
            _mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Sell Upper
            _mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: Separator 1
            _mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4: Buy Mean
            _mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5: Sell Mean
            _mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 6: Separator 2
            _mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 7: Buy Lower
            _mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 8: Sell Lower

            // Strategy label
            _strategyLabel = new TextBlock
            {
                FontFamily = ChartControl.Properties.LabelFont.Family,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(4),
                Text = "Loading..."
            };
            Grid.SetRow(_strategyLabel, 0);
            _mainGrid.Children.Add(_strategyLabel);

            // ========== UPPER LEVEL SECTION ==========
            // ROW 1: BUY UPPER
            var buyUpperRowGrid = CreateButtonRow(1);
            _buyUpperButton = CreateMainButton("BUY UPPER", Brushes.ForestGreen, BuyUpperButton_Click);
            _cancelBuyUpperButton = CreateCancelButton(CancelBuyUpperButton_Click);
            buyUpperRowGrid.Children.Add(_buyUpperButton);
            buyUpperRowGrid.Children.Add(_cancelBuyUpperButton);
            _mainGrid.Children.Add(buyUpperRowGrid);

            // ROW 2: SELL UPPER
            var sellUpperRowGrid = CreateButtonRow(2);
            _sellUpperButton = CreateMainButton("SELL UPPER", Brushes.Crimson, SellUpperButton_Click);
            _cancelSellUpperButton = CreateCancelButton(CancelSellUpperButton_Click);
            sellUpperRowGrid.Children.Add(_sellUpperButton);
            sellUpperRowGrid.Children.Add(_cancelSellUpperButton);
            _mainGrid.Children.Add(sellUpperRowGrid);

            // ROW 3: SEPARATOR 1
            var separator1 = new System.Windows.Shapes.Rectangle
            {
                Height = 2,
                Fill = Brushes.Gray,
                Margin = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetRow(separator1, 3);
            _mainGrid.Children.Add(separator1);

            // ========== MEAN LEVEL SECTION ==========
            // ROW 4: BUY MEAN
            var buyMeanRowGrid = CreateButtonRow(4);
            _buyMeanButton = CreateMainButton("BUY MEAN", Brushes.MediumSeaGreen, BuyMeanButton_Click);
            _cancelBuyMeanButton = CreateCancelButton(CancelBuyMeanButton_Click);
            buyMeanRowGrid.Children.Add(_buyMeanButton);
            buyMeanRowGrid.Children.Add(_cancelBuyMeanButton);
            _mainGrid.Children.Add(buyMeanRowGrid);

            // ROW 5: SELL MEAN
            var sellMeanRowGrid = CreateButtonRow(5);
            _sellMeanButton = CreateMainButton("SELL MEAN", Brushes.Coral, SellMeanButton_Click);
            _cancelSellMeanButton = CreateCancelButton(CancelSellMeanButton_Click);
            sellMeanRowGrid.Children.Add(_sellMeanButton);
            sellMeanRowGrid.Children.Add(_cancelSellMeanButton);
            _mainGrid.Children.Add(sellMeanRowGrid);

            // ROW 6: SEPARATOR 2
            var separator2 = new System.Windows.Shapes.Rectangle
            {
                Height = 2,
                Fill = Brushes.Gray,
                Margin = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetRow(separator2, 6);
            _mainGrid.Children.Add(separator2);

            // ========== LOWER LEVEL SECTION ==========
            // ROW 7: BUY LOWER
            var buyLowerRowGrid = CreateButtonRow(7);
            _buyLowerButton = CreateMainButton("BUY LOWER", Brushes.DarkSeaGreen, BuyLowerButton_Click);
            _cancelBuyLowerButton = CreateCancelButton(CancelBuyLowerButton_Click);
            buyLowerRowGrid.Children.Add(_buyLowerButton);
            buyLowerRowGrid.Children.Add(_cancelBuyLowerButton);
            _mainGrid.Children.Add(buyLowerRowGrid);

            // ROW 8: SELL LOWER
            var sellLowerRowGrid = CreateButtonRow(8);
            _sellLowerButton = CreateMainButton("SELL LOWER", Brushes.IndianRed, SellLowerButton_Click);
            _cancelSellLowerButton = CreateCancelButton(CancelSellLowerButton_Click);
            sellLowerRowGrid.Children.Add(_sellLowerButton);
            sellLowerRowGrid.Children.Add(_cancelSellLowerButton);
            _mainGrid.Children.Add(sellLowerRowGrid);

            RefreshButtonsUI();

            if (TabSelected())
                InsertWPFControls();

            _chartWindow.MainTabControl.SelectionChanged += TabChangedHandler;
        }

        private Grid CreateButtonRow(int rowIndex)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(grid, rowIndex);
            return grid;
        }

        private Button CreateMainButton(string content, Brush background, RoutedEventHandler clickHandler)
        {
            var button = new Button
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(6),
                Background = background,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
            };
            button.Click += clickHandler;
            Grid.SetColumn(button, 0);
            return button;
        }

        private Button CreateCancelButton(RoutedEventHandler clickHandler)
        {
            var button = new Button
            {
                Content = "X",
                Width = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(4),
                Background = Brushes.Gray,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Visibility = Visibility.Collapsed
            };
            button.Click += clickHandler;
            Grid.SetColumn(button, 1);
            return button;
        }

        // ========== BUTTON CLICK HANDLERS ==========

        private void BuyUpperButton_Click(object sender, RoutedEventArgs e)
        {
            HandleBuyUpperClick();
            RefreshButtonsUI();
        }

        private void SellUpperButton_Click(object sender, RoutedEventArgs e)
        {
            HandleSellUpperClick();
            RefreshButtonsUI();
        }

        private void BuyMeanButton_Click(object sender, RoutedEventArgs e)
        {
            HandleBuyMeanClick();
            RefreshButtonsUI();
        }

        private void SellMeanButton_Click(object sender, RoutedEventArgs e)
        {
            HandleSellMeanClick();
            RefreshButtonsUI();
        }

        private void BuyLowerButton_Click(object sender, RoutedEventArgs e)
        {
            HandleBuyLowerClick();
            RefreshButtonsUI();
        }

        private void SellLowerButton_Click(object sender, RoutedEventArgs e)
        {
            HandleSellLowerClick();
            RefreshButtonsUI();
        }

        private void CancelBuyUpperButton_Click(object sender, RoutedEventArgs e)
        {
            HandleCancelBuyUpperClick();
            RefreshButtonsUI();
        }

        private void CancelSellUpperButton_Click(object sender, RoutedEventArgs e)
        {
            HandleCancelSellUpperClick();
            RefreshButtonsUI();
        }

        private void CancelBuyMeanButton_Click(object sender, RoutedEventArgs e)
        {
            HandleCancelBuyMeanClick();
            RefreshButtonsUI();
        }

        private void CancelSellMeanButton_Click(object sender, RoutedEventArgs e)
        {
            HandleCancelSellMeanClick();
            RefreshButtonsUI();
        }

        private void CancelBuyLowerButton_Click(object sender, RoutedEventArgs e)
        {
            HandleCancelBuyLowerClick();
            RefreshButtonsUI();
        }

        private void CancelSellLowerButton_Click(object sender, RoutedEventArgs e)
        {
            HandleCancelSellLowerClick();
            RefreshButtonsUI();
        }

        private void DisposeWPFControls()
        {
            if (_chartWindow != null)
                _chartWindow.MainTabControl.SelectionChanged -= TabChangedHandler;

            RemoveWPFControls();

            // Unsubscribe from all button events
            if (_buyUpperButton != null) _buyUpperButton.Click -= BuyUpperButton_Click;
            if (_sellUpperButton != null) _sellUpperButton.Click -= SellUpperButton_Click;
            if (_buyMeanButton != null) _buyMeanButton.Click -= BuyMeanButton_Click;
            if (_sellMeanButton != null) _sellMeanButton.Click -= SellMeanButton_Click;
            if (_buyLowerButton != null) _buyLowerButton.Click -= BuyLowerButton_Click;
            if (_sellLowerButton != null) _sellLowerButton.Click -= SellLowerButton_Click;
            if (_cancelBuyUpperButton != null) _cancelBuyUpperButton.Click -= CancelBuyUpperButton_Click;
            if (_cancelSellUpperButton != null) _cancelSellUpperButton.Click -= CancelSellUpperButton_Click;
            if (_cancelBuyMeanButton != null) _cancelBuyMeanButton.Click -= CancelBuyMeanButton_Click;
            if (_cancelSellMeanButton != null) _cancelSellMeanButton.Click -= CancelSellMeanButton_Click;
            if (_cancelBuyLowerButton != null) _cancelBuyLowerButton.Click -= CancelBuyLowerButton_Click;
            if (_cancelSellLowerButton != null) _cancelSellLowerButton.Click -= CancelSellLowerButton_Click;

            // Null out all references
            _strategyLabel = null;
            _buyUpperButton = null;
            _sellUpperButton = null;
            _buyMeanButton = null;
            _sellMeanButton = null;
            _buyLowerButton = null;
            _sellLowerButton = null;
            _cancelBuyUpperButton = null;
            _cancelSellUpperButton = null;
            _cancelBuyMeanButton = null;
            _cancelSellMeanButton = null;
            _cancelBuyLowerButton = null;
            _cancelSellLowerButton = null;
            _mainGrid = null;
            _chartTraderGrid = null;
            _chartWindow = null;
        }

        private void InsertWPFControls()
        {
            if (_panelActive || _mainGrid == null || _chartTraderGrid == null)
                return;

            Grid.SetRow(_mainGrid, _chartTraderGrid.RowDefinitions.Count - 1);
            _chartTraderGrid.Children.Add(_mainGrid);
            _panelActive = true;
        }

        private void RemoveWPFControls()
        {
            if (!_panelActive || _chartTraderGrid == null || _mainGrid == null)
                return;

            _chartTraderGrid.Children.Remove(_mainGrid);
            _panelActive = false;
        }

        private bool TabSelected()
        {
            if (_chartWindow?.MainTabControl?.Items == null || ChartControl == null)
                return false;

            foreach (TabItem tab in _chartWindow.MainTabControl.Items)
            {
                var ct = tab.Content as ChartTab;
                if (ct != null && ct.ChartControl == ChartControl && tab == _chartWindow.MainTabControl.SelectedItem)
                    return true;
            }
            return false;
        }

        private void TabChangedHandler(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count <= 0)
                return;

            _tabItem = e.AddedItems[0] as TabItem;
            if (_tabItem == null)
                return;

            _chartTab = _tabItem.Content as ChartTab;
            if (_chartTab == null)
                return;

            if (TabSelected())
                InsertWPFControls();
            else
                RemoveWPFControls();
        }
    }
}
