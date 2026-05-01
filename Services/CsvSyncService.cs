using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PomodoroDataManager.Data;
using PomodoroDataManager.Models;

namespace PomodoroDataManager.Services
{
    /// <summary>
    /// CSVファイルをDBと同期するためのサービスクラス。
    /// 「新しいデータを自動で取り込む」「CSVの編集内容を手動で反映する」の
    /// 両方の機能を担当します。
    /// </summary>
    public class CsvSyncService
    {
        // データの出し入れ用「窓口」（AccessかSQLiteかを気にしなくてよい）
        private readonly IHistoryRepository _repository;

        // OneDriveフォルダを監視するためのウォッチャー
        private FileSystemWatcher? _watcher;

        // イベント：新しいデータが取り込まれた時に呼び出される（画面を更新するため）
        public event Action<int>? OnImported;

        // イベント：エラーが発生した時に呼び出される
        public event Action<string>? OnError;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        /// <param name="repository">使用するDBの窓口</param>
        public CsvSyncService(IHistoryRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// OneDriveの指定フォルダの監視を開始します。
        /// 新しいCSVファイルが追加・変更されると自動でインポートを試みます。
        /// </summary>
        /// <param name="folderPath">監視するOneDriveフォルダのパス</param>
        public void StartWatching(string folderPath)
        {
            // 既存の監視があれば停止する
            StopWatching();

            if (!Directory.Exists(folderPath))
            {
                OnError?.Invoke($"監視フォルダが見つかりません。フォルダを確認してください:\n{folderPath}");
                return;
            }

            // FileSystemWatcher = 「フォルダの変化を常に見張る番人」
            _watcher = new FileSystemWatcher(folderPath)
            {
                Filter = "*.csv",                    // CSVファイルだけを対象にする
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true           // 監視を有効にする
            };

            // 新しいCSVファイルが追加された時
            _watcher.Created += (_, e) => AutoImportFile(e.FullPath);
            // CSVファイルが変更された時（タイムスタンプが変わった時）
            _watcher.Changed += (_, e) => AutoImportFile(e.FullPath);
            // ※ファイルが削除されても何もしない（削除保護）
        }

        /// <summary>
        /// フォルダの監視を停止します。
        /// </summary>
        public void StopWatching()
        {
            _watcher?.Dispose();
            _watcher = null;
        }

        /// <summary>
        /// 【自動・新規登録】
        /// 指定されたCSVファイルを読み込み、まだDBにないデータだけを追加します。
        /// CSVファイルが存在しない場合は何もしません（削除保護）。
        /// </summary>
        /// <param name="csvFilePath">取り込むCSVファイルのフルパス</param>
        public void AutoImportFile(string csvFilePath)
        {
            // ★削除保護: ファイルが存在しない場合はDB側を一切変更しない
            if (!File.Exists(csvFilePath))
                return;

            try
            {
                // ファイルが別プロセス（タイムスタンプ書き込みなど）で使用中の場合に
                // 少し待ってから読むことで、読み込みエラーを減らします
                System.Threading.Thread.Sleep(500);

                var newRecords = ParseCsvFile(csvFilePath);
                var existingKeys = _repository.GetAllUniqueKeys();

                int importedCount = 0;
                foreach (var record in newRecords)
                {
                    // すでにDBにあるデータはスキップ（重複しない新規データのみ追加）
                    if (!existingKeys.Contains(record.UniqueKey))
                    {
                        record.SourceFilePath = csvFilePath; // インポート元のパスを保持
                        _repository.Insert(record);
                        importedCount++;
                    }
                }

                if (importedCount > 0)
                    OnImported?.Invoke(importedCount);
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"CSVの自動インポートでエラーが発生しました:\n{ex.Message}");
                LogService.WriteLog($"[自動インポートエラー] {csvFilePath}: {ex}");
            }
        }

