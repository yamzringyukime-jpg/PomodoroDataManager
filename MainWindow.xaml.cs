using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using PomodoroDataManager.Data;
using PomodoroDataManager.Models;
using PomodoroDataManager.Services;
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

        // 現在監視中のフォルダパス
        private string _watchedFolderPath = string.Empty;

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

                // サービスからのイベント（新しいデータが来た時、エラーが起きた時）を登録
                _syncService.OnImported += count =>
                {
                    // 別スレッドから呼ばれることがあるため、Dispatcherを通して画面を更新
                    Dispatcher.Invoke(() =>
                    {
                        ShowStatus($"✅ {count} 件の新しい記録を自動でインポートしました。");
                        RefreshDataGrid();
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

                // 起動時にデータを読み込んで表示
                RefreshDataGrid();
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

            // フォルダ内の全CSVファイルに対して手動同期を実行
            foreach (var csvFile in csvFiles)
            {
                var (inserted, updated) = _syncService.ManualSyncFile(csvFile);
                totalInserted += inserted;
                totalUpdated += updated;
            }

            RefreshDataGrid();
            ShowStatus($"✅ 手動同期完了 — 新規: {totalInserted} 件 / 更新: {totalUpdated} 件");
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
                Mode = ((System.Windows.Controls.ComboBoxItem)ModeFilterComboBox.SelectedItem)?.Content?.ToString()
            };
            if (criteria.Mode == "全て") criteria.Mode = null;

            var records = _repository.Search(criteria);
            HistoryDataGrid.ItemsSource = records;
            TotalCountText.Text = $"該当 {records.Count} 件";
        }

        /// <summary>
        /// 検索条件が変更された時の共通イベント
        /// </summary>
        private void SearchCondition_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => RefreshDataGrid();
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
    }
}