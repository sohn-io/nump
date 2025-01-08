using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace nump.Components.Classes;



public interface IHasGuid
{
    public Guid Guid { get; set;}
}
public class TimeData
{
    public DateTime created { get; set; }
    public DateTime modified { get; set; }
}

public class NotificationData : IHasGuid
{
    [Key]
    public Guid Guid { get; set; }
    public string Name { get; set; } = String.Empty;
    public string Type { get; set; } = "HTML";
    public string SendRecipients { get; set; } = "[]";
    public string? CcRecipients { get; set; }
    public string? BccRecipients { get; set; }
    public string Subject { get; set; } = String.Empty;
    public string? Body { get; set; }
    public int NotificationType { get; set; }
    public DateTime? RunTime { get; set; }
    [NotMapped]
    public List<string> SendRecipientsList
    {
        get
        {
            return JsonConvert.DeserializeObject<List<string>>(SendRecipients) ?? new List<string>();
        }
        set => SendRecipients = JsonConvert.SerializeObject(value);
    }
    [NotMapped]
    public List<string>? CcRecipientsList
    {
        get => JsonConvert.DeserializeObject<List<string>>(CcRecipients);
        set => CcRecipients = JsonConvert.SerializeObject(value);
    }
    [NotMapped]
    public List<string>? BccRecipientsList
    {
        get => JsonConvert.DeserializeObject<List<string>>(BccRecipients);
        set => BccRecipients = JsonConvert.SerializeObject(value);
    }
}

public class Location
{
    public string SourceColumnValue { get; set; }
    public string AdOUGuid { get; set; }
    public List<Guid>? assocGroups {get; set;}
}
public class LocationMap : IHasGuid
{
    [Key]
    public Guid Guid { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public string DefaultLocation { get; set; }
    
    [JsonIgnore]
    public string? DefaultGroups { get; set; }

    [JsonIgnore]
    public string Locations { get; set; }

    [NotMapped]
    public List<Location>? LocationList
    {
        get => JsonConvert.DeserializeObject<List<Location>>(Locations);
        set => Locations = JsonConvert.SerializeObject(value);
    }
    [NotMapped]
    public List<Guid>? DefaultGroupList
    {
        get => string.IsNullOrEmpty(DefaultGroups) ? null : JsonConvert.DeserializeObject<List<Guid>>(DefaultGroups);
        set => DefaultGroups = value != null ? JsonConvert.SerializeObject(value) : null;
    }
}
public class Setting
{
    [Key]
    public string SettingName { get; set; }
    public string Data { get; set; }
}
public class Option
{
    public string option { get; set; }
    public string value { get; set; }
}
public class ManagerOption
{
    public string Option { get; set; }
    public string Value { get; set; }
    public string? SourceColumn { get; set; }
}
public class AccountOptions
{
    public string CreationType { get; set; }
    public string CreationValue { get; set; }
    public string AccountDescriptionType { get; set; }
    public string AccountDescriptionValue { get; set; }
    public string DisplayNameType { get; set; }
    public string DisplayNameValue { get; set; }
    public string PasswordCreationType { get; set; }

    public string PasswordCreationValue { get; set; }
    [NotMapped]
    public PWCreationOptions? PasswordOptions
    {
        get => JsonConvert.DeserializeObject<PWCreationOptions>(PasswordCreationValue);
        set => PasswordCreationValue = JsonConvert.SerializeObject(value);
    }
}
public class PWCreationOptions
{
    public int minLength { get; set; }
    public int maxLength { get; set; }
    public int specialChars { get; set; }
    public int numbers { get; set; }
    public int uppercase { get; set; }
    public string? separator { get; set; }
}
public class IngestData : IHasGuid
{
    [Key]
    public Guid Guid { get; set; }
    public string Name { get; set; } = String.Empty;

    public string? Description { get; set; }
    public string FileLocation { get; set; } = String.Empty;
    public string? adLocationColumn { get; set; }

    [JsonIgnore]
    public string AccountOption { get; set; } = "[]";

    [JsonIgnore]
    public string EmailOption { get; set; } = "[]";

    [JsonIgnore]
    public string ManagerOption { get; set; } = "[]";
    public Guid? locationMap { get; set; }
    public LocationMap? LocationMapChild {get; set;}

    [JsonIgnore]
    public string AttributeMap { get; set; } = "[]";

    [NotMapped]
    public List<ADAttributeMap>? attributeMap
    {
        get => JsonConvert.DeserializeObject<List<ADAttributeMap>>(AttributeMap);
        set => AttributeMap = JsonConvert.SerializeObject(value);
    }


