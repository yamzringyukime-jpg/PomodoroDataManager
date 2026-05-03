using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using PomodoroDataManager.Data;
using PomodoroDataManager.Models;
using PomodoroDataManager.Services;
using SkiaSharp;
using WinForms = System.Windows.Forms; // WPFとWindows Formsの名前衝突をエイリアスで回避

namespace PomodoroDataManager
{
    /// <summary>
    /// メインウインドウのコードビハインド（画面の動作を制御するクラス）。
    /// 画面の部品（ボタン等）と、サービスクラスを繋ぐ役割を担います。
    /// </summary>
    public partial class MainWindow : Window
    {
        // Accessデータベースのファイルパス（アプリと同じフォルダに保存）
        private readonly string _dbFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "pomodoro_history.accdb"
        );

        // データの出し入れ用「窓口」
        private IHistoryRepository _repository = null!;

        // CSV同期サービス
        private CsvSyncService _syncService = null!;

        // ダッシュボード集計サービス
        private DashboardService _dashboardService = null!;

        // 現在監視中のフォルダパス
        private string _watchedFolderPath = string.Empty;

        // 現在のグラフ表示期間（日数）
        private int _currentPeriodDays = 7;

        /// <summary>
        /// コンストラクタ：アプリ起動時の初期化処理
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // ウインドウが完全に表示されてから初期化
            Loaded += MainWindow_Loaded;
            // ウィンドウが閉じられる時に後片付け
            Closed += (_, _) => _syncService?.StopWatching();

