using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLite.Core.Settings;
using NovaLite.UI.Themes;

namespace NovaLite.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly MainWindowViewModel _shell;
    private readonly AppSettings _settings;

    [ObservableProperty] private string _selectedTheme;
    [ObservableProperty] private string _modelDirectory;
    [ObservableProperty] private float _temperature;
    [ObservableProperty] private float _topP;
    [ObservableProperty] private int _maxTokens;
    [ObservableProperty] private int _contextLength;
    [ObservableProperty] private int _gpuLayers;

    public bool IsLightTheme    => SelectedTheme == "Light";
    public bool IsDarkTheme     => SelectedTheme == "Dark";
    public bool IsSystemTheme   => SelectedTheme == "System";
    public bool IsOledTheme     => SelectedTheme == "OledBlack";

    public SettingsViewModel(MainWindowViewModel shell)
    {
        _shell = shell;
        _settings = AppSettings.Load();

        _selectedTheme  = _settings.Theme;
        _modelDirectory = _settings.ModelDirectory;
        _temperature    = _settings.Temperature;
        _topP           = _settings.TopP;
        _maxTokens      = _settings.MaxTokens;
        _contextLength  = _settings.ContextLength;
        _gpuLayers      = _settings.GpuLayers;
    }

    partial void OnSelectedThemeChanged(string value)
    {
        ThemeManager.Apply(value);
        _settings.Theme = value;
        _settings.Save();
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsOledTheme));
    }

    [RelayCommand]
    private void SelectTheme(string theme)
    {
        SelectedTheme = theme;
    }

    [RelayCommand]
    private void Save()
    {
        _settings.Theme         = SelectedTheme;
        _settings.ModelDirectory = ModelDirectory;
        _settings.Temperature   = Temperature;
        _settings.TopP          = TopP;
        _settings.MaxTokens     = MaxTokens;
        _settings.ContextLength = ContextLength;
        _settings.GpuLayers     = GpuLayers;
        _settings.Save();
    }
}
