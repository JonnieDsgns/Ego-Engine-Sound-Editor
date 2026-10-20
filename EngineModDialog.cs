using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EngineSoundTool;

public sealed class EngineModDialog : Window
{
    public EngineModDetails Details { get; private set; }
    public EngineModDialog(Window owner,EngineModDetails initial)
    {
#pragma warning disable WPF0001 // Match the Fluent dark theme used by MainWindow.xaml.
        ThemeMode=ThemeMode.Dark;
#pragma warning restore WPF0001
        Details=initial;
        Owner=owner;Title="Export to Mod";Width=440;SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;
        ResizeMode =ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(31,31,31));Foreground=Brushes.White;
        FontFamily=new FontFamily("Segoe UI");FontSize=14;
        var panel=new StackPanel{Margin=new Thickness(24)};
        Content=new Border{Background=Background,BorderBrush=new SolidColorBrush(Color.FromRgb(60,60,60)),BorderThickness=new Thickness(1),Child=panel};
        panel.Children.Add(new TextBlock{Text="Export to Mod",FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,20)});
        TextBox Field(string label,string value)
        {
            panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,0,0,6)});
            var input=new TextBox{Text=value,Margin=new Thickness(0,0,0,16),Padding=new Thickness(10,7,10,7),MinHeight=34,Background=new SolidColorBrush(Color.FromRgb(43,43,43)),Foreground=Brushes.White,CaretBrush=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(80,80,80))};
            panel.Children.Add(input);return input;
        }
        var name=Field("Mod name",initial.Name);
        Loaded+=(_,_)=>{name.Focus();name.SelectAll();};
        var author=Field("Creator",initial.Author);
        var version=Field("Version",initial.Version);
        panel.Children.Add(new TextBlock{Text="Creates a Launcher ZIP and an editable .egoengineaudio project.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
        var cancel=new Button{Content="Cancel",Background=new SolidColorBrush(Color.FromRgb(43,43,43)),BorderBrush=new SolidColorBrush(Color.FromRgb(80,80,80)),IsCancel=true,Margin=new Thickness(0,0,8,0),Padding=new Thickness(16,7,16,7),Foreground=Brushes.White};buttons.Children.Add(cancel);
        var export=new Button{Content="Continue…",IsDefault=true,Padding=new Thickness(16,7,16,7),Background=new SolidColorBrush(Color.FromRgb(184,255,22)),Foreground=new SolidColorBrush(Color.FromRgb(16,16,16))};buttons.Children.Add(export);
        export.Click+=(_,_)=>
        {
            if(string.IsNullOrWhiteSpace(name.Text)||string.IsNullOrWhiteSpace(author.Text)||string.IsNullOrWhiteSpace(version.Text))
            {MessageBox.Show(this,"Enter a mod name, creator, and version.",Title);return;}
            Details=new(name.Text.Trim(),author.Text.Trim(),version.Text.Trim());DialogResult=true;
        };
    }
}
