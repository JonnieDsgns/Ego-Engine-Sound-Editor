# Ego Engine Sound Editor

Preview, export, and replace engine audio in **F1 25 BDL bundles**. Inspect paired waveforms and RPM tables, create mods for Simplified Launcher, and save editable projects.

## Features

- Open individual `.bdl` bundles or browse an audio folder.
- Filter bundles by filename or path.
- Preview individual engine audio sections.
- Inspect paired waveforms, sample-position markers, and RPM tables.
- Export sections as WAV and import validated replacements.
- Revert individual sections to their original audio.
- Export Launcher-compatible mod ZIPs with a mod name, creator, and version.
- Save and reopen `.egoengineaudio` projects.
- Highlight modified bundles and sections in **bold**.
- Preserve original game files during export.

## Installation

1. Download and extract a packaged Windows x64 release, if available.
2. Keep the `license` folder beside `EgoEngineSoundEditor.exe`.
3. Run `EgoEngineSoundEditor.exe`.

The single-file release includes the .NET runtime and required libraries. No separate runtime installation is required. The source checkout must be built before it can be run.

## How to Use

1. Choose **Open File / Project** to open a bundle, or **Open Audio Folder** to browse your engine audio folder, typically `F1 25/audio/2025/bundles`.
2. Select a bundle and section. Use **Play / Resume**, **Pause**, and **Stop** to preview it.
3. Select **Export WAV** and edit the audio in your preferred audio editor.
4. Save it as **uncompressed mono PCM16 WAV at 48,000 Hz**, preserving the **exact original sample count and duration**.
5. Select **Replace with WAV** to import your edited audio. Use **Revert Section** to restore the original samples.
6. Choose **Export to Mod**, enter the mod name, creator, and version, and save the ZIP.
7. Import the ZIP into **Simplified Launcher**.

Each mod export also creates a companion `.egoengineaudio` project. Use **Save Project** to save a project independently, and **Open File / Project** to reopen it. Projects contain the original bundle, replacements, mod details, and installation path.

If the installation path cannot be detected, select the intended destination folder inside the game's `audio` directory. The original BDL filename must be preserved.

## Waveform Controls

- **Click:** Preview audio from the selected position.
- **Mouse wheel:** Zoom the audio timeline.
- **Double-click:** Show the full audio duration.

RPM curves use their own table-index axis. Their exact relationship to the audio timeline has not been confirmed.

## Supported Audio and Limitations

- Supports BDL versions **118 and 119**.
- Replacements must be **mono PCM16 WAV at 48 kHz** with exactly the original sample count.
- Shorter or longer replacements are not supported. The editor does not automatically resample, trim, or extend audio.
- Only PCM audio regions are replaced; the remaining bundle data is preserved.
- Preview plays the recorded sample sequence rather than simulating dynamic in-game engine playback.
- The roles of individual engine sections have not all been confirmed.

Keep backups before installing mods. Exported bundles have been checked at file level; in-game behaviour has not yet been verified. Game audio and other game files are not included in this repository.

## Build from Source

Requirements: **Windows x64** and the **.NET 9 SDK**. Git LFS support is configured for any vendored binaries added to this repository.

```powershell
git clone https://github.com/JonnieDsgns/Ego-Engine-Sound-Editor.git
cd Ego-Engine-Sound-Editor
dotnet restore EgoEngineSoundEditor.csproj
dotnet build EgoEngineSoundEditor.csproj -c Release
```

Open `EgoEngineSoundEditor.csproj` directly in Visual Studio if you prefer. This is a standalone project; the AlterEgo Toolsuite is not required.

## Publish

```powershell
dotnet publish EgoEngineSoundEditor.csproj -p:PublishProfile=SingleExe
```

The profile in `Properties/PublishProfiles/SingleExe.pubxml` produces a self-contained Windows x64 executable under `publish/EgoEngineSoundEditor/`, with a separate `license/` folder.

The GitHub Actions workflow builds the release and uploads it as a workflow artifact. Build output, personal settings, and audio projects are excluded from Git.

## License

This project is licensed under the **GNU General Public License v3.0 only** (`GPL-3.0-only`). See [LICENSE](LICENSE) for the full terms.

You may use, study, modify, and redistribute the software. If you distribute modified versions or a combined work derived from this code, you must comply with GPLv3, including providing the corresponding source code under GPLv3 and retaining the required notices. Private modifications do not have to be published, and using this editor to create an audio mod does not by itself place that mod under the GPL.

Bundled third-party components retain their respective licenses. Their license texts and notices are included in `Distribution/` and, where applicable, `vendor/`. Published releases place the notices in `license/` beside the executable.
