using System.Threading;
using System.Configuration;
using System.Data;
using System.Windows;
using RefScrn.Services;

namespace RefScrn;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private GlobalHotkeyService _hotkeyService;
    private SystemTrayService _trayService;
    private Services.SettingsService _settingsService;
    private SettingsWindow _settingsWindow;
    private OverlayWindow _currentOverlay; // 跟踪当前打开的截图窗口
    private Mutex _mutex; // Added for single instance
    private System.Diagnostics.Stopwatch _startupTimer;

    public App()
    {
        _startupTimer = System.Diagnostics.Stopwatch.StartNew();
        
        // Register Global Exception Handlers
        this.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        
        LogService.Info("=== App Instance Starting ===");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        LogService.Info("OnStartup initiated.");

        // Ensure single instance
        const string appName = "RefScrn_SingleInstance_Mutex";
        bool createdNew;
        try 
        {
            _mutex = new Mutex(true, appName, out createdNew);
            if (!createdNew)
            {
                LogService.Info("Another instance is already running. Showing alert and shutting down.");
                MessageBox.Show("程序已在运行中。\n请检查系统托盘中的 RefScrn 图标。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Mutex creation failed", ex);
        }

        try 
        {
            LogService.Info("Initializing services...");
            _settingsService = new Services.SettingsService();
            _hotkeyService = new GlobalHotkeyService();
            _trayService = new SystemTrayService(ShutdownApp, ShowSettings);
            _trayService.ShowMessage("RefScrn", "已在后台运行。按 Alt+A 开始截图。");
            LogService.Info("Services initialized successfully.");
        }
        catch (Exception ex)
        {
            LogService.Error("Critical service initialization failed", ex);
            MessageBox.Show($"启动失败: {ex.Message}\n详情请见日志: {LogService.GetLogPath()}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _startupTimer.Stop();
        LogService.Info($"[Performance] Startup took: {_startupTimer.ElapsedMilliseconds} ms");

        // Create hidden MainWindow to receive header
        try 
        {
            var mainWindow = new MainWindow();
            mainWindow.WindowStyle = WindowStyle.None;
            mainWindow.ShowInTaskbar = false;
            mainWindow.Width = 0;
            mainWindow.Height = 0;
            mainWindow.Opacity = 0;
            mainWindow.Show(); // Must show to ensure Handle is created
            mainWindow.Hide(); // Hide immediately (Handle remains valid)
            this.MainWindow = mainWindow;
            LogService.Info("Hidden background window created.");
        }
        catch (Exception ex)
        {
             LogService.Error("Failed to create hidden main window", ex);
        }

        RegisterCurrentHotkey();
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogService.Error("UI Thread Unhandled Exception", e.Exception);
        MessageBox.Show($"发生程序错误 (UI): {e.Exception.Message}\n日志已保存至: {LogService.GetLogPath()}", "程序异常", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        LogService.Error("AppDomain Unhandled Exception", ex);
        MessageBox.Show($"发生严重错误: {ex?.Message}\n程序即将退出。", "严重错误", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnUnobservedTaskException(object sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        LogService.Error("Unobserved Task Exception", e.Exception);
        e.SetObserved();
    }

    public void RegisterCurrentHotkey()
    {
         if (MainWindow == null) return;
         
         // Parse hotkey string from settings
         var hotkeyStr = _settingsService.CurrentSettings.Hotkey; // e.g., "Ctrl+Alt+A"
         if (string.IsNullOrEmpty(hotkeyStr)) hotkeyStr = "Alt+A";

         try 
         {
             var (modifiers, key) = ParseHotkey(hotkeyStr);
             _hotkeyService.Register(MainWindow, modifiers, key, OnHotkeyTriggered);
             LogService.Info($"Hotkey registered: {hotkeyStr}");
         }
         catch(Exception ex)
         {
              LogService.Error($"Failed to register hotkey '{hotkeyStr}'", ex);
              _trayService.ShowMessage("错误", $"快捷键 '{hotkeyStr}' 注册失败\n{ex.Message}");
         }
    }

    private (System.Windows.Input.ModifierKeys, System.Windows.Input.Key) ParseHotkey(string hotkey)
    {
        System.Windows.Input.ModifierKeys mods = System.Windows.Input.ModifierKeys.None;
        if (hotkey.Contains("Ctrl")) mods |= System.Windows.Input.ModifierKeys.Control;
        if (hotkey.Contains("Alt")) mods |= System.Windows.Input.ModifierKeys.Alt;
        if (hotkey.Contains("Shift")) mods |= System.Windows.Input.ModifierKeys.Shift;
        if (hotkey.Contains("Win")) mods |= System.Windows.Input.ModifierKeys.Windows;

        string keyStr = hotkey.Split('+').Last();
        
        // Use KeyConverter for more robust parsing
        try 
        {
            var converter = new System.Windows.Input.KeyConverter();
            var keyObj = converter.ConvertFromString(keyStr);
            if (keyObj is System.Windows.Input.Key k)
            {
                return (mods, k);
            }
        }
        catch 
        {
            // Fallback to Enum.TryParse if Converter fails
            if (Enum.TryParse(keyStr, out System.Windows.Input.Key kEnum))
            {
                return (mods, kEnum);
            }
        }

        return (System.Windows.Input.ModifierKeys.Alt, System.Windows.Input.Key.A); // Fallback
    }

    private void OnHotkeyTriggered()
    {
        Console.WriteLine("App.OnHotkeyTriggered: Hotkey pressed!");
        
        // 如果已经有打开的 OverlayWindow，先关闭它
        if (_currentOverlay != null && _currentOverlay.IsLoaded)
        {
            _currentOverlay.Close();
            _currentOverlay = null;
            return; // 本次按键用于关闭现有窗口
        }
        
        try
        {
            var (screenshot, bounds) = ScreenCaptureService.CaptureScreenUnderMouse();
            _currentOverlay = new OverlayWindow(screenshot, bounds.Left, bounds.Top, _settingsService.CurrentSettings);
            _currentOverlay.Closed += (s, e) => _currentOverlay = null; // 窗口关闭时清空引用
            _currentOverlay.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Capture failed: {ex.Message}");
        }
    }

    private void ShutdownApp() => Current.Shutdown();
    
    private void ShowSettings()
    {
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(_settingsService);
            _settingsWindow.Closed += (s, e) => 
            {
                _settingsWindow = null;
                // Reload settings and re-register hotkey
                RegisterCurrentHotkey();
                // Persist settings explicitly just in case
                _settingsService.Save();
            };
            _settingsWindow.Show();
        }
        else
        {
            _settingsWindow.Activate();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogService.Info("App exiting...");
        _trayService?.Dispose();
        _hotkeyService?.Dispose();
        _mutex?.ReleaseMutex(); // Release the mutex on exit
        base.OnExit(e);
    }
}
