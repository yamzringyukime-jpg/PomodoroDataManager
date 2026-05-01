using System;
using System.Collections.Generic;
using System.Data.OleDb;
using PomodoroDataManager.Models;

namespace PomodoroDataManager.Data
{
    /// <summary>
    /// IHistoryRepositoryインターフェースの「Access版」実装。
    /// Microsoft Access (.accdb) ファイルを使ってデータを保存・読み込みします。
    /// 
    /// 【注意】このクラスを使うには、PCに「Microsoft Access Database Engine」が
    /// インストールされている必要があります（Accessを使っているPCなら通常インストール済みです）。
    /// </summary>
    public class AccessHistoryRepository : IHistoryRepository
    {
        // Accessファイルへの接続文字列（接続の設定情報）
        private readonly string _connectionString;

        /// <summary>
        /// コンストラクタ（クラスを作る時の初期化処理）。
        /// </summary>
        /// <param name="dbFilePath">Accessデータベースファイル（.accdb）の保存先パス</param>
        public AccessHistoryRepository(string dbFilePath)
        {
            // OleDb経由でAccessファイルに接続するための設定文字列を組み立てます
            _connectionString = $"Provider=Microsoft.ACE.OLEDB.16.0;Data Source={dbFilePath};Persist Security Info=False;";
        }

        /// <summary>
        /// データベース（Accessファイル）の初期化。
        /// 必要なテーブルが存在しない場合は自動で作成します。
        /// </summary>
        public void Initialize()
        {
            // テーブル作成SQL。IF NOT EXISTSはAccessでは使えないため、
            // エラーが出た場合（既に存在する場合）は無視する方針にします。
            const string createTableSql = @"
                CREATE TABLE PomodoroHistory (
                    Id       AUTOINCREMENT PRIMARY KEY,
                    TaskName TEXT(255),
                    Mode     TEXT(50),
                    StartTime DATETIME,
                    EndTime   DATETIME,
                    Duration  TEXT(20),
                    UniqueKey TEXT(255),
                    SourceFilePath TEXT(255)
                )";

            const string addColumnSql = "ALTER TABLE PomodoroHistory ADD COLUMN SourceFilePath TEXT(255)";

            using var conn = new OleDbConnection(_connectionString);
            conn.Open();
            using var cmd = new OleDbCommand(createTableSql, conn);
            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (OleDbException ex)
            {
                // テーブルが既に存在する場合に発生する例外を無視します。
                // 環境やプロバイダーによりエラーコードやメッセージが異なる場合があるため、
                // 一般的なエラーコードとメッセージ内容の両方で判定します。
                bool isTableExistsError = ex.ErrorCode == -2147217892 || 
                                          ex.Message.Contains("既に存在") || 
                                          ex.Message.Contains("already exists");

                if (!isTableExistsError)
                {
                    throw;
                }
            }

            // 既存のテーブルにカラムがない場合に備えて、追加を試みる（エラーは無視）
            try
            {
                using var cmdAdd = new OleDbCommand(addColumnSql, conn);
                cmdAdd.ExecuteNonQuery();
            }
            catch { /* すでにある場合はエラーになるが無視 */ }
        }

        /// <summary>
        /// DBに存在する全レコードのユニークキーをHashSet（重複なし集合）で返します。
        /// 「新しいデータかどうか」を素早く判定するために使います。
        /// </summary>
        public HashSet<string> GetAllUniqueKeys()
        {
            var keys = new HashSet<string>();
            const string sql = "SELECT UniqueKey FROM PomodoroHistory";

            using var conn = new OleDbConnection(_connectionString);
            conn.Open();
            using var cmd = new OleDbCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                    keys.Add(reader.GetString(0));
            }

            return keys;
        }

