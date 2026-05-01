using System.Collections.Generic;
using PomodoroDataManager.Models;

namespace PomodoroDataManager.Data
{
    /// <summary>
    /// データの「出し入れ専用の窓口（インターフェース）」。
    /// 
    /// このインターフェースを使うことで、アプリ本体は
    /// 「Accessで保存されているのか、SQLiteで保存されているのか」を
    /// 気にしなくてよくなります。
    /// 
    /// 将来SQLiteに切り替えたい時は、このインターフェースに合わせた
    /// 新しい実装を作るだけでOKです（アプリの画面や集計ロジックは変更不要）。
    /// </summary>
    public interface IHistoryRepository
    {
        /// <summary>
        /// データベースを初期化します（テーブルが存在しない場合は作成）。
        /// </summary>
        void Initialize();

        /// <summary>
        /// すでにDBに存在する全レコードのユニークキー一覧を取得します。
        /// 重複チェックに使います。
        /// </summary>
        HashSet<string> GetAllUniqueKeys();

        /// <summary>
        /// 新しいレコードをDBに1件追加します。
        /// </summary>
        void Insert(PomodoroRecord record);

        /// <summary>
        /// 既存のレコードを更新します。UniqueKeyが一致するレコードを上書きします。
        /// </summary>
        void Update(PomodoroRecord record);

        /// <summary>
        /// 指定されたIDのレコードを削除します。
        /// </summary>
        /// <param name="id">削除するレコードのID</param>
        void Delete(int id);

        /// <summary>
        /// 検索条件に合致するレコードを取得します（新しい順）。
        /// criteriaが空の場合は全件取得します。
        /// </summary>
        List<PomodoroRecord> Search(SearchCriteria criteria);
    }
}