        /// <summary>
        /// 【手動・更新反映】
        /// 指定されたCSVファイルを読み込み、DBの既存データと差分を比較して更新します。
        /// CSVファイルが存在しない場合は何もしません（削除保護）。
        /// </summary>
        /// <param name="csvFilePath">更新元のCSVファイルのフルパス</param>
        /// <returns>新規追加件数と更新件数のセット</returns>
        public (int inserted, int updated) ManualSyncFile(string csvFilePath)
        {
            // ★削除保護: ファイルが存在しない場合はDB側を一切変更しない
            if (!File.Exists(csvFilePath))
            {
                OnError?.Invoke($"指定されたCSVファイルが見つかりません。\n{csvFilePath}");
                return (0, 0);
            }

            try
            {
                var records = ParseCsvFile(csvFilePath);
                var existingKeys = _repository.GetAllUniqueKeys();

                int insertedCount = 0;
                int updatedCount = 0;

                foreach (var record in records)
                {
                    if (!existingKeys.Contains(record.UniqueKey))
                    {
                        // DBに存在しない → 新規追加
                        record.SourceFilePath = csvFilePath;
                        _repository.Insert(record);
                        insertedCount++;
                    }
                    else
                    {
                        // DBに存在する → 更新
                        _repository.Update(record);
                        updatedCount++;
                    }
                }

                return (insertedCount, updatedCount);
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"CSVの手動同期でエラーが発生しました:\n{ex.Message}");
                LogService.WriteLog($"[手動同期エラー] {csvFilePath}: {ex}");
                return (0, 0);
            }
        }

        /// <summary>
        /// CSVファイルを読み込み、PomodoroRecordのリストに変換します。
        /// ヘッダー行（1行目）はスキップします。
        /// BOM付きUTF-8に対応しています。
        /// </summary>
        /// <param name="filePath">CSVファイルのパス</param>
        private static List<PomodoroRecord> ParseCsvFile(string filePath)
        {
            var records = new List<PomodoroRecord>();

            // BOM付きUTF-8（ポモドーロタイマーの出力形式）として読み込む
            // Encoding.UTF8 はデフォルトでBOMを自動検知して処理します
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);

            // 1行目はヘッダー（「作業内容,モード,開始時刻,...」）なのでスキップ
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // CSVをカンマで分割（"タスク名" のようにダブルクォートで囲まれた場合も考慮）
                var columns = SplitCsvLine(line);
                if (columns.Length < 5) continue;

                // 日時の形式: 2025/01/15 09:30:00
                const string dtFormat = "yyyy/MM/dd HH:mm:ss";

                if (!DateTime.TryParseExact(columns[2], dtFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime))
                    continue;
                if (!DateTime.TryParseExact(columns[3], dtFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime))
                    continue;

                records.Add(new PomodoroRecord
                {
                    TaskName = columns[0],
                    Mode = columns[1],
                    StartTime = startTime,
                    EndTime = endTime,
                    Duration = columns[4],
                });
            }

            return records;
        }

        /// <summary>
        /// CSVの1行をカンマで分割します。
        /// ダブルクォートで囲まれたフィールド（「A, B」のようにカンマを含む場合）も正しく処理します。
        /// </summary>
        private static string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString().Trim('"').Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString().Trim('"').Trim());

            return result.ToArray();
        }
        /// <summary>
        /// CSVファイル内の特定のレコードを書き換えます。
        /// 編集内容を元のファイルに同期するために使用します。
        /// </summary>
        /// <param name="oldRecord">編集前のレコード（検索用）</param>
        /// <param name="newRecord">編集後のレコード（上書き用）</param>
        public void WriteBackToCsv(PomodoroRecord oldRecord, PomodoroRecord newRecord)
        {
            if (string.IsNullOrEmpty(newRecord.SourceFilePath) || !File.Exists(newRecord.SourceFilePath))
                return;

            var lines = File.ReadAllLines(newRecord.SourceFilePath, Encoding.UTF8);
            var newLines = new List<string>();
            bool updated = false;

            // 旧レコードを特定するためのユニークキー
            var oldKey = oldRecord.UniqueKey;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    newLines.Add(line);
                    continue;
                }

                var columns = SplitCsvLine(trimmed);
                if (columns.Length < 5)
                {
                    newLines.Add(line);
                    continue;
                }

                // 開始時刻などが一致するかチェック（実質的なUniqueKey比較）
                var rowKey = $"{columns[0]}|{columns[1]}|{columns[2]}";
                if (!updated && rowKey == oldKey)
                {
                    // 一致した場合、新しいレコードの内容で1行作成
                    var newLine = $"\"{newRecord.TaskName}\",\"{newRecord.Mode}\",\"{newRecord.StartTime:yyyy/MM/dd HH:mm:ss}\",\"{newRecord.EndTime:yyyy/MM/dd HH:mm:ss}\",\"{newRecord.Duration}\"";
                    newLines.Add(newLine);
                    updated = true;
                }
                else
                {
                    newLines.Add(line);
                }
            }

            if (updated)
            {
                File.WriteAllLines(newRecord.SourceFilePath, newLines, Encoding.UTF8);
            }
        }
    }
}
