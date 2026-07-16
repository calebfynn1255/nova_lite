using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NovaLite.Database.Entities;

public partial class ChatSessionEntity : INotifyPropertyChanged
{
    public Guid Id { get; set; }
    
    private string _title = "New Chat";
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    private DateTime _updatedAt = DateTime.UtcNow;
    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set => SetProperty(ref _updatedAt, value);
    }
    
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    private bool _isEditing;
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public ICollection<ChatMessageEntity> Messages { get; set; } = new List<ChatMessageEntity>();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
