using System;

namespace PomodoroDataManager.Models
{
    /// <summary>
    /// ポモドーロの1セッション（ひとつの作業or休憩）を表すデータモデル。
    /// CSVの1行に対応します。
    /// </summary>
    public class PomodoroRecord
    {
        /// <summary>DB上の一意なID（自動採番）</summary>
        public int Id { get; set; }

        /// <summary>作業内容の名前（例：「コーディング」「資料作成」）</summary>
        public string TaskName { get; set; } = string.Empty;

        /// <summary>モード（「作業」または「休憩」）</summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>開始時刻</summary>
        public DateTime StartTime { get; set; }

        /// <summary>終了時刻</summary>
        public DateTime EndTime { get; set; }

        /// <summary>経過時間の文字列（例：「25:30」= 25分30秒）</summary>
        public string Duration { get; set; } = string.Empty;
        
        /// <summary>取り込み元のCSVファイルパス（編集の書き戻しに使用）</summary>
        public string SourceFilePath { get; set; } = string.Empty;

        /// <summary>
        /// このレコードが重複していないかを判定するためのキー文字列。
        /// タスク名・モード・開始時刻の組み合わせを使います。
        /// </summary>
        public string UniqueKey => $"{TaskName}|{Mode}|{StartTime:yyyy/MM/dd HH:mm:ss}";
    }
}
