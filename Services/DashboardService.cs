using System;
using System.Collections.Generic;
using System.Linq;
using PomodoroDataManager.Data;
using PomodoroDataManager.Models;

namespace PomodoroDataManager.Services
{
    /// <summary>
    /// ダッシュボード用の集計ロジックを提供するサービスクラス。
    /// 
    /// 既存のIHistoryRepositoryのSearchメソッドでレコードを取得し、
    /// C#メモリ上で集計を行います。
    /// （SQLで複雑なクエリを組むよりも、シンプルで保守しやすいアプローチ）
    /// </summary>
    public class DashboardService
    {
        // データベースへの窓口（リポジトリ）
        private readonly IHistoryRepository _repository;

        /// <summary>
        /// コンストラクタ。リポジトリの参照を受け取ります。
        /// </summary>
        public DashboardService(IHistoryRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// 指定した1日分の作業サマリーを集計して返します。
        /// Focus Meter（本日の合計作業時間）などに使用します。
        /// </summary>
        /// <param name="date">集計対象の日付</param>
        /// <returns>その日のDailySummary</returns>
        public DailySummary GetDailySummary(DateTime date)
        {
            // その日のレコードを検索条件で取得
            var criteria = new SearchCriteria
            {
                DateFrom = date.Date,
                DateTo = date.Date, // Searchメソッド内で+1日されるため、当日分が取得される
                Keyword = null,
                Mode = null
            };

            var records = _repository.Search(criteria);

            // 「作業」モードのレコードだけを抽出
            var workRecords = records.Where(r => r.Mode == "作業").ToList();

            // 「休憩」モードのレコードだけを抽出
            var breakRecords = records.Where(r => r.Mode == "休憩").ToList();

            // 各レコードの作業時間（分）を計算
            // Duration文字列をパースする（例: "25:30" → 25.5分）
            double totalWorkMinutes = workRecords.Sum(r => ParseDurationToMinutes(r.Duration));
            double totalBreakMinutes = breakRecords.Sum(r => ParseDurationToMinutes(r.Duration));

            // 最長連続作業時間を算出
            double maxStreak = workRecords.Count > 0
                ? workRecords.Max(r => ParseDurationToMinutes(r.Duration))
                : 0;

            return new DailySummary
            {
                Date = date.Date,
                TotalWorkMinutes = totalWorkMinutes,
                TotalBreakMinutes = totalBreakMinutes,
                WorkSessionCount = workRecords.Count,
                MaxStreakMinutes = maxStreak,
                TaskMinutes = workRecords.GroupBy(r => r.TaskName)
                                         .ToDictionary(g => g.Key, g => g.Sum(r => ParseDurationToMinutes(r.Duration)))
            };
        }

        /// <summary>
        /// 指定期間の日別サマリーをリストで返します。
        /// 棒グラフ（週間・月間の推移）に使用します。
        /// データがない日も0として含まれます。
        /// </summary>
        /// <param name="startDate">開始日</param>
        /// <param name="endDate">終了日</param>
        /// <returns>日別DailySummaryのリスト</returns>
        public List<DailySummary> GetPeriodSummary(DateTime startDate, DateTime endDate)
        {
            // 期間内の全レコードを一括取得（何度もDBにアクセスしない）
            var criteria = new SearchCriteria
            {
                DateFrom = startDate.Date,
                DateTo = endDate.Date,
                Keyword = null,
                Mode = null
            };

            var allRecords = _repository.Search(criteria);

            // 日付ごとにグループ化して集計
            var grouped = allRecords
                .GroupBy(r => r.StartTime.Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            var summaries = new List<DailySummary>();

            // 開始日から終了日まで1日ずつ走査（データがない日も0で埋める）
            for (var day = startDate.Date; day <= endDate.Date; day = day.AddDays(1))
            {
                if (grouped.TryGetValue(day, out var dayRecords))
                {
                    var workRecords = dayRecords.Where(r => r.Mode == "作業").ToList();
                    var breakRecords = dayRecords.Where(r => r.Mode == "休憩").ToList();

                    summaries.Add(new DailySummary
                    {
                        Date = day,
                        TotalWorkMinutes = workRecords.Sum(r => ParseDurationToMinutes(r.Duration)),
                        TotalBreakMinutes = breakRecords.Sum(r => ParseDurationToMinutes(r.Duration)),
                        WorkSessionCount = workRecords.Count,
                        MaxStreakMinutes = workRecords.Count > 0
                            ? workRecords.Max(r => ParseDurationToMinutes(r.Duration))
                            : 0,
                        TaskMinutes = workRecords.GroupBy(r => r.TaskName)
                                                 .ToDictionary(g => g.Key, g => g.Sum(r => ParseDurationToMinutes(r.Duration)))
                    });
                }
                else
                {
                    // データなしの日は全て0
                    summaries.Add(new DailySummary
                    {
                        Date = day,
                        TotalWorkMinutes = 0,
                        TotalBreakMinutes = 0,
                        WorkSessionCount = 0,
                        MaxStreakMinutes = 0
                    });
                }
            }

            return summaries;
        }

        /// <summary>
        /// Duration文字列（例: "25:30"）を分単位の数値にパースします。
        /// 
        /// 対応フォーマット:
        ///   "MM:SS"  → 分:秒  （例: "25:30" → 25.5分）
        ///   "H:MM:SS" → 時:分:秒（例: "1:05:00" → 65分）
        ///   パースに失敗した場合は、StartTimeとEndTimeの差分から計算を試みます。
        /// </summary>
        private double ParseDurationToMinutes(string duration)
        {
            if (string.IsNullOrWhiteSpace(duration))
                return 0;

            var parts = duration.Split(':');

            try
            {
                if (parts.Length == 2)
                {
                    // "MM:SS" 形式
                    int minutes = int.Parse(parts[0]);
                    int seconds = int.Parse(parts[1]);
                    return minutes + (seconds / 60.0);
                }
                else if (parts.Length == 3)
                {
                    // "H:MM:SS" 形式
                    int hours = int.Parse(parts[0]);
                    int minutes = int.Parse(parts[1]);
                    int seconds = int.Parse(parts[2]);
                    return (hours * 60) + minutes + (seconds / 60.0);
                }
            }
            catch
            {
                // パース失敗時は0を返す（ログに記録することも検討）
            }

            return 0;
        }
    }
}
