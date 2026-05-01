using System.Configuration;
using System.Data;
using System.Windows;

namespace PomodoroDataManager;

/// <summary>
/// WPFアプリのエントリーポイント。
/// UseWindowsFormsを使うと「Application」がWPFとWindowsForms両方に存在するため、
/// System.Windows.Application と完全修飾名で指定して衝突を回避します。
/// </summary>
public partial class App : System.Windows.Application
{
}

