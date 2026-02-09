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
    private Mutex _mutex; // Added for single instance
    private System.Diagnostics.Stopwatch _startupTimer;

    public App()
    {
        _startupTimer = System.Diagnostics.Stopwatch.StartNew();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Ensure single instance
        const string appName = "RefScrn_SingleInstance_Mutex";
        bool createdNew;
        _mutex = new Mutex(true, appName, out createdNew);

        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _settingsService = new Services.SettingsService();
        _hotkeyService = new GlobalHotkeyService();
        _trayService = new SystemTrayService(ShutdownApp, ShowSettings);
        _trayService.ShowMessage("RefScrn", "Running in background. Check Settings for Hotkey.");

        _startupTimer.Stop();
        Console.WriteLine($"[Performance] Startup took: {_startupTimer.ElapsedMilliseconds} ms");

        // Create hidden MainWindow to receive header
        var mainWindow = new MainWindow();
        mainWindow.WindowStyle = WindowStyle.None;
        mainWindow.ShowInTaskbar = false;
        mainWindow.Width = 0;
        mainWindow.Height = 0;
        mainWindow.Opacity = 0;
        mainWindow.Show(); // Must show to ensure Handle is created
        mainWindow.Hide(); // Hide immediately (Handle remains valid)
        this.MainWindow = mainWindow;

        RegisterCurrentHotkey();
        
        // SettingsService handles auto-start via registry on save, so we just load state here
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
             
             // Notify user of success (Optional, but good for verification)
             // _trayService.ShowMessage("RefScrn", $"快捷键已更新为: {hotkeyStr}");
         }
         catch(Exception ex)
         {
              Console.WriteLine($"Failed to register hotkey '{hotkeyStr}': {ex.Message}");
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
        // ... (existing logic) inside OnHotkeyTriggered is fine, but I replaced the whole method to match line numbers comfortably
        Console.WriteLine("App.OnHotkeyTriggered: Hotkey pressed!");
        try
        {
            // ...
            var (screenshot, bounds) = ScreenCaptureService.CaptureScreenUnderMouse();
            var overlay = new OverlayWindow(screenshot, bounds.Left, bounds.Top, _settingsService.CurrentSettings);
            overlay.Show();
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
        _trayService?.Dispose();
        _hotkeyService?.Dispose();
        _mutex?.ReleaseMutex(); // Release the mutex on exit
        base.OnExit(e);
    }
}
