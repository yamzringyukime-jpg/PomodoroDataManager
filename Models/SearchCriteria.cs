using System;

namespace PomodoroDataManager.Models
{
    /// <summary>
    /// 履歴検索のための検索条件を保持するクラス。
    /// </summary>
    public class SearchCriteria
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? Keyword { get; set; }
        public string? Mode { get; set; } // "作業", "休憩", or null/empty for "All"

        /// <summary>
        /// 条件がすべて空（全表示状態）かどうか。
        /// </summary>
        public bool IsEmpty => !DateFrom.HasValue && !DateTo.HasValue && string.IsNullOrEmpty(Keyword) && string.IsNullOrEmpty(Mode);
    }
}
