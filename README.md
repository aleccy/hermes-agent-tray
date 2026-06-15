# Hermes Agent Tray

**A Windows system tray application that enables multi-profile independent operation for Hermes Agent — different platform tokens, different user whitelists, different Gateway instances, one-click switching, zero interference.**

Built with WPF (.NET 8).

## Features

- **Multi-Profile Management** — Create, configure, and delete independent Gateway profiles, each with its own `.env` file
- **Gateway Start/Stop** — One-click start/stop for individual or all Gateway instances
- **Desktop & Dashboard** — Quick launch for the desktop client and web dashboard
- **Console** — Open a command-line terminal pre-configured with each profile's environment variables
- **Auto Start** — Supports Windows auto-start + selective Gateway auto-start per profile
- **Bilingual UI** — Switch between Chinese and English in settings
- **System Tray** — Minimize to tray and run in the background

## UI Preview

```
┌─────────────────────────────────────────────────────────────────────┐
│  Hermes Agent Tray                                                  │
│  [+ New Profile] [Start All GW] [Stop All] [Refresh] [Settings]    │
├─────────────────────────────────────────────────────────────────────┤
│  ● default    Running (PID 1234)  [Auto] [Stop GW] [Console] [Config]        │
│  ● mytest     Stopped             [Auto] [Start GW] [Console] [Config] [Del]  │
├─────────────────────────────────────────────────────────────────────┤
│  Desktop & Dashboard                                                │
│  ● Desktop    Running                                     [Stop]   │
│  ● Dashboard  http://127.0.0.1:9119                       [Stop] [Open] │
├─────────────────────────────────────────────────────────────────────┤
│  1/2 gateway(s) running                                             │
└─────────────────────────────────────────────────────────────────────┘
```

## Build

### Prerequisites

- .NET 8 SDK
- Windows 10/11 (x64)

### Debug Build

```bash
dotnet build
```

### Release Build

Using `build-release.bat`:

```bash
# Self-contained single file (~70MB, no .NET runtime required)
build-release.bat

# Framework-dependent single file (~10MB, requires .NET 8 runtime)
build-release.bat --fdd

# Show all options
build-release.bat --help
```

Or publish manually:

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -p:ReadyToRun=false \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ".\bin\Release\publish-sc\"
```

## Profile Mechanism

Each profile has its own directory structure and `.env` file:

```
%HERMES_HOME%/
├── .env                          # default profile environment variables
├── config.yaml
├── profiles/
│   └── mytest/
│       ├── .env                  # mytest environment variables
│       └── logs/                 # runtime logs
```

- **API keys** (e.g. `OPENAI_API_KEY`) are automatically inherited from the default profile
- **Platform tokens** (e.g. `WEIXIN_TOKEN`, `QQ_APP_ID`) are configured independently per profile, not inherited
- **User whitelists** (e.g. `WEIXIN_ALLOWED_USERS`) are inherited from default and can be overridden

## Settings

| Option | Description |
|--------|-------------|
| UI Language | Chinese / English (requires restart) |
| Auto Start on Boot | Register in Windows startup |
| Auto Start Gateway | Enable to show "Auto" checkbox per profile |

## Tech Stack

- **Framework**: WPF on .NET 8
- **Architecture**: MVVM (CommunityToolkit.Mvvm)
- **System Tray**: Hardcodet.NotifyIcon.Wpf
- **Database**: Microsoft.Data.Sqlite
- **Configuration**: YamlDotNet

## Author

alec_cy

## License

MIT
