# Ego Engine Audio Editor

Edit F1 25 BDL engine bundles, preview paired waveforms and RPM tables, replace PCM sections, and export Simplified Launcher mods and editable .egoengineaudio projects.

## Requirements

- Windows x64.
- .NET 9 SDK for development. The published application includes its runtime.
- Git LFS when cloning or pushing the vendored binaries.

## Build

```powershell
git lfs install
git lfs pull
dotnet restore EgoEngineSoundEditor.csproj
dotnet build EgoEngineSoundEditor.csproj -c Release
```

Open `EgoEngineSoundEditor.csproj` directly in Visual Studio, or use the commands above. No Toolsuite checkout or sibling projects are required.

## Publish

```powershell
dotnet publish EgoEngineSoundEditor.csproj -p:PublishProfile=SingleExe
```

The publish folder is configured in `Properties/PublishProfiles/SingleExe.pubxml`. Distribute the executable together with its `license` folder. No separate .NET installation is required by users.

## Use

Open a game audio file or folder, select audio, export WAV for editing, then import a replacement. Use **Export to Mod** to create a Launcher-compatible ZIP and its companion project. Open the project again to continue editing.

Replacement WAVs must be mono PCM16 at 48,000 Hz with exactly the original sample count. Supports BDL versions 118 and 119. Preview does not simulate dynamic in-game engine playback.

Original game files are preserved by export. Game audio is not included. File-level checks do not replace in-game testing.

## Upload to GitHub

Create an empty GitHub repository, then run in this folder:

```powershell
git init
git lfs install
git add .
git commit -m "Initial standalone release"
git branch -M main
git remote add origin https://github.com/JonnieDsgns/Ego-Engine-Sound-Editor.git
git push -u origin main
```

Build output, user settings, and projects are ignored. Binary dependencies are tracked with Git LFS; GitHub may require LFS storage for those objects. Do not upload bin/, obj/, or publish/ as source.

## Third-party notices

Dependency license texts are provided in Distribution/ and, where applicable, vendor/. Publishing copies notices to license/ beside the executable. These notices cover dependencies; choose a project license separately before licensing your own source or artwork.
