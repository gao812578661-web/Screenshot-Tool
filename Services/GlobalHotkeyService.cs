using System;
using System.Windows;
using System.Windows.Interop;
using WpfInput = System.Windows.Input;

namespace RefScrn.Services
{
    public class GlobalHotkeyService : IDisposable
    {
        private HwndSource _source;
        private const int HOTKEY_ID = 9000;
        private Action _onHotkeyTriggered;

        public void Register(Window window, WpfInput.ModifierKeys modifiers, WpfInput.Key key, Action onTrigger)
        {
            if (window == null) return;
            
            // Dispose previous if any (simple implementation supports 1 hotkey)
            if (_source != null)
            {
                var helperOld = new WindowInteropHelper(window);
                NativeMethods.UnregisterHotKey(helperOld.Handle, HOTKEY_ID);
                _source.RemoveHook(HwndHook);
                _source = null;
            }

            _onHotkeyTriggered = onTrigger;
            var helper = new WindowInteropHelper(window);
            _source = HwndSource.FromHwnd(helper.Handle);
            _source.AddHook(HwndHook);

            int vKey = WpfInput.KeyInterop.VirtualKeyFromKey(key);
            int mod = 0;
            if ((modifiers & WpfInput.ModifierKeys.Alt) == WpfInput.ModifierKeys.Alt) mod |= NativeMethods.MOD_ALT;
            if ((modifiers & WpfInput.ModifierKeys.Control) == WpfInput.ModifierKeys.Control) mod |= NativeMethods.MOD_CONTROL;
            if ((modifiers & WpfInput.ModifierKeys.Shift) == WpfInput.ModifierKeys.Shift) mod |= NativeMethods.MOD_SHIFT;
            if ((modifiers & WpfInput.ModifierKeys.Windows) == WpfInput.ModifierKeys.Windows) mod |= NativeMethods.MOD_WIN;

            if (!NativeMethods.RegisterHotKey(helper.Handle, HOTKEY_ID, mod, vKey))
            {
                // Silently fail or log, as re-registering can be spammy. 
                // Or handle duplicate registration gracefully.
                 Console.WriteLine($"Failed to register hotkey. Error: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
            }
            else
            {
                Console.WriteLine($"Hotkey registered successfully: {modifiers}+{key}");
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                Console.WriteLine("Hotkey received!");
                _onHotkeyTriggered?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Unregister(Window window)
        {
             if (_source != null)
            {
                var helper = new WindowInteropHelper(window);
                NativeMethods.UnregisterHotKey(helper.Handle, HOTKEY_ID);
                _source.RemoveHook(HwndHook);
                _source = null;
            }
        }

        public void Dispose()
        {
            // Requires window handle to unregister, handled by Unregister usually.
            // This is just a cleanup.
        }
    }
}
