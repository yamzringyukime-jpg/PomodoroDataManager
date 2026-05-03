using System;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Media;
using PomodoroDataManager.Data;
using PomodoroDataManager.Models;
using PomodoroDataManager.Services;

namespace PomodoroDataManager
{
    public partial class EditRecordWindow : Window
    {
        private readonly PomodoroRecord _originalRecord;
        private readonly IHistoryRepository _repository;
        private readonly CsvSyncService _syncService;
        private bool _isInitialized = false;
        private bool _isUpdatingModeOrTask = false;

        /// <summary>「の休憩」サフィックスを除いた元のタスク名を保持する</summary>
        private string _baseTaskName = string.Empty;

        private static readonly SolidColorBrush ErrorBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE9, 0x45, 0x60));
        private static readonly SolidColorBrush SuccessBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80));

        public EditRecordWindow(PomodoroRecord record, IHistoryRepository repository, CsvSyncService syncService)
        {
            InitializeComponent();
            _originalRecord = record;
            _repository = repository;
            _syncService = syncService;

            LoadRecordData();

            // 新規作成時は削除ボタンを非表示にする
            if (_originalRecord.Id == 0)
            {
                DeleteButton.Visibility = Visibility.Collapsed;
                Title = "レコードの新規登録";
            }

            _isInitialized = true;
            CalculateDuration();
        }

        private void LoadRecordData()
        {
            // 元のタスク名から「の休憩」サフィックスを除去してベース名を取得
            string taskName = _originalRecord.TaskName;
            if (_originalRecord.Mode == "休憩" && taskName.EndsWith("の休憩"))
            {
                _baseTaskName = taskName.Substring(0, taskName.Length - "の休憩".Length);
            }
            else
            {
                _baseTaskName = taskName;
            }

            TaskNameTextBox.Text = _originalRecord.TaskName;

            // ModeComboBox の選択を正しく設定
            foreach (System.Windows.Controls.ComboBoxItem item in ModeComboBox.Items)
            {
                if (item.Content.ToString() == _originalRecord.Mode)
                {
                    ModeComboBox.SelectedItem = item;
                    break;
                }
            }

            StartDatePicker.SelectedDate = _originalRecord.StartTime.Date;
            StartTimeText.Text = _originalRecord.StartTime.ToString("HH:mm:ss");
            EndDatePicker.SelectedDate = _originalRecord.EndTime.Date;
            EndTimeText.Text = _originalRecord.EndTime.ToString("HH:mm:ss");
        }

        private void TimeChanged(object sender, EventArgs e)
        {
            if (!_isInitialized) return;
            CalculateDuration();
        }

        private void Mode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingModeOrTask) return;

            if (ModeComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            {
                _isUpdatingModeOrTask = true;
                string mode = item.Content?.ToString() ?? "";

                // 現在のタスク名からベース名を抽出（「の休憩」がついていれば除去）
                string currentTask = TaskNameTextBox.Text;
                if (currentTask.EndsWith("の休憩"))
                {
                    _baseTaskName = currentTask.Substring(0, currentTask.Length - "の休憩".Length);
                }
                else
                {
                    _baseTaskName = currentTask;
                }

                if (mode == "休憩")
                {
                    // 作業→休憩: 「○○の休憩」形式にする
                    if (!string.IsNullOrWhiteSpace(_baseTaskName))
                    {
                        TaskNameTextBox.Text = _baseTaskName + "の休憩";
                    }
                    else
                    {
                        TaskNameTextBox.Text = "休憩";
                    }
                }
                else if (mode == "作業")
                {
                    // 休憩→作業: 「の休憩」を取り除いて元のタスク名に戻す
                    TaskNameTextBox.Text = _baseTaskName;
                }

                _isUpdatingModeOrTask = false;
            }
        }

        private void TaskName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingModeOrTask) return;

            _isUpdatingModeOrTask = true;
            string taskText = TaskNameTextBox.Text.Trim();

            if (taskText.EndsWith("の休憩") || taskText == "休憩")
            {
                // 「の休憩」で終わるか「休憩」そのものならMODEを休憩に
                foreach (System.Windows.Controls.ComboBoxItem item in ModeComboBox.Items)
                {
                    if (item.Content?.ToString() == "休憩")
                    {
                        ModeComboBox.SelectedItem = item;
                        break;
                    }
                }
                // ベース名を更新
                if (taskText.EndsWith("の休憩"))
                {
                    _baseTaskName = taskText.Substring(0, taskText.Length - "の休憩".Length);
                }
            }
            else
            {
                // それ以外ならMODEを作業に
                foreach (System.Windows.Controls.ComboBoxItem item in ModeComboBox.Items)
                {
                    if (item.Content?.ToString() == "作業")
                    {
                        ModeComboBox.SelectedItem = item;
                        break;
                    }
                }
                _baseTaskName = taskText;
            }

            _isUpdatingModeOrTask = false;
        }

        private void CalculateDuration()
        {
            try
            {
                var start = GetDateTime(StartDatePicker, StartTimeText);
                var end = GetDateTime(EndDatePicker, EndTimeText);

                if (!start.HasValue || !end.HasValue)
                {
                    DurationTextBlock.Text = "--:--";
                    DurationTextBlock.Foreground = ErrorBrush;
                    SaveButton.IsEnabled = false;
                    ValidationMessage.Text = "⚠ 日付または時刻の形式が不正です (HH:mm:ss)";
                    return;
                }

                var diff = end.Value - start.Value;
                if (diff.TotalSeconds < 0)
                {
                    DurationTextBlock.Text = "--:--";
                    DurationTextBlock.Foreground = ErrorBrush;
                    SaveButton.IsEnabled = false;
                    ValidationMessage.Text = "⚠ 終了時刻が開始時刻より前になっています";
                }
                else
                {
                    string durationStr = $"{(int)diff.TotalMinutes:D2}:{diff.Seconds:D2}";
                    DurationTextBlock.Text = durationStr;
                    DurationTextBlock.Foreground = SuccessBrush;
                    SaveButton.IsEnabled = true;
                    ValidationMessage.Text = "";
                }
            }
            catch
            {
                DurationTextBlock.Text = "--:--";
                DurationTextBlock.Foreground = ErrorBrush;
                SaveButton.IsEnabled = false;
                ValidationMessage.Text = "⚠ 日付または時刻の形式が不正です";
            }
        }

        private DateTime? GetDateTime(System.Windows.Controls.DatePicker picker, System.Windows.Controls.TextBox timeBox)
        {
            if (!picker.SelectedDate.HasValue) return null;
            if (!TimeSpan.TryParse(timeBox.Text, out var time)) return null;
            return picker.SelectedDate.Value.Date + time;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var start = GetDateTime(StartDatePicker, StartTimeText);
            var end = GetDateTime(EndDatePicker, EndTimeText);

            if (!start.HasValue || !end.HasValue)
            {
                System.Windows.MessageBox.Show("時刻の形式が正しくありません (HH:mm:ss)", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 選択中のMODE文字列を取得
            string selectedMode = "作業";
            if (ModeComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
            {
                selectedMode = selectedItem.Content?.ToString() ?? "作業";
            }

            // 新しいレコード情報を作成
            var newRecord = new PomodoroRecord
            {
                Id = _originalRecord.Id,
                TaskName = TaskNameTextBox.Text,
                Mode = selectedMode,
                StartTime = start.Value,
                EndTime = end.Value,
                Duration = DurationTextBlock.Text,
                SourceFilePath = _originalRecord.SourceFilePath
            };
            // 重複チェック
            var existingRecords = _repository.Search(new SearchCriteria());
            if (_syncService.IsTimeOverlapping(newRecord, existingRecords))
            {
                try { new System.Media.SoundPlayer(@"C:\Windows\Media\Windows Notify System Generic.wav").Play(); } catch { }
                System.Windows.MessageBox.Show(
                    "指定された時間帯は、既存の記録と重複しています。\n時間帯を調整してください。",
                    "時間重複エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (newRecord.Id == 0)
                {
                    // 1. DBへの新規登録
                    _repository.Insert(newRecord);
                }
                else
                {
                    // 1. DBの更新
                    _repository.Update(newRecord);

                    // 2. CSVへの書き戻し（元ファイルが判明している場合のみ）
                    if (!string.IsNullOrEmpty(_originalRecord.SourceFilePath) && File.Exists(_originalRecord.SourceFilePath))
                    {
                        _syncService.WriteBackToCsv(_originalRecord, newRecord);
                    }
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存に失敗しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 削除ボタンが押された時の処理
        /// </summary>
        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_originalRecord.Id == 0) return;

            var result = System.Windows.MessageBox.Show(
                "このレコードを削除してもよろしいですか？\n※データベースから完全に削除されます。元のCSVファイルは変更されません。",
                "削除の確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _repository.Delete(_originalRecord.Id);
                    DialogResult = true;
                    Close();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"削除に失敗しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}

