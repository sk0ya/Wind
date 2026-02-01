using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Wind.Interop;
using Wind.Models;

namespace Wind.Services;

public class HotkeyManager : IDisposable
{
    private IntPtr _windowHandle;
    private HwndSource? _hwndSource;
    private int _nextHotkeyId = 1;
    private readonly Dictionary<int, HotkeyBinding> _registeredHotkeys = new();
    private bool _disposed;

    public ObservableCollection<HotkeyBinding> Hotkeys { get; } = new();

    public event EventHandler<HotkeyBinding>? HotkeyPressed;

    public void Initialize(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.Handle;

        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WndProc);

        RegisterDefaultHotkeys();
    }

    private void RegisterDefaultHotkeys()
    {
        // Default hotkeys - can be customized
        RegisterHotkey("Next Tab", System.Windows.Input.ModifierKeys.Control, Key.Tab, HotkeyAction.NextTab);
        RegisterHotkey("Previous Tab", System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, Key.Tab, HotkeyAction.PreviousTab);
        RegisterHotkey("Close Tab", System.Windows.Input.ModifierKeys.Control, Key.W, HotkeyAction.CloseTab);

        RegisterHotkey("Command Palette", System.Windows.Input.ModifierKeys.Alt, Key.P, HotkeyAction.CommandPalette);

        // Tab switching 1-9
        for (int i = 1; i <= 9; i++)
        {
            var action = (HotkeyAction)(HotkeyAction.SwitchToTab1 + i - 1);
            var key = (Key)(Key.D1 + i - 1);
            RegisterHotkey($"Switch to Tab {i}", System.Windows.Input.ModifierKeys.Control, key, action);
        }
    }

    public bool RegisterHotkey(string name, System.Windows.Input.ModifierKeys modifiers, Key key, HotkeyAction action, string? parameter = null)
    {
        var binding = new HotkeyBinding
        {
            Id = _nextHotkeyId++,
            Name = name,
            Modifiers = modifiers,
            Key = key,
            Action = action,
            Parameter = parameter
        };

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        uint mods = ConvertModifiers(modifiers);

        if (NativeMethods.RegisterHotKey(_windowHandle, binding.Id, mods, vk))
        {
            _registeredHotkeys[binding.Id] = binding;
            Hotkeys.Add(binding);
            return true;
        }

        return false;
    }

    private uint ConvertModifiers(System.Windows.Input.ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) result |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) result |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) result |= NativeMethods.MOD_SHIFT;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows)) result |= NativeMethods.MOD_WIN;
        return result;
    }

    public void UnregisterHotkey(HotkeyBinding binding)
    {
        if (_registeredHotkeys.ContainsKey(binding.Id))
        {
            NativeMethods.UnregisterHotKey(_windowHandle, binding.Id);
            _registeredHotkeys.Remove(binding.Id);
            Hotkeys.Remove(binding);
        }
    }

    public void UnregisterAllHotkeys()
    {
        foreach (var binding in _registeredHotkeys.Values.ToList())
        {
            NativeMethods.UnregisterHotKey(_windowHandle, binding.Id);
        }
        _registeredHotkeys.Clear();
        Hotkeys.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_registeredHotkeys.TryGetValue(id, out var binding))
            {
                HotkeyPressed?.Invoke(this, binding);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;

        UnregisterAllHotkeys();
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource?.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