        /// <summary>
        /// 新しいレコードをDBに1件追加（Insert）します。
        /// </summary>
        public void Insert(PomodoroRecord record)
        {
            const string sql = @"
                INSERT INTO PomodoroHistory (TaskName, Mode, StartTime, EndTime, Duration, UniqueKey, SourceFilePath)
                VALUES (@TaskName, @Mode, @StartTime, @EndTime, @Duration, @UniqueKey, @SourceFilePath)";

            using var conn = new OleDbConnection(_connectionString);
            conn.Open();
            using var cmd = new OleDbCommand(sql, conn);
            cmd.Parameters.AddWithValue("@TaskName", record.TaskName);
            cmd.Parameters.AddWithValue("@Mode", record.Mode);
            cmd.Parameters.AddWithValue("@StartTime", record.StartTime);
            cmd.Parameters.AddWithValue("@EndTime", record.EndTime);
            cmd.Parameters.AddWithValue("@Duration", record.Duration);
            cmd.Parameters.AddWithValue("@UniqueKey", record.UniqueKey);
            cmd.Parameters.AddWithValue("@SourceFilePath", record.SourceFilePath ?? "");
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 既存レコードを更新（Update）します。
        /// Idが一致するレコードの内容を上書きします。
        /// </summary>
        public void Update(PomodoroRecord record)
        {
            const string sql = @"
                UPDATE PomodoroHistory
                SET TaskName = @TaskName,
                    Mode = @Mode,
                    StartTime = @StartTime,
                    EndTime = @EndTime,
                    Duration = @Duration,
                    UniqueKey = @UniqueKey,
                    SourceFilePath = @SourceFilePath
                WHERE Id = @Id";

            using var conn = new OleDbConnection(_connectionString);
            conn.Open();
            using var cmd = new OleDbCommand(sql, conn);
            cmd.Parameters.AddWithValue("@TaskName", record.TaskName);
            cmd.Parameters.AddWithValue("@Mode", record.Mode);
            cmd.Parameters.AddWithValue("@StartTime", record.StartTime);
            cmd.Parameters.AddWithValue("@EndTime", record.EndTime);
            cmd.Parameters.AddWithValue("@Duration", record.Duration);
            cmd.Parameters.AddWithValue("@UniqueKey", record.UniqueKey);
            cmd.Parameters.AddWithValue("@SourceFilePath", record.SourceFilePath ?? "");
            cmd.Parameters.AddWithValue("@Id", record.Id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 指定されたIDのレコードを削除します。
        /// </summary>
        public void Delete(int id)
        {
            const string sql = "DELETE FROM PomodoroHistory WHERE Id = @Id";

            using var conn = new OleDbConnection(_connectionString);
            conn.Open();
            using var cmd = new OleDbCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 検索条件に一致するレコードを取得します（新しい順）。
        /// </summary>
        public List<PomodoroRecord> Search(SearchCriteria criteria)
        {
            var records = new List<PomodoroRecord>();
            var sqlHead = "SELECT Id, TaskName, Mode, StartTime, EndTime, Duration, UniqueKey, SourceFilePath FROM PomodoroHistory";
            var whereClauses = new List<string>();
            var parameters = new Dictionary<string, object>();

            if (criteria != null)
            {
                if (criteria.DateFrom.HasValue)
                {
                    whereClauses.Add("StartTime >= @DateFrom");
                    parameters.Add("@DateFrom", criteria.DateFrom.Value.Date);
                }
                if (criteria.DateTo.HasValue)
                {
                    whereClauses.Add("StartTime < @DateTo");
                    parameters.Add("@DateTo", criteria.DateTo.Value.Date.AddDays(1));
                }
                if (!string.IsNullOrEmpty(criteria.Keyword))
                {
                    whereClauses.Add("TaskName LIKE @Keyword");
                    parameters.Add("@Keyword", $"%{criteria.Keyword}%");
                }
                if (!string.IsNullOrEmpty(criteria.Mode))
                {
                    whereClauses.Add("Mode = @Mode");
                    parameters.Add("@Mode", criteria.Mode);
                }
            }

            var sql = sqlHead;
            if (whereClauses.Count > 0)
            {
                sql += " WHERE " + string.Join(" AND ", whereClauses);
            }
            sql += " ORDER BY StartTime DESC";

            using var conn = new OleDbConnection(_connectionString);
            conn.Open();
            using var cmd = new OleDbCommand(sql, conn);
            foreach (var p in parameters)
            {
                cmd.Parameters.AddWithValue(p.Key, p.Value);
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                records.Add(new PomodoroRecord
                {
                    Id = reader.GetInt32(0),
                    TaskName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Mode = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    StartTime = reader.IsDBNull(3) ? DateTime.MinValue : reader.GetDateTime(3),
                    EndTime = reader.IsDBNull(4) ? DateTime.MinValue : reader.GetDateTime(4),
                    Duration = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    SourceFilePath = reader.IsDBNull(7) ? "" : reader.GetString(7),
                });
            }

            return records;
        }

        // Search(empty) で代用可能にするため削除するか、互換性のために残す（今回はSearchに統一するためインターフェースに合わせて削除）
    }
}
