using System;
using System.IO;

namespace PomodoroDataManager.Services
{
    /// <summary>
    /// アプリのログをテキストファイルに書き出すサービスクラス。
    /// エラーが起きた時に「いつ、何が起きたか」を記録しておくためのものです。
    /// ログファイルの場所: アプリケーション（.exe）と同じフォルダ内の pomodoro_manager.log
    /// </summary>
    public static class LogService
    {
        // ログファイルの保存先（実行ファイルと同じフォルダ）
        private static readonly string LogFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "pomodoro_manager.log"
        );

        /// <summary>
        /// ログに1行書き込みます。
        /// 書き込みに失敗しても例外を発生させません（ログ失敗でアプリが落ちないよう）。
        /// </summary>
        /// <param name="message">書き込む内容</param>
        public static void WriteLog(string message)
        {
            try
            {
                // 日時を先頭に付けて追記（上書きではなく後ろに追記する）
                var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                File.AppendAllText(LogFilePath, logLine + Environment.NewLine, System.Text.Encoding.UTF8);
            }
            catch
            {
                // ログ書き込み失敗は無視（ログのせいでアプリが止まらないようにする）
            }
        }
    }
}