    [NotMapped]
    public AccountOptions accountOption
    {
        get => JsonConvert.DeserializeObject<AccountOptions>(AccountOption);
        set => AccountOption = JsonConvert.SerializeObject(value);
    }
    [NotMapped]
    public Option? emailOption
    {
        get => string.IsNullOrEmpty(EmailOption) ? null : JsonConvert.DeserializeObject<Option>(EmailOption);
        set => EmailOption = value != null ? JsonConvert.SerializeObject(value) : null;
    }
    [NotMapped]
    public ManagerOption managerOption
    {
        get => string.IsNullOrEmpty(ManagerOption) ? null : JsonConvert.DeserializeObject<ManagerOption>(ManagerOption);
        set => ManagerOption = value != null ? JsonConvert.SerializeObject(value) : null;
    }
    public string? filter { get; set; }


}

public class Frequency
{
    public int type { get; set; }
    public TimeOnly time { get; set; }
    public DateOnly? date { get; set; }
}
public class TaskProcess : IHasGuid
{
    [Key]
    public Guid Guid { get; set; }
    public string Name { get; set; } = String.Empty;
    public bool Enabled { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Modified { get; set; }

    [JsonIgnore]
    public string Frequency { get; set; } = "[]";

    [JsonIgnore]
    public string? CompletedNotification { get; set; }
    public Guid? CreatedNotification { get; set; }
    public Guid? UpdatedNotification { get; set; }
    public Guid AssocIngest { get; set; }
    public IngestData? IngestChild { get; set; }

    public bool AllowUpdateFields { get; set; }
    public bool AllowCreateAccount { get; set; }
    public string? CreateDomain {get; set;}
    public bool AllowSearchLogging { get; set; }
    public int AccountExpirationDays { get; set; }
    public Guid? ParentTask { get; set; }
    public string? Description { get; set; }
    public string? CompletedFolder { get; set; }
    public string? RetentionFolder { get; set; }
    public int? RetentionDays { get; set; }
    [NotMapped]
    [JsonIgnore]
    public CancellationTokenSource? CancelToken { get; set; }

    [NotMapped]
    [JsonIgnore]
    public string CurrentStatus { get; set; } = String.Empty;
    [NotMapped]
    [JsonIgnore]
    public int CurrCsvRow { get; set; }

    [NotMapped]
    [JsonIgnore]
    public int MaxCsvRow { get; set; }

    [NotMapped]
    public List<Guid>? CompletedNotificationList
    {
        get => string.IsNullOrEmpty(CompletedNotification) ? null : JsonConvert.DeserializeObject<List<Guid>>(CompletedNotification);
        set => CompletedNotification = value != null ? JsonConvert.SerializeObject(value) : null;
    }

    [NotMapped]
    [JsonIgnore]
    public double CurrProgress
    {
        get
        {
            // Guard against division by zero
            if (MaxCsvRow == 0)
            {
                return 0; // Or handle appropriately, like returning 100 or -1 for "undefined"
            }

            // Calculate progress as a percentage
            return Math.Ceiling((double)CurrCsvRow / MaxCsvRow * 100);
        }
        set {; }
    }

    [NotMapped]
    public Frequency _frequency
    {
        get => JsonConvert.DeserializeObject<Frequency>(Frequency);
        set
        {
            Frequency = JsonConvert.SerializeObject(value);
            UpdateNextRunTime();
        }
    }
    public void UpdateNextRunTime()
    {
        // Calculate the next run time based on the frequency time
        DateTime currentDateTime = DateTime.Now;
        DateTime calculatedNextRunTime = currentDateTime.Date.Add(_frequency.time.ToTimeSpan());

        // If the calculated next run time is in the past, schedule it for tomorrow
        if (calculatedNextRunTime < currentDateTime)
        {
            calculatedNextRunTime = calculatedNextRunTime.AddDays(1);
        }

        // Set the updated NextRunTime
        NextRunTime = calculatedNextRunTime;
    }
    [JsonIgnore]
    public DateTime? NextRunTime
    {
        get
        {
            // Ensure NextRunTime is not in the past, if it is, update it
            if (nextRunTime < DateTime.Now)
            {
                UpdateNextRunTime();
            }

            // Return the computed next run time
            return nextRunTime;

        }
        set
        {
            nextRunTime = value;
        }
    }
    [JsonIgnore]
    private DateTime? nextRunTime;
}
