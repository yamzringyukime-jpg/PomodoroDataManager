using System;

namespace PomodoroDataManager.Models
{
    /// <summary>
    /// 1日分の作業サマリーを表すデータモデル。
    /// ダッシュボードのグラフやFocus Meterで使用します。
    /// </summary>
    public class DailySummary
    {
        /// <summary>対象日付</summary>
        public DateTime Date { get; set; }

        /// <summary>その日の「作業」モードの合計時間（分単位）</summary>
        public double TotalWorkMinutes { get; set; }

        /// <summary>その日の「休憩」モードの合計時間（分単位）</summary>
        public double TotalBreakMinutes { get; set; }

        /// <summary>その日の作業セッション数</summary>
        public int WorkSessionCount { get; set; }

        /// <summary>その日の最長連続作業時間（分単位）</summary>
        public double MaxStreakMinutes { get; set; }
        
        /// <summary>タスク名ごとの合計時間（分単位）</summary>
        public System.Collections.Generic.Dictionary<string, double> TaskMinutes { get; set; } = new();

        /// <summary>
        /// 合計作業時間を「X時間Y分」形式の文字列で返すヘルパー。
        /// 例: 3時間45分 → "3h 45m", 25分 → "0h 25m"
        /// </summary>
        public string TotalWorkFormatted
        {
            get
            {
                int hours = (int)(TotalWorkMinutes / 60);
                int mins = (int)(TotalWorkMinutes % 60);
                return $"{hours}h {mins:D2}m";
            }
        }
    }
}
