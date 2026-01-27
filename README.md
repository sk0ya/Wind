# Wind - Windows Tab Manager

A modern Windows application that allows you to manage multiple windows in a tabbed interface, similar to browser tabs.

## Features

- **Tab Management**: Embed any Windows application into tabs
- **Tab Groups**: Organize tabs into color-coded groups
- **Session Management**: Save and restore your window sessions
- **Hotkey Support**: Quick keyboard shortcuts for tab navigation
- **Modern UI**: Windows 11 Fluent Design with Mica backdrop

## Requirements

- Windows 10/11
- .NET 8.0 Runtime

## Building

```bash
cd src/Wind
dotnet restore
dotnet build
```

## Running

```bash
dotnet run --project src/Wind
```

Or run the built executable:
```bash
src\Wind\bin\Debug\net8.0-windows\Wind.exe
```

## Usage

### Adding Windows
1. Click the "+ Add Window" button
2. Select a window from the list
3. The window will be embedded as a new tab

### Tab Navigation
- Click on tabs to switch between windows
- Use `Ctrl + Tab` for next tab
- Use `Ctrl + Shift + Tab` for previous tab
- Use `Ctrl + 1-9` to switch to specific tabs
- Use `Ctrl + W` to close current tab

### Tab Groups
1. Click the folder icon to create a new group
2. Drag tabs into groups to organize them
3. Groups are color-coded for easy identification

### Session Management
- Click "Save Session" to save current tab configuration
- Sessions are automatically restored on next launch
- Session data is stored in `%LOCALAPPDATA%\Wind\session.json`

## Architecture

```
Wind/
├── Interop/          # Win32 API interop
│   ├── NativeMethods.cs
│   └── WindowHost.cs
├── Models/           # Data models
│   ├── WindowInfo.cs
│   ├── TabItem.cs
│   ├── TabGroup.cs
│   └── SessionData.cs
├── Services/         # Business logic
│   ├── WindowManager.cs
│   ├── TabManager.cs
│   ├── SessionManager.cs
│   └── HotkeyManager.cs
├── ViewModels/       # MVVM ViewModels
│   └── MainViewModel.cs
└── Views/            # UI components
    ├── TabBar.xaml
    └── WindowPicker.xaml
```

## Technology Stack

- .NET 8.0 + WPF
- WPF-UI (Fluent Design)
- CommunityToolkit.Mvvm (MVVM framework)
- Microsoft.Extensions.DependencyInjection

## Known Limitations

- Some applications may not embed properly (e.g., elevated processes)
- UWP applications have limited support
- Some windows may lose functionality when embedded

## License

MIT License
