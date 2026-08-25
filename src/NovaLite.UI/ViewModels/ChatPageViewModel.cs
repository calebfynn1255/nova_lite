using System.Threading.Tasks;
using System.Threading;
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using NovaLite.Core.Models;
using NovaLite.Database.Entities;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using System.Text;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using UglyToad.PdfPig;

namespace NovaLite.UI.ViewModels;

public partial class ChatPageViewModel : ObservableObject
{
    private readonly NovaLite.Core.Services.ConversationService _conversationService;
    private readonly NovaLite.Core.Interfaces.IChatRepository _chatRepo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkspaceLinkVisible))]
    private string _activeModelName = "No model loaded";
    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private bool _hasMessages;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFileAttached))]
    private string? _attachedFileName;

    [ObservableProperty] private string? _attachedFilePath;
    [ObservableProperty] private string? _attachedFileContent;
    [ObservableProperty] private string? _attachedFileSizeDisplay;
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLinkedWorkspace))]
    private string? _linkedWorkspacePath;

    public bool IsFileAttached => !string.IsNullOrEmpty(AttachedFileName);
    public bool HasLinkedWorkspace => !string.IsNullOrEmpty(LinkedWorkspacePath);

    /// <summary>
    /// Always true as project context linking is available for all models.
    /// </summary>
    public bool IsWorkspaceLinkVisible => true;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];
    public ObservableCollection<ChatSessionEntity> ChatSessions { get; } = [];

    [ObservableProperty] private ChatSessionEntity? _selectedSession;

    public event Action? ChatUpdated;

    private CancellationTokenSource? _cts;
    private bool _sendInProgress;

    public ChatPageViewModel(NovaLite.Core.Services.ConversationService conversationService)
    {
        _conversationService = conversationService;
        _chatRepo = App.ChatRepository;
    }

    public bool IsPcAccessEnabled
    {
        get => App.Conversation?.IsPcAccessEnabled ?? false;
        set
        {
            try { App.Conversation.SetPcAccessEnabled(value); } catch { }
            OnPropertyChanged(nameof(IsPcAccessEnabled));
        }
    }

    [RelayCommand]
    private void ClearAttachment()
    {
        AttachedFileName = null;
        AttachedFilePath = null;
        AttachedFileContent = null;
        AttachedFileSizeDisplay = null;
    }

    [RelayCommand]
    private void ClearError()
    {
        ErrorMessage = null;
    }

    [RelayCommand]
    private void ClearWorkspace()
    {
        LinkedWorkspacePath = null;
        try { App.Conversation?.SetWorkspace(null); } catch {}
    }

    public void SetWorkspace(string path)
    {
        LinkedWorkspacePath = path;
        try
        {
            App.Conversation?.SetWorkspace(path);
            // Linking a project only supplies optional read context. It must never
            // grant file or terminal control; the user enables PC Access separately.
        }
        catch {}
    }

    public async Task AttachFileAsync(string path, string fileName)
    {
        if (!System.IO.File.Exists(path))
        {
            ErrorMessage = "File does not exist.";
            return;
        }

        try
        {
            var fileInfo = new System.IO.FileInfo(path);
            if (fileInfo.Length > 25 * 1024 * 1024) // 25MB limit
            {
                ErrorMessage = "File is too large. Maximum size is 25MB.";
                return;
            }
            
            ErrorMessage = "Analyzing attachment, please wait...";

            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            string content;

            if (ext == ".pdf")
            {
                content = await ExtractTextFromPdfAsync(path);
            }
            else if (ext == ".docx")
            {
                content = await Task.Run(() => ExtractTextFromDocx(path));
            }
            else if (ext == ".xlsx" || ext == ".xlxs")
            {
                content = await Task.Run(() => ExtractTextFromXlsx(path));
            }
            else if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif" || ext == ".webp" || ext == ".tiff")
            {
                content = await NovaLite.UI.Services.ImageAnalysisService.AnalyzeImageAsync(path);
            }
            else if (IsTextFile(path))
            {
                content = await System.IO.File.ReadAllTextAsync(path);
            }
            else
            {
                ErrorMessage = "Unsupported file type. Supported: text files, PDF, DOCX, XLSX, images (JPG, PNG, BMP, GIF, WEBP, TIFF).";
                return;
            }

            if (string.IsNullOrWhiteSpace(content) || content.Trim() == "[No text content could be extracted from this file]")
            {
                content = $"[Notice: The file '{fileName}' was attached, but no readable text or image OCR could be extracted. Please ensure the document contains unencrypted text or readable scans.]";
            }

            AttachedFilePath = path;
            AttachedFileName = fileName;
            AttachedFileContent = content;
            AttachedFileSizeDisplay = FormatFileSize(fileInfo.Length);
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to read file: {ex.Message}";
        }
    }

    private static async Task<string> ExtractTextFromPdfAsync(string path)
    {
        var sb = new StringBuilder();
        try
        {
            using (var document = PdfDocument.Open(path))
            {
                int pageNum = 1;
                foreach (var page in document.GetPages())
                {
                    var text = page.Text?.Trim();
                    sb.AppendLine($"--- Page {pageNum} ---");

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.AppendLine(text);
                    }
                    else
                    {
                        // Fallback for scanned/image PDFs: Extract page images and run Windows OCR
                        var ocrSb = new StringBuilder();
                        try
                        {
                            var images = page.GetImages();
                            foreach (var img in images)
                            {
                                byte[]? imageBytes = null;

                                // Try to get a standard PNG first (PdfPig converts from PDF format)
                                if (img.TryGetPng(out var pngBytes))
                                {
                                    imageBytes = pngBytes;
                                }
                                else
                                {
                                    // Fallback: RawBytes is a valid JPEG for embedded JPEG images
                                    var raw = img.RawBytes;
                                    if (raw.Length > 0)
                                        imageBytes = raw.ToArray();
                                }

                                if (imageBytes != null && imageBytes.Length > 0)
                                {
                                    string ocrText = await ExtractOcrFromBytesAsync(imageBytes);
                                    if (!string.IsNullOrWhiteSpace(ocrText))
                                    {
                                        ocrSb.AppendLine(ocrText);
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Ignore image conversion exceptions for corrupt sub-images
                        }

                        if (ocrSb.Length > 0)
                        {
                            sb.AppendLine("[OCR Extracted Text from Page Image]:");
                            sb.AppendLine(ocrSb.ToString().Trim());
                        }
                        else
                        {
                            sb.AppendLine("[No text layer found on this page]");
                        }
                    }
                    pageNum++;
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[PDF Extraction Note: {ex.Message}]");
        }

        return sb.ToString().Trim();
    }

    private static async Task<string> ExtractOcrFromBytesAsync(byte[] bytes)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            var ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (ocrEngine == null) return string.Empty;

            var result = await ocrEngine.RecognizeAsync(softwareBitmap);
            return result?.Text ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractTextFromDocx(string path)
    {
        using (var archive = ZipFile.OpenRead(path))
        {
            var entry = archive.GetEntry("word/document.xml");
            if (entry == null) return string.Empty;

            using (var stream = entry.Open())
            {
                var doc = XDocument.Load(stream);
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                var sb = new StringBuilder();

                foreach (var paragraph in doc.Descendants(w + "p"))
                {
                    var text = string.Concat(paragraph.Descendants(w + "t").Select(t => t.Value));
                    if (!string.IsNullOrEmpty(text))
                    {
                        sb.AppendLine(text);
                    }
                }
                return sb.ToString();
            }
        }
    }

    private static string ExtractTextFromXlsx(string path)
    {
        using (var archive = ZipFile.OpenRead(path))
        {
            var sharedStrings = new List<string>();
            var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
            if (sharedStringsEntry != null)
            {
                using (var stream = sharedStringsEntry.Open())
                {
                    var doc = XDocument.Load(stream);
                    XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    sharedStrings.AddRange(doc.Descendants(s + "t").Select(t => t.Value));
                }
            }

            var sb = new StringBuilder();
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var worksheetEntries = archive.Entries
                .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName);

            int sheetNum = 1;
            foreach (var sheetEntry in worksheetEntries)
            {
                sb.AppendLine($"--- Sheet {sheetNum++} ---");
                using (var stream = sheetEntry.Open())
                {
                    var doc = XDocument.Load(stream);
                    var rows = doc.Descendants(ns + "row").OrderBy(r => (int?)r.Attribute("r") ?? 0);
                    foreach (var row in rows)
                    {
                        var cells = row.Descendants(ns + "c");
                        var rowValues = new List<string>();
                        foreach (var cell in cells)
                        {
                            var valEl = cell.Element(ns + "v");
                            if (valEl == null) continue;
                            var val = valEl.Value;
                            var typeAttr = cell.Attribute("t");
                            if (typeAttr != null && typeAttr.Value == "s")
                            {
                                if (int.TryParse(val, out int idx) && idx >= 0 && idx < sharedStrings.Count)
                                {
                                    val = sharedStrings[idx];
                                }
                            }
                            rowValues.Add(val);
                        }
                        if (rowValues.Count > 0)
                        {
                            sb.AppendLine(string.Join("\t", rowValues));
                        }
                    }
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }

    private static bool IsTextFile(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        var textExtensions = new System.Collections.Generic.HashSet<string>
        {
            ".txt", ".md", ".cs", ".json", ".xml", ".yaml", ".yml", ".csv",
            ".py", ".js", ".ts", ".html", ".css", ".ini", ".cfg", ".log",
            ".bat", ".sh", ".csproj", ".sln", ".sql", ".rs", ".go",
            ".cpp", ".h", ".c", ".java", ".kt", ".swift", ".fs", ".axaml", ".xaml"
        };
        if (textExtensions.Contains(ext)) return true;

        try
        {
            using var stream = System.IO.File.OpenRead(path);
            byte[] buffer = new byte[1024];
            int read = stream.Read(buffer, 0, buffer.Length);
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == 0) return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
    
    public void SyncSessions(System.Collections.Generic.IEnumerable<ChatSessionEntity> sessions)
    {
        ChatSessions.Clear();
        foreach (var s in sessions)
            ChatSessions.Add(s);
    }

    public async Task LoadSessionAsync(Guid sessionId)
    {
        await _conversationService.SetActiveSessionAsync(sessionId);
        
        var sessionMessages = _conversationService.Session.Messages.ToList();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Merge only when we are currently sending or when UI has streaming messages
            var uiHasStreaming = Messages.Any(m => m.IsStreaming);
            if (!_sendInProgress && !uiHasStreaming)
            {
                // Safe to replace UI with the persisted session entirely
                Messages.Clear();
                DateTime? lastDate = null;
                foreach (var msg in sessionMessages)
                {
                    if (lastDate == null || lastDate.Value.Date != msg.Timestamp.Date)
                    {
                        Messages.Add(new ChatMessageViewModel
                        {
                            IsSeparator = true,
                            SeparatorText = msg.Timestamp.ToString("d MMMM yyyy"),
                            Timestamp = msg.Timestamp
                        });
                    }
                    lastDate = msg.Timestamp;

                    Messages.Add(new ChatMessageViewModel
                    {
                        Content = msg.Content,
                        IsUser = msg.Role == ChatRole.User,
                        Timestamp = msg.Timestamp,
                        AttachedFileName = msg.AttachedFileName,
                        AttachedFileSizeDisplay = msg.AttachedFileSizeDisplay,
                        AttachedFileContent = msg.AttachedFileContent
                    });
                }
            }
            else
            {
                // Merge new messages only (preserve in-flight UI state)
                DateTime lastUiNonSeparatorTimestamp = Messages.LastOrDefault(m => !m.IsSeparator)?.Timestamp ?? DateTime.MinValue;
                DateTime? lastDate = Messages.LastOrDefault()?.Timestamp;
                foreach (var msg in sessionMessages)
                {
                    if (msg.Timestamp <= lastUiNonSeparatorTimestamp) continue; // already present or older

                    if (lastDate == null || lastDate.Value.Date != msg.Timestamp.Date)
                    {
                        Messages.Add(new ChatMessageViewModel
                        {
                            IsSeparator = true,
                            SeparatorText = msg.Timestamp.ToString("d MMMM yyyy"),
                            Timestamp = msg.Timestamp
                        });
                    }
                    lastDate = msg.Timestamp;

                    Messages.Add(new ChatMessageViewModel
                    {
                        Content = msg.Content,
                        IsUser = msg.Role == ChatRole.User,
                        Timestamp = msg.Timestamp,
                        AttachedFileName = msg.AttachedFileName,
                        AttachedFileSizeDisplay = msg.AttachedFileSizeDisplay,
                        AttachedFileContent = msg.AttachedFileContent
                    });
                }
            }

            HasMessages = Messages.Any();
        });
    }

    public async Task<Guid> StartNewChatAsync()
    {
        var newId = await _conversationService.StartNewSessionAsync("New Chat");
        ClearMessages();
        return newId;
    }

    public void ClearMessages()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Messages.Clear();
            HasMessages = false;
        });
    }

    partial void OnInputTextChanged(string value)
    {
        SendCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSend), FlowExceptionsToTaskScheduler = false)]
    private async Task Send()
    {
        if (_sendInProgress) return;
        _sendInProgress = true;
        if (string.IsNullOrWhiteSpace(InputText)) return;

        var userText = InputText.Trim();
        InputText = string.Empty;

        var now = DateTime.Now;
        var lastMsg = Messages.LastOrDefault(m => !m.IsSeparator);
        if (lastMsg == null || lastMsg.Timestamp.Date != now.Date)
        {
            Messages.Add(new ChatMessageViewModel
            {
                IsSeparator = true,
                SeparatorText = now.ToString("d MMMM yyyy"),
                Timestamp = now
            });
        }

        var fileName = AttachedFileName;
        var fileSize = AttachedFileSizeDisplay;
        var fileContent = AttachedFileContent;

        ClearAttachment();

        // Add user message immediately so the UI feels responsive — avoid duplicates
        var lastUiMsg = Messages.LastOrDefault(m => !m.IsSeparator);
        if (!(lastUiMsg != null && lastUiMsg.IsUser && string.Equals(lastUiMsg.Content, userText, StringComparison.Ordinal) && (now - lastUiMsg.Timestamp).TotalSeconds < 3))
        {
            Messages.Add(new ChatMessageViewModel
            {
                Content = userText,
                IsUser = true,
                Timestamp = now,
                AttachedFileName = fileName,
                AttachedFileSizeDisplay = fileSize,
                AttachedFileContent = fileContent
            });
        }
        HasMessages = true;
        ChatUpdated?.Invoke(); // scroll to bottom

        // IsGenerating shows the TypingIndicator — no blank bubble until first token
        IsGenerating = true;
        _cts = new CancellationTokenSource();

        ChatMessageViewModel? aiMsg = null;

        try
        {
            // Yield once to let Avalonia render the user message + typing indicator
            // before the heavy CPU work (tokenization/decode) begins on the background thread.
            await Task.Yield();

            await Task.Run(async () =>
            {
                await foreach (var token in _conversationService.SendAsync(userText, fileName, fileSize, fileContent, null, _cts.Token))
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (aiMsg == null)
                        {
                            // First token — swap typing indicator for real bubble.
                            // If there's an existing empty assistant message loaded from DB,
                            // reuse it to avoid duplicate assistant bubbles.
                            var lastView = Messages.LastOrDefault(m => !m.IsSeparator);
                            if (lastView != null && !lastView.IsUser && string.IsNullOrEmpty(lastView.Content))
                            {
                                aiMsg = lastView;
                                aiMsg.Content = token;
                                aiMsg.IsStreaming = true;
                                // Do not assign to init-only `Timestamp` after construction
                                IsGenerating = false;
                            }
                            else
                            {
                                aiMsg = new ChatMessageViewModel
                                {
                                    Content = token,
                                    IsUser = false,
                                    IsStreaming = true,
                                    Timestamp = DateTime.Now
                                };
                                Messages.Add(aiMsg);
                                IsGenerating = false;
                            }
                        }
                        else
                        {
                            aiMsg.Content += token;
                        }

                        ChatUpdated?.Invoke();
                    });
                }
            });

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (aiMsg != null)
                    aiMsg.IsStreaming = false;
                else
                {
                    Messages.Add(new ChatMessageViewModel
                    {
                        Content = "*(no response)*",
                        IsUser = false,
                        Timestamp = DateTime.Now
                    });
                }
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No model loaded"))
        {
            Messages.Add(new ChatMessageViewModel
            {
                Content = "> ⚠️ No model loaded. Go to **Models** to load a GGUF file.",
                IsUser = false,
                Timestamp = DateTime.Now
            });
        }
        catch (OperationCanceledException)
        {
            if (aiMsg != null)
            {
                aiMsg.Content += " *(cancelled)*";
                aiMsg.IsStreaming = false;
            }
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessageViewModel
            {
                Content = $"> ❌ Error: {ex.Message}",
                IsUser = false,
                Timestamp = DateTime.Now
            });
            Serilog.Log.Error(ex, "Chat inference failed");
        }
        finally
        {
            IsGenerating = false;
            if (aiMsg != null) aiMsg.IsStreaming = false;
            _cts?.Dispose();
            _cts = null;
            ChatUpdated?.Invoke();
            _sendInProgress = false;
        }
    }

    private bool CanSend() => !string.IsNullOrWhiteSpace(InputText) && !IsGenerating;

    [RelayCommand]
    private void StopGeneration()
    {
        _cts?.Cancel();
    }
}

public partial class ChatMessageViewModel : ObservableObject
{
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private bool _isStreaming;
    public bool IsUser { get; init; }
    public DateTime Timestamp { get; init; }
    public string AuthorLabel => IsUser ? "You" : "Nova";

    public bool IsSeparator { get; init; }
    public bool IsUserMessage => IsUser && !IsSeparator;
    public bool IsAiMessage => !IsUser && !IsSeparator;
    public string SeparatorText { get; init; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAttachment))]
    private string? _attachedFileName;

    [ObservableProperty] private string? _attachedFileSizeDisplay;
    [ObservableProperty] private string? _attachedFileContent;

    public bool HasAttachment => !string.IsNullOrEmpty(AttachedFileName);

    [RelayCommand]
    private async Task CopyText()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(Content);
        }
    }
}
