using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NovaLite.Core.Settings;
using System.IO;
using NovaLite.UI.Themes;
using NovaLite.UI.ViewModels;

namespace NovaLite.UI;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();
    
    public static Core.Services.ConversationService Conversation { get; private set; } = null!;
    public static Core.Interfaces.IChatRepository ChatRepository { get; private set; } = null!;
    public static Core.AI.LocalInferenceProvider Provider { get; private set; } = null!;
    public static Engine.Loaders.GGUFLoader GgufLoader { get; private set; } = null!;
    public static Setup.SetupService SetupManager { get; private set; } = null!;

    public override void Initialize()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaLite");
        Directory.CreateDirectory(appData);
        var logPath = Path.Combine(appData, "startup.log");
        try
        {
            // Global exception and exit handlers to capture unexpected shutdowns
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try { File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] UnhandledException: {e.ExceptionObject}{Environment.NewLine}"); } catch {}
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                try { File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] UnobservedTaskException: {e.Exception}{Environment.NewLine}"); } catch {}
            };
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                try { File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] ProcessExit{Environment.NewLine}"); } catch {}
            };

            File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] Initialize start{Environment.NewLine}");

            AvaloniaXamlLoader.Load(this);
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] Loaded XAML{Environment.NewLine}");

            // Load persisted settings and apply theme immediately
            Settings = AppSettings.Load();
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] Loaded Settings (IsFirstRun={Settings.IsFirstRun}){Environment.NewLine}");
            ThemeManager.Apply(Settings.Theme);
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] Applied Theme{Environment.NewLine}");

        // Setup DI (Manual)
        GgufLoader = new Engine.Loaders.GGUFLoader(Microsoft.Extensions.Logging.Abstractions.NullLogger<Engine.Loaders.GGUFLoader>.Instance);
        Provider = new Core.AI.LocalInferenceProvider(GgufLoader, Microsoft.Extensions.Logging.Abstractions.NullLogger<Core.AI.LocalInferenceProvider>.Instance);
        
            // Setup Database — initialize schema in background so startup doesn't block the UI
            var dbContextFactory = new NovaLite.Database.Factories.NovaDbContextFactory();

            // Kick off creation asynchronously and don't block Initialize().
            // Any DB operations later will create contexts as-needed; failures are logged to disk.
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var context = dbContextFactory.CreateDbContext();
                    await context.Database.EnsureCreatedAsync();
                    File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] Database EnsureCreatedAsync completed{Environment.NewLine}");
                }
                catch (Exception ex)
                {
                    try
                    {
                        var msg = $"[{DateTime.UtcNow:O}] Database initialization failed: {ex}{Environment.NewLine}";
                        File.AppendAllText(logPath, msg);
                    }
                    catch
                    {
                        System.Diagnostics.Debug.WriteLine($"Database initialization failed and could not write log: {ex}");
                    }
                }
            });
        
        var chatRepo = new Core.Services.ChatRepository(dbContextFactory);
        ChatRepository = chatRepo;
        var memoryService = new Core.Services.MemoryExtractionService(dbContextFactory, Microsoft.Extensions.Logging.Abstractions.NullLogger<Core.Services.MemoryExtractionService>.Instance);
        
            Conversation = new Core.Services.ConversationService(
            Provider, 
            chatRepo, 
            memoryService, 
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Core.Services.ConversationService>.Instance);
        File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] ConversationService created{Environment.NewLine}");

        SetupManager = new Setup.SetupService(GgufLoader);
        File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] SetupService created{Environment.NewLine}");

            File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] Initialize complete{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] Initialize exception: {ex}{Environment.NewLine}"); } catch {}
            throw;
        }
    }
    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaLite");
            Directory.CreateDirectory(appData);
            var logPath = Path.Combine(appData, "startup.log");
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] OnFrameworkInitializationCompleted start{Environment.NewLine}");
        }
        catch {}

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Settings.IsFirstRun)
            {
                var vm = new SetupWindowViewModel();
                var setupWindow = new Views.SetupWindow { DataContext = vm };
                setupWindow.Closed += (s, e) =>
                {
                    // After setup window closes, if it's no longer first run (setup completed), open main window
                    if (!AppSettings.Load().IsFirstRun)
                    {
                        var mainVm = new MainWindowViewModel();
                        desktop.MainWindow = new MainWindow { DataContext = mainVm };
                        desktop.MainWindow.Show();
                        try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaLite", "startup.log"), $"[{DateTime.UtcNow:O}] MainWindow shown after setup{Environment.NewLine}"); } catch {}
                    }
                    else
                    {
                        // Setup was aborted
                        desktop.Shutdown();
                        try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaLite", "startup.log"), $"[{DateTime.UtcNow:O}] Setup aborted, shutting down{Environment.NewLine}"); } catch {}
                    }
                };
                desktop.MainWindow = setupWindow;
                try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaLite", "startup.log"), $"[{DateTime.UtcNow:O}] SetupWindow shown{Environment.NewLine}"); } catch {}
            }
            else
            {
                var vm = new MainWindowViewModel();
                desktop.MainWindow = new MainWindow { DataContext = vm };
                try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaLite", "startup.log"), $"[{DateTime.UtcNow:O}] MainWindow shown directly{Environment.NewLine}"); } catch {}
            }

            desktop.Exit += (_, _) => Settings.Save();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
