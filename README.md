# ReforgeX

ReforgeX is a Windows app for managing Arma Reforger mods. It reads the addon folders the game already uses, shows each mod's name and size, and lets you search, remove, and hand the addon files to someone else.

## What you can do

- See installed mods with their name, id, folder, and size.
- Search by name or id.
- Select more than one at a time. Use the checkbox, Shift-click for a range, the header box for the current search, or Ctrl+A.
- Remove the selection. The folders go to the Recycle Bin. Files that shipped with the game install are left alone.
- Turn mods off without deleting them. They move into a folder named like `addons-disabled` next to the addon folder, so the game does not load them. Turn on puts them back. Workshop mods and files that shipped with the game stay where they are.
- Filter the list: all mods, only ones the game loads, only ones turned off, or one source at a time.
- Copy a Steam launch line (`-addons` plus the selected ids) from the Ids button or the row menu.
- Open a mod's workshop page when it has an id.
- Share the selected addon folders as a zip, including each mod's `.gproj`.
- Import a zip from Share, or an addon folder, into an addon directory. You can also drop a zip or folder onto the window.
- Add another folder if your mods are not in the usual places.

ReforgeX looks in these locations when they exist:

- `Documents\My Games\ArmaReforger\addons`
- `Documents\My Games\ArmaReforger\profile\addons`
- `Documents\My Games\ArmaReforgerWorkbench\addons`
- Steam Workshop content for Arma Reforger (`1874880`)
- Paths from Steam launch options (`-addonDownloadDir`, `-addonsDir`)

## Download

The installer is on the [Releases](https://github.com/binx-ux/ReforgeX/releases) page. `ReforgeX-Setup.exe` installs for the current user and does not need administrator rights. It adds a Start menu shortcut and an Uninstall ReforgeX shortcut. You can also remove it from Apps & Features.

## Requirements

- Windows 10 or later
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build from source. The installer already includes the runtime.

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