            // グリッドをダブルクリックした時の処理
            HistoryDataGrid.MouseDoubleClick += HistoryDataGrid_MouseDoubleClick;
        }

        /// <summary>
        /// ウインドウが読み込まれた後の初期化処理
        /// </summary>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Repositoryの初期化（Access接続文字列を設定）
                _repository = new AccessHistoryRepository(_dbFilePath);
                _repository.Initialize();

                // CSV同期サービスの初期化
                _syncService = new CsvSyncService(_repository);

                // ダッシュボード集計サービスの初期化
                _dashboardService = new DashboardService(_repository);

                // サービスからのイベント（新しいデータが来た時、エラーが起きた時）を登録
                _syncService.OnImported += count =>
                {
                    // 別スレッドから呼ばれることがあるため、Dispatcherを通して画面を更新
                    Dispatcher.Invoke(() =>
                    {
                        ShowStatus($"✅ {count} 件の新しい記録を自動でインポートしました。");
                        RefreshDataGrid();
                        RefreshDashboard(); // ダッシュボードも更新
                    });
                };

                _syncService.OnError += msg =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ShowStatus($"⚠️ エラーが発生しました。詳細はログを確認してください。");
                        LogService.WriteLog($"[SyncServiceError] {msg}");
                        System.Windows.MessageBox.Show(msg, "エラー",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                };

                _syncService.OnImportSkipped += skippedRecords =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try { new System.Media.SoundPlayer(@"C:\Windows\Media\Windows Notify System Generic.wav").Play(); } catch { }
                        var msg = $"以下のデータは既存の記録と時間帯が重複しているため、取り込まれませんでした:\n\n" +
                                  string.Join("\n", skippedRecords.Select(r => $"・{r.TaskName} ({r.StartTime:HH:mm} - {r.EndTime:HH:mm})"));
                        
                        System.Windows.MessageBox.Show(msg, "インポート スキップ通知", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                };

                // 起動時にデータを読み込んで表示
                RefreshDataGrid();

                // ダッシュボードの初期表示
                RefreshDashboard();

                ShowStatus("起動完了。OneDriveフォルダを選択または検索を行ってください。");
            }
            catch (Exception ex)
            {
                // DB接続に失敗した場合（Accessエンジン未インストールなど）
                LogService.WriteLog($"[起動エラー] {ex}");
                System.Windows.MessageBox.Show(
                    $"データベースの初期化に失敗しました。\n\n" +
                    $"考えられる原因:\n" +
                    $"・Microsoft Access Database Engineがインストールされていない\n\n" +
                    $"詳細: {ex.Message}",
                    "起動エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 「フォルダを選択」ボタンが押された時の処理
        /// </summary>
        private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            // フォルダ選択ダイアログを表示（Windowsの標準ダイアログ）
            using var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "ポモドーロCSVが保存されているOneDriveフォルダを選択してください",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                _watchedFolderPath = dialog.SelectedPath;
                FolderPathTextBox.Text = _watchedFolderPath;

                // フォルダの監視を開始
                _syncService.StartWatching(_watchedFolderPath);

                // 緑のインジケーターを表示（監視中）
                StatusIndicator.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(72, 199, 142));
                WatchStatusText.Text = "監視中";
                WatchStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(72, 199, 142));

                // 「更新を反映する」ボタンを有効化
                ManualSyncButton.IsEnabled = true;

                ShowStatus($"監視開始: {_watchedFolderPath}");
                LogService.WriteLog($"[監視開始] {_watchedFolderPath}");
            }
        }

        /// <summary>
        /// 「更新を反映する」ボタンが押された時の処理（手動同期）
        /// </summary>
        private void ManualSyncButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_watchedFolderPath)) return;

            // フォルダ内のCSVを全件取得
            var csvFiles = Directory.GetFiles(_watchedFolderPath, "*.csv");
            if (csvFiles.Length == 0)
            {
                ShowStatus("⚠️ フォルダ内にCSVファイルが見つかりません。");
                return;
            }

            int totalInserted = 0;
            int totalUpdated = 0;
            var totalSkipped = new List<PomodoroRecord>();

            // フォルダ内の全CSVファイルに対して手動同期を実行
            foreach (var csvFile in csvFiles)
            {
                var (inserted, updated, skipped) = _syncService.ManualSyncFile(csvFile);
                totalInserted += inserted;
                totalUpdated += updated;
                totalSkipped.AddRange(skipped);
            }

            RefreshDataGrid();
            RefreshDashboard(); // ダッシュボードも更新
            ShowStatus($"✅ 手動同期完了 — 新規: {totalInserted} 件 / 更新: {totalUpdated} 件");

            // スキップされたデータがあれば通知
            if (totalSkipped.Count > 0)
            {
                try { new System.Media.SoundPlayer(@"C:\Windows\Media\Windows Notify System Generic.wav").Play(); } catch { }
                var msg = $"以下のデータは既存の記録と時間帯が重複しているため、取り込まれませんでした:\n\n" +
                          string.Join("\n", totalSkipped.Select(r => $"・{r.TaskName} ({r.StartTime:HH:mm} - {r.EndTime:HH:mm})"));
                System.Windows.MessageBox.Show(msg, "手動同期 スキップ通知", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// 検索パネルの内容に基づいてデータを再表示します。
        /// </summary>
        private void RefreshDataGrid()
        {
            if (_repository == null) return;
            
            var criteria = new SearchCriteria
            {
                DateFrom = StartDatePicker.SelectedDate,
                DateTo = EndDatePicker.SelectedDate,
                Keyword = KeywordSearchTextBox.Text,
                Mode = ((ComboBoxItem)ModeFilterComboBox.SelectedItem)?.Content?.ToString()
            };
            if (criteria.Mode == "全て") criteria.Mode = null;

            var records = _repository.Search(criteria);
            HistoryDataGrid.ItemsSource = records;
            TotalCountText.Text = $"該当 {records.Count} 件";
        }

        // ========================================
        // ダッシュボード関連メソッド
        // ========================================

        /// <summary>
        /// ダッシュボードの全UIを最新データで更新します。
        /// Focus Meter（本日の集中時間）とグラフの両方を再描画します。
        /// </summary>
        private void RefreshDashboard()
        {
            if (_dashboardService == null) return;

            try
            {
                // --- Focus Meter（本日の統計）を更新 ---
                var todaySummary = _dashboardService.GetDailySummary(DateTime.Today);

                // メインのデジタル表示（合計作業時間）
                FocusMeterText.Text = todaySummary.TotalWorkFormatted;

                // サブ計器
                SessionCountText.Text = todaySummary.WorkSessionCount.ToString();
                MaxStreakText.Text = $"{(int)todaySummary.MaxStreakMinutes} min";

                // 休憩時間
                int breakHours = (int)(todaySummary.TotalBreakMinutes / 60);
                int breakMins = (int)(todaySummary.TotalBreakMinutes % 60);
                BreakTimeText.Text = $"{breakHours}h {breakMins:D2}m";

                // --- 棒グラフ（期間別アクティビティ）を更新 ---
                RefreshChart();
            }
            catch (Exception ex)
            {
                LogService.WriteLog($"[ダッシュボード更新エラー] {ex.Message}");
            }
        }

        /// <summary>
        /// 棒グラフの内容を現在の期間設定で更新します。
        /// LiveCharts2のSeriesとX軸ラベルを再構成します。
        /// </summary>
        private void RefreshChart()
        {
            if (_dashboardService == null) return;

            // 指定期間の日別サマリーを取得
            var endDate = DateTime.Today;
            var startDate = endDate.AddDays(-(_currentPeriodDays - 1));
            var summaries = _dashboardService.GetPeriodSummary(startDate, endDate);

            // X軸のラベル（日付）を生成
            var labels = summaries.Select(s => s.Date.ToString("M/d")).ToArray();
            
            // 期間内に存在する全てのタスク名を抽出
            var taskNames = summaries.SelectMany(s => s.TaskMinutes.Keys).Distinct().ToList();
            var seriesList = new List<ISeries>();

            foreach (var taskName in taskNames)
            {
                // 各日のそのタスクの作業時間を配列にする
                var taskValues = summaries.Select(s => s.TaskMinutes.ContainsKey(taskName) ? s.TaskMinutes[taskName] : 0.0).ToArray();
                
                seriesList.Add(new StackedColumnSeries<double>
                {
                    Values = taskValues,
                    Name = taskName,
                    // タスク名から一意の色を生成
                    Fill = new SolidColorPaint(GetColorForTask(taskName)),
                    Stroke = null,
                    MaxBarWidth = _currentPeriodDays <= 7 ? 25 : 12,
                    Padding = 2
                });
            }

            // もしデータが一件もない場合でも空のシリーズをセットして表示を維持
            if (seriesList.Count == 0)
            {
                seriesList.Add(new StackedColumnSeries<double> { Values = new double[summaries.Count], Name = "なし" });
            }

            WeeklyChart.Series = seriesList.ToArray();

            // X軸の設定（日付ラベル）
            WeeklyChart.XAxes = new Axis[]
            {
                new Axis
                {
                    Labels = labels,
                    LabelsPaint = new SolidColorPaint(new SKColor(160, 174, 192)),  // #A0AEC0
                    TextSize = 10,
                    SeparatorsPaint = new SolidColorPaint(new SKColor(15, 52, 96, 80)),  // #0F3460（薄め）
                    TicksPaint = new SolidColorPaint(new SKColor(15, 52, 96, 80))
                }
            };

            // Y軸の設定（作業時間：分）
            WeeklyChart.YAxes = new Axis[]
            {
                new Axis
                {
                    LabelsPaint = new SolidColorPaint(new SKColor(160, 174, 192)),  // #A0AEC0
                    TextSize = 10,
                    SeparatorsPaint = new SolidColorPaint(new SKColor(15, 52, 96, 50)),  // 薄いグリッド線
                    MinLimit = 0,
                    Labeler = value => value.ToString("F2")
                }
            };
        }

        /// <summary>
        /// 期間切り替えボタン（7D / 30D）が押された時の処理
        /// </summary>
        private void PeriodButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int days))
            {
                _currentPeriodDays = days;

                // 選択中のボタンをアクセント色にし、非選択をインストルメント色に戻す
                // （Data-driven: ボタンのスタイルをStateから切り替える）
                Period7DaysButton.Style = days == 7
                    ? (Style)FindResource("AccentButton")
                    : (Style)FindResource("InstrumentButton");
                Period30DaysButton.Style = days == 30
                    ? (Style)FindResource("AccentButton")
                    : (Style)FindResource("InstrumentButton");

                RefreshChart();
            }
        }

        /// <summary>
        /// 検索条件が変更された時の共通イベント
        /// </summary>
        private void SearchCondition_Changed(object sender, SelectionChangedEventArgs e) => RefreshDataGrid();
        private void SearchCondition_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshDataGrid();

        /// <summary>
        /// 検索条件を完全リセット
        /// </summary>
        private void ResetSearchButton_Click(object sender, RoutedEventArgs e)
        {
            StartDatePicker.SelectedDate = null;
            EndDatePicker.SelectedDate = null;
            KeywordSearchTextBox.Text = string.Empty;
            ModeFilterComboBox.SelectedIndex = 0; // 全て
            RefreshDataGrid();
        }

        /// <summary>
        /// 「新規登録」ボタンが押された時の処理
        /// </summary>
        private void AddNewRecordButton_Click(object sender, RoutedEventArgs e)
        {
            // IDが0の空のレコードを作成
            var newRecord = new PomodoroRecord
            {
                StartTime = DateTime.Now.AddMinutes(-25),
                EndTime = DateTime.Now,
                Mode = "作業",
                TaskName = "",
                Duration = "25:00"
            };

            var editWindow = new EditRecordWindow(newRecord, _repository, _syncService);
            editWindow.Owner = this;
            if (editWindow.ShowDialog() == true)
            {
                RefreshDataGrid();
                RefreshDashboard(); // ダッシュボードも更新
                ShowStatus("✅ 新しいレコードを登録しました。");
            }
        }

        /// <summary>
        /// グリッドがダブルクリックされた時（編集画面を開く）
        /// </summary>
        private void HistoryDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (HistoryDataGrid.SelectedItem is PomodoroRecord selected)
            {
                var editWindow = new EditRecordWindow(selected, _repository, _syncService);
                editWindow.Owner = this;
                if (editWindow.ShowDialog() == true)
                {
                    RefreshDataGrid();
                    RefreshDashboard(); // ダッシュボードも更新
                    ShowStatus("✅ レコードを更新しました。");
                }
            }
        }

        /// <summary>
        /// ステータスバーのメッセージを更新します。
        /// </summary>
        private void ShowStatus(string message)
        {
            StatusMessageText.Text = message;
        }

        /// <summary>
        /// タスク名に基づいて一意の色（SKColor）を生成します。
        /// 同じタスク名には常に同じ色が割り当てられます。
        /// </summary>
        private SKColor GetColorForTask(string taskName)
        {
            if (string.IsNullOrEmpty(taskName)) return new SKColor(160, 174, 192);

            // 文字列のハッシュ値を利用して色を決定
            int hash = taskName.GetHashCode();
            byte r = (byte)((hash & 0xFF0000) >> 16);
            byte g = (byte)((hash & 0x00FF00) >> 8);
            byte b = (byte)(hash & 0x0000FF);

            // ダークテーマに映えるよう、少し明るさを調整（パステル調に寄せる）
            return new SKColor(
                (byte)((r % 150) + 80),
                (byte)((g % 150) + 80),
                (byte)((b % 150) + 80),
                220);
        }
    }
}