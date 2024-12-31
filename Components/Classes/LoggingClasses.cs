using System.ComponentModel.DataAnnotations.Schema;
using System.DirectoryServices;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using nump.Components.Classes;

public class UserUpdateLog
{
    [Key]
    public int LogItemId {get; set;}    
    [Required]
    public DateTime DateTime {get; set;}    
    [Required]
    public Guid RunId {get; set;}    
    [Required]
    public string? UserName {get; set;}    

    public Guid? UserGuid {get; set;}    
    [Required]
    public string? UpdatedAttribute {get; set;}    
    [Required]
    public string? OldValue {get; set;}    
    [Required]
    public string? NewValue {get; set;}
    public string? CsvUserObject {get; set;}


    [NotMapped]
    public Dictionary<string, object>? _csvObject
    {
        get => CsvUserObject != null ? JsonSerializer.Deserialize<Dictionary<string, object>>(CsvUserObject) : null;
        set => CsvUserObject = JsonSerializer.Serialize(value);
    }
}

public class UserCreationLog
{
    [Key]
    public int? LogItemId {get; set;}    
    [Required]
    public DateTime? DateTime {get; set;}    
    [Required]
    public Guid ?RunId {get; set;}    
    [Required]
    public string? UserName {get; set;}

    public Guid? UserGuid {get; set;}    
    [Required]
    public string? Result {get; set;}    
    [Required]
    public string? Reason {get; set;}    
    [Required]
    public string? csvUserObject {get; set;}
    public string? CreationDN {get; set;}
    [NotMapped]
    public Dictionary<string, object>? _csvObject
    {
        get => csvUserObject != null ? JsonSerializer.Deserialize<Dictionary<string, object>>(csvUserObject) : null;
        set => csvUserObject = JsonSerializer.Serialize(value);
    }
}
public class TaskLog : IHasGuid
{
    [Key]
    public Guid Guid {get; set;}
    
    [Required]
    public DateTime? RunTime {get; set;}
    public Guid TaskGuid {get; set;}    
    [Required]
    public string? TaskDisplayName {get; set;}    
    [Required]
    public string? CurrentStatus {get; set;}
    public string? Result {get; set;}
    public int? CreatedUsers {get; set;}
    public int? UpdatedUsers {get; set;}
}
public class NotificationLog : IHasGuid
{
    [Key]
    public Guid Guid {get; set;}    
    [Required]
    public DateTime? RunTime {get; set;}    
    [Required]
    public Guid? NotificationGuid {get; set;}    
    [Required]
    public string? Notification {get; set;}
    public string? Result {get; set;}

    [NotMapped]
    public NotificationData _notification
    {
        get => JsonSerializer.Deserialize<NotificationData>(Notification);
        set => Notification = JsonSerializer.Serialize(value);
    }

}