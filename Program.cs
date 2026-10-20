using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace EngineSoundTool;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var culture=System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture=culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture=culture;
        var app = new Application();
        app.Run(new MainWindow(args.FirstOrDefault()));
    }
}

public sealed partial class MainWindow : Window
{
    readonly MediaPlayer player = new();
    readonly string cache = Path.Combine(Path.GetTempPath(), "AlterEgoEngineAudio", Guid.NewGuid().ToString("N"));
    readonly List<Button> sectionButtons = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    List<string> files = new();
    BdlDocument? document;
    string? playingPath;
    string? selectedBundle;
    bool restoringSelection;
    bool paused;
    double? pendingSeek;
    Button saveButton = null!;
    readonly CancellationTokenSource lifetime = new();
    bool busy;
    long savedRevision=-1;
    EngineModDetails? modDetails;
    string? projectGamePath;
    Button projectButton=null!;
    bool closeRequested, committingFile;

    public MainWindow(string? initialPath)
    {
        InitializeComponent();
        PreviewKeyDown += (_,e)=>{if(busy)e.Handled=true;};
        titleBar.MouseLeftButtonDown += (_,e)=>
        {
            for(var source=e.OriginalSource as DependencyObject;source!=null;source=VisualTreeHelper.GetParent(source))
                if(source==brandLogo || source is System.Windows.Controls.Primitives.ButtonBase)return;
            if(e.ClickCount==2)WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
            else if(e.LeftButton==System.Windows.Input.MouseButtonState.Pressed)DragMove();
        };
        minimizeButton.Click+=(_,_)=>WindowState=WindowState.Minimized;
        maximizeButton.Click+=(_,_)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
        closeButton.Click+=(_,_)=>Close();

        toolbar.Children.Add(Button("Open File / Project…", OpenFile)); toolbar.Children.Add(Button("Open Audio Folder…", OpenFolder));
        saveButton = Button("Export to Mod…", Save); toolbar.Children.Add(saveButton);
        projectButton=Button("Save Project…",SaveProject);toolbar.Children.Add(projectButton);
        search.TextChanged += (_, _) => FilterFiles(); bundles.SelectionChanged += async (_, _) => await RunAsync(SelectBundle);
        transport.Children.Clear();
        AddSectionButton(transport, "▶ Play / Resume", Play); AddSectionButton(transport, "Pause", Pause); AddSectionButton(transport, "Stop", Stop);
        transport.Children.Add(playback);
        AddSectionButton(editing, "Export WAV…", Export); AddSectionButton(editing, "Replace with WAV…", Replace); AddSectionButton(editing, "Revert Section", Revert);
        pairs.SelectionChanged += (_,_) =>
        {
            int first=pairs.SelectedIndex*2;
            if(first>=0 && document!=null && first<document.Sections.Count &&
               (sections.SelectedItem is not BdlSection selected || selected.Index/2!=pairs.SelectedIndex))
                sections.SelectedIndex=first;
        };
        sections.SelectionChanged += (_, _) => { Stop(); Refresh(); };
        visualization.SeekRequested += async (index,seconds) => await RunAsync(async ()=>{sections.SelectedIndex=index;await Play();pendingSeek=seconds;});
        player.MediaOpened += (_,_)=>{if(pendingSeek is {} seconds){player.Position=TimeSpan.FromSeconds(seconds);pendingSeek=null;}};
        player.MediaFailed += (_, e) => { Stop(); Error(e.ErrorException); };
        player.MediaEnded += (_, _) => Stop();
        timer.Tick += (_, _) => { if (playingPath != null) { playback.Text = $"{player.Position:mm\\:ss} / {(player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.ToString(@"mm\:ss") : "…")}"; visualization.SetCursor(Selected.Index,player.Position.TotalSeconds); } };
        timer.Start();
        Closing += (_, e) =>
        {
            if (!MayDiscard()) { e.Cancel = true; return; }
            if (busy && committingFile) { closeRequested = true; e.Cancel = true; status.Text = "Finishing the save; the window will close afterwards."; return; }
            lifetime.Cancel(); player.Close(); timer.Stop(); if (!busy) CleanupCache();
        };
        Refresh(); status.Text = "Open a BDL file or the F1 25 audio folder. Original files are preserved.";
        if (!string.IsNullOrWhiteSpace(initialPath)) Loaded += async (_, _) => await RunAsync(() => OpenPath(initialPath));
    }
    private void Logo_Click(object sender,System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled=true;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName="https://www.patreon.com/cw/JonnieDesigns",
                UseShellExecute=true
            });
        }
        catch(Exception error){Error(error);}
    }
    Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Foreground = new SolidColorBrush(Color.FromRgb(236,236,236)), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 8) };
        button.Click += async (_, _) => await RunAsync(action); return button;
    }
    void AddSectionButton(Panel panel, string label, Func<Task> action) { var b = Button(label, action); panel.Children.Add(b); sectionButtons.Add(b); }
    Button Button(string text, Action action) => Button(text, () => { action(); return Task.CompletedTask; });
    void AddSectionButton(Panel panel, string label, Action action) => AddSectionButton(panel, label, () => { action(); return Task.CompletedTask; });
    void CleanupCache() { try { Directory.Delete(cache, true); } catch { } }
    async Task RunAsync(Func<Task> action)
    {
        if (busy || lifetime.IsCancellationRequested) return;
        busy = true;
        bundles.IsHitTestVisible = sections.IsHitTestVisible = pairs.IsHitTestVisible = false;
        Refresh();
        try { await action(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) { if (!lifetime.IsCancellationRequested) Error(e); }
        finally
        {
            busy = false;
            committingFile = false;
            if (lifetime.IsCancellationRequested) CleanupCache();
            else { bundles.IsHitTestVisible = sections.IsHitTestVisible = pairs.IsHitTestVisible = true; Refresh(); }
            if(closeRequested) { closeRequested=false; Close(); }
        }
    }
    void Error(Exception e) { status.Text = e.Message; MessageBox.Show(this, e.Message, "Engine Sounds", MessageBoxButton.OK, MessageBoxImage.Error); }
    bool MayDiscard() => document?.IsDirty != true || document.ChangeRevision==savedRevision || MessageBox.Show(this, "There are unsaved replacements. Discard changes?\nChoose No to keep the current bundle open and save your work.", "Unsaved Changes", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    async Task OpenFile()
    {
        var dialog = new OpenFileDialog { Filter = "Engine Audio / Projects|*.bdl;*.egoengineaudio|Engine Bundles (*.bdl)|*.bdl|Engine Audio Project|*.egoengineaudio", CheckFileExists = true };
        if (await StandaloneEditor.DirectoryHistoryDialogs.ShowAsync(dialog, this, "EngineAudio.OpenBundle", global: true) == true) await OpenPath(dialog.FileName);
    }
    async Task OpenFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Select an Audio or Bundle Folder" };
        if (await StandaloneEditor.DirectoryHistoryDialogs.ShowAsync(dialog, this, "EngineAudio.OpenFolder", global: true) == true) await OpenPath(dialog.FolderName);
    }
    async Task OpenPath(string path)
    {
        if (!MayDiscard()) return;
        if(Path.GetExtension(path).Equals(EngineProject.Extension,StringComparison.OrdinalIgnoreCase))
        {
            var project=await Task.Run(()=>EngineProject.Open(path),lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            Stop();document=project.Document;selectedBundle=document.SourcePath;
            projectGamePath=project.Info.GamePath;modDetails=project.Info.Mod;savedRevision=document.ChangeRevision;
            files=new(){document.SourcePath};search.Text="";FilterFiles();
            pairs.ItemsSource=Enumerable.Range(0,(document.Sections.Count+1)/2).Select(i=>$"Pair {i*2}/{i*2+1}").ToArray();pairs.SelectedIndex=-1;
            sections.ItemsSource=document.Sections;sections.SelectedIndex=0;Refresh();
            status.Text="Project opened: "+modDetails.Name+" · Modified sections are shown in bold.";
            return;
        }
        List<string> found = await Task.Run(() => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.bdl", SearchOption.AllDirectories).Select(file => { lifetime.Token.ThrowIfCancellationRequested(); return file; }).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList() : new List<string> { Path.GetFullPath(path) }, lifetime.Token);
        lifetime.Token.ThrowIfCancellationRequested();
        if (found.Count == 0) throw new InvalidDataException("No BDL files were found in the selected folder.");
        Stop(); document = null; selectedBundle = null; sections.ItemsSource = null;
        files = found; search.Text = ""; FilterFiles(); bundles.SelectedIndex = 0; await SelectBundle();
    }
    void FilterFiles()
    {
        restoringSelection = true;
        bundles.ItemsSource = files.Where(f => f.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).Select(f => new BundleItem(f)).ToList();
        bundles.SelectedItem = bundles.Items.Cast<BundleItem>().FirstOrDefault(b => b.Path == selectedBundle);
        restoringSelection = false;
    }
    async Task SelectBundle()
    {
        if (restoringSelection || bundles.SelectedItem is not BundleItem item || item.Path == selectedBundle) return;
        if (!MayDiscard()) { FilterFiles(); return; }
        {
            var opened = await Task.Run(() => new BdlDocument(item.Path), lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            Stop(); document = opened; selectedBundle = item.Path;savedRevision=-1;projectGamePath=null;modDetails=null;
            pairs.ItemsSource=Enumerable.Range(0,(document.Sections.Count+1)/2).Select(i=>$"Pair {i*2}/{i*2+1}").ToArray();pairs.SelectedIndex=-1;
            sections.ItemsSource = document.Sections; sections.SelectedIndex = 0; Refresh();
            status.Text = $"{document.SourcePath}\nVersion {document.Version} · {document.Sections.Count} sections";
        }
        if (selectedBundle != item.Path) FilterFiles();
    }
    BdlSection Selected => sections.SelectedItem as BdlSection ?? throw new InvalidOperationException("Please select a section.");
    void Refresh()
    {
        var selected = sections.SelectedItem as BdlSection;
        bool enabled = document != null && selected != null;
        foreach (var b in sectionButtons) b.IsEnabled = enabled && !busy;
        saveButton.IsEnabled = document != null && !busy;
        projectButton.IsEnabled = document != null && !busy;
        foreach(var item in bundles.Items.OfType<BundleItem>())item.IsModified=item.Path==selectedBundle && document?.IsDirty==true;
        // Property notifications update modified styles in place. Refreshing the
        // item collections during SelectionChanged can reset SelectedItem while
        // WPF is still completing the click, invalidating the selection below.
        if(enabled){pairs.SelectedIndex=selected!.Index/2;visualization.SetPair(document,selected.Index/2*2);}else visualization.SetPair(null,0);
        documentTitle.Text = document == null ? "Open a .bdl file" : Path.GetFileName(document.SourcePath) + (document.IsDirty ? " · Modified" : "");
        Title = "Ego Engine Audio Editor" + (document == null ? "" : $" · {Path.GetFileName(document.SourcePath)}{(document.IsDirty ? " *" : "")}");
        details.Text = enabled ? $"Section {selected!.Index:00}\n{selected.Seconds:F6} seconds · exactly {selected.SampleCount:N0} samples · Mono PCM16, 48,000 Hz\nRPM table {selected.RpmMin:F0}–{selected.RpmMax:F0} · {selected.GrainCount:N0} positions\n{(document!.IsReplaced(selected.Index) ? "WAV replacement pending – preview and export use the replacement." : "Original samples – no replacement for this section.")}\n{document.ChangeCount} replacement(s) in this bundle. Section roles have not yet been confirmed." : "";
    }
    async Task Play()
    {
        if (paused && playingPath != null) { player.Play(); paused = false; return; }
        Stop(); Directory.CreateDirectory(cache);
        playingPath = Path.Combine(cache, Guid.NewGuid().ToString("N") + ".wav");
        var doc = document!; int index = Selected.Index; string output = playingPath;
        byte[] wave = await Task.Run(() => PcmWave.Encode(doc.GetPcm(index)), lifetime.Token);
        await File.WriteAllBytesAsync(output, wave, lifetime.Token);
        lifetime.Token.ThrowIfCancellationRequested();
        player.Open(new Uri(playingPath)); player.Play();
    }
    void Pause() { if (playingPath != null) { player.Pause(); paused = true; } }
    void Stop()
    {
        player.Close(); paused = false; playback.Text = "";pendingSeek=null;visualization.SetCursor(-1,0);
        if (playingPath != null) { try { File.Delete(playingPath); } catch { } playingPath = null; }
    }
    async Task Export()
    {
        var dialog = new SaveFileDialog { Filter = "PCM-WAV (*.wav)|*.wav", FileName = Path.GetFileNameWithoutExtension(document!.SourcePath) + $"_part_{Selected.Index:00}.wav", OverwritePrompt = true };
        if (await StandaloneEditor.DirectoryHistoryDialogs.ShowAsync(dialog, this, "EngineAudio.ExportWav", global: true) != true) return;
        if (!string.Equals(Path.GetExtension(dialog.FileName), ".wav", StringComparison.OrdinalIgnoreCase)) throw new IOException("WAV export requires a filename ending in .wav.");
        committingFile = true;
        var doc = document!; int index = Selected.Index;
        byte[] wave = await Task.Run(() => PcmWave.Encode(doc.GetPcm(index)), lifetime.Token);
        await File.WriteAllBytesAsync(dialog.FileName, wave, lifetime.Token);
        lifetime.Token.ThrowIfCancellationRequested(); status.Text = "WAV exported: " + dialog.FileName;
    }
    async Task Replace()
    {
        var dialog = new OpenFileDialog { Filter = "PCM-WAV (*.wav)|*.wav", CheckFileExists = true };
        if (await StandaloneEditor.DirectoryHistoryDialogs.ShowAsync(dialog, this, "EngineAudio.ImportWav", global: true) != true) return;
        var doc = document!; int index = Selected.Index;
        byte[] wave = await File.ReadAllBytesAsync(dialog.FileName, lifetime.Token);
        await Task.Run(() => doc.Replace(index, wave), lifetime.Token);
        lifetime.Token.ThrowIfCancellationRequested(); Stop(); Refresh();
        status.Text = "Replacement validated and queued. Use Export to Mod to create a mod and project.";
    }
    void Revert() { document!.Revert(Selected.Index); Stop(); Refresh(); status.Text = "Section reverted to its original samples."; }
    EngineModDetails? AskModDetails()
    {
        var dialog=new EngineModDialog(this,modDetails??new EngineModDetails(Path.GetFileNameWithoutExtension(document!.SourcePath)+" Engine Mod","","1.0"));
        if(dialog.ShowDialog()!=true)return null;
        return modDetails=dialog.Details;
    }
    async Task<string?> GetGamePath()
    {
        string? path=projectGamePath??EngineProject.FindGamePath(document!.SourcePath);
        if(path==null)
        {
            var dialog=new OpenFolderDialog{Title="Select the BDL Destination Folder Inside F1 25/audio"};
            if(await StandaloneEditor.DirectoryHistoryDialogs.ShowAsync(dialog,this,"EngineAudio.ModTarget",global:true)!=true)return null;
            path=EngineProject.FindGamePath(Path.Combine(dialog.FolderName,Path.GetFileName(document!.SourcePath)));
            if(path==null)throw new InvalidDataException("Please select a destination folder inside audio.");
        }
        return projectGamePath=EngineProject.ValidatePath(path);
    }
    static string SafeName(string name)=>string.Concat(name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));
    async Task Save()
    {
        var details=AskModDetails();if(details==null)return;
        string? gamePath=await GetGamePath();if(gamePath==null)return;
        var dialog=new SaveFileDialog{Title="Export to Mod",Filter="Simplified Launcher Mod (*.zip)|*.zip",DefaultExt=".zip",FileName=SafeName(details.Name)+".zip",OverwritePrompt=false};
        if(await StandaloneEditor.DirectoryHistoryDialogs.ShowAsync(dialog,this,"EngineAudio.ExportMod",global:true)!=true)return;
        committingFile=true;var doc=document!;
        await Task.Run(()=>EngineProject.ExportMod(doc,dialog.FileName,gamePath,details),lifetime.Token);
        savedRevision=doc.ChangeRevision;
        status.Text="Mod exported: "+dialog.FileName+" · Project: "+Path.ChangeExtension(dialog.FileName,EngineProject.Extension);
    }
    async Task SaveProject()
    {
        var details=AskModDetails();if(details==null)return;
        string? gamePath=await GetGamePath();if(gamePath==null)return;
        var dialog=new SaveFileDialog{Title="Save Engine Audio Project",Filter="Engine Audio Project|*.egoengineaudio",DefaultExt=EngineProject.Extension,FileName=SafeName(details.Name)+EngineProject.Extension,OverwritePrompt=false};
        if(await StandaloneEditor.DirectoryHistoryDialogs.ShowAsync(dialog,this,"EngineAudio.SaveProject",global:true)!=true)return;
        committingFile=true;var doc=document!;
        await Task.Run(()=>EngineProject.Save(doc,dialog.FileName,gamePath,details),lifetime.Token);
        savedRevision=doc.ChangeRevision;status.Text="Project saved: "+dialog.FileName;
    }
    sealed class BundleItem(string path) : INotifyPropertyChanged
    {
        public string Path { get; } = path;
        private bool isModified;
        public bool IsModified
        {
            get => isModified;
            set
            {
                if(isModified==value)return;
                isModified=value;
                PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(IsModified)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        public override string ToString() => System.IO.Path.GetFileName(Path);
    }
}
