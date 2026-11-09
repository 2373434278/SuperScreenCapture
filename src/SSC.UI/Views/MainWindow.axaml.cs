using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SSC.UI.ViewModels;

namespace SSC.UI.Views;

/// <summary>
/// 设置主窗口（原型 P5）。视图模型负责设置状态，
/// 代码后置只处理必须依赖窗口或系统 API 的交互：
/// 目录选择器、剪贴板、打开文档。
/// </summary>
public partial class MainWindow : Window
{
    private const string OcrPowerShellCommand =
        "Get-WindowsCapability -Online | ? Name -like \"Language.OCR*\"";

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>顶栏“托盘”按钮：隐藏窗口，由菜单栏托盘图标继续驻留。</summary>
    private void OnHideToTray(object? sender, RoutedEventArgs e) => Hide();

    /// <summary>打开系统目录选择器并写回“保存目录”。</summary>
    private async void OnBrowseSaveDirectory(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        var storage = StorageProvider;
        if (storage is null || !storage.CanPickFolder)
        {
            viewModel.ShowStatus(viewModel.L["statusBrowseUnavailable"]);
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = viewModel.L["saveDirectory"],
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            viewModel.SaveDirectory = folders[0].Path.LocalPath;
        }
    }

    /// <summary>复制 Windows OCR 语言包安装命令。</summary>
    private async void OnCopyOcrCommand(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (viewModel is null || clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(OcrPowerShellCommand);
        viewModel.ShowStatus(viewModel.L["commonCopy"]);
    }

    private void OnOpenReadme(object? sender, RoutedEventArgs e) => OpenBundledDocument("README.md");

    private void OnOpenThirdParty(object? sender, RoutedEventArgs e) => OpenBundledDocument("THIRD-PARTY-NOTICES.md");

    /// <summary>打开随发布包附带的文档；缺失时在状态栏提示。</summary>
    private void OpenBundledDocument(string fileName)
    {
        var viewModel = ViewModel;
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
        {
            viewModel?.ShowStatus(viewModel.L["aboutThirdPartyMissing"]);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (InvalidOperationException)
        {
            viewModel?.ShowStatus(viewModel.L["statusBrowseUnavailable"]);
        }
    }
}
