# ReforgeX

ReforgeX is a Windows app for managing Arma Reforger mods. It reads the addon folders the game already uses, shows each mod's name and size, and lets you search, remove, and hand the addon files to someone else.

## What you can do

- See installed mods with their name, id, folder, and size.
- Search by name or id.
- Select more than one at a time. Use the checkbox, Shift-click for a range, the header box for the current search, or Ctrl+A.
- Remove the selection. The folders go to the Recycle Bin. Files that shipped with the game install are left alone.
- Share the selected addon folders as a zip, including each mod's `.gproj`.
- Import a zip from Share, or an addon folder, into an addon directory. You can also drop a zip or folder onto the window.
- Add another folder if your mods are not in the usual places.

ReforgeX looks in these locations when they exist:

- `Documents\My Games\ArmaReforger\addons`
- `Documents\My Games\ArmaReforger\profile\addons`
- `Documents\My Games\ArmaReforgerWorkbench\addons`
- Steam Workshop content for Arma Reforger (`1874880`)
- Paths from Steam launch options (`-addonDownloadDir`, `-addonsDir`)

## Requirements

- Windows 10 or later
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build

## Build

```
dotnet build ReforgeX.sln -c Release
```

The program is written to `bin\Release\net8.0-windows\ReforgeX.exe`.

To run it from source:

```
dotnet run --project ReforgeX.csproj
```

## License

MIT. See [LICENSE](LICENSE).
