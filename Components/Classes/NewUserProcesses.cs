using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

namespace nump.Components.Classes;

public interface IHasGuid
{
    Guid Guid { get; }
}
public class RequiredElement
{
    public string Attribute { get; set; }
    public string Value { get; set; }
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
    public string name { get; set; }
    public string type { get; set; }
    public string sendRecipients { get; set; }
    public string? ccRecipients { get; set; }
    public string? bccRecipients { get; set; }
    public string? header { get; set; }
    public string? body { get; set; }
    public int NotificationType { get; set; }
    public DateTime? runTime { get; set; }
    [NotMapped]
    public List<string> sendRecipientsList
    {
        get => JsonSerializer.Deserialize<List<string>>(sendRecipients);
        set => sendRecipients = JsonSerializer.Serialize(value);
    }
    [NotMapped]
    public List<string>? ccRecipientsList
    {
        get => JsonSerializer.Deserialize<List<string>>(ccRecipients);
        set => ccRecipients = JsonSerializer.Serialize(value);
    }
    [NotMapped]
    public List<string>? bccRecipientsList
    {
        get => JsonSerializer.Deserialize<List<string>>(bccRecipients);
        set => bccRecipients = JsonSerializer.Serialize(value);
    }
}

public class Location
{
    public string sourceColumnValue { get; set; }
    public string adOUGuid { get; set; }
}
public class LocationMap : IHasGuid
{
    [Key]
    public Guid Guid { get; set; }
    public string name { get; set; }
    public string? description { get; set; }
    public string defaultLocation { get; set; }

    public string locations { get; set; }

    [NotMapped]
    public List<Location>? locationList
    {
        get => JsonSerializer.Deserialize<List<Location>>(locations);
        set => locations = JsonSerializer.Serialize(value);
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
    public string option { get; set; }
    public string value { get; set; }
    public string? sourceColumn { get; set; }
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
        get => JsonSerializer.Deserialize<PWCreationOptions>(PasswordCreationValue);
        set => PasswordCreationValue = JsonSerializer.Serialize(value);
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
    public string name { get; set; }

    public string? description { get; set; }
    public string fileLocation { get; set; }
    public string? adLocationColumn { get; set; }

    public string _accountOption { get; set; }
    public string _emailOption { get; set; }
    public string _managerOption { get; set; }
    public Guid locationMap { get; set; }

    public string _attributeMap { get; set; }
    [NotMapped]
    public List<ADAttributeMap>? attributeMap
    {
        get => JsonSerializer.Deserialize<List<ADAttributeMap>>(_attributeMap);
        set => _attributeMap = JsonSerializer.Serialize(value);
    }


    [NotMapped]
    public List<string>? locationList
    {
        get => JsonSerializer.Deserialize<List<string>>(fileLocation);
        set => fileLocation = JsonSerializer.Serialize(value);
    }
    [NotMapped]
    public AccountOptions accountOption
    {
        get => JsonSerializer.Deserialize<AccountOptions>(_accountOption);
        set => _accountOption = JsonSerializer.Serialize(value);
    }
    [NotMapped]
    public Option? emailOption
    {
        get => string.IsNullOrEmpty(_emailOption) ? null : JsonSerializer.Deserialize<Option>(_emailOption);
        set => _emailOption = value != null ? JsonSerializer.Serialize(value) : null;
    }
    [NotMapped]
    public ManagerOption managerOption
    {
        get => string.IsNullOrEmpty(_managerOption) ? null : JsonSerializer.Deserialize<ManagerOption>(_managerOption);
        set => _managerOption = value != null ? JsonSerializer.Serialize(value) : null;
    }
    public string? filter { get; set; }


}

public class Frequency
{
    public int type { get; set; }
    public TimeOnly time { get; set; }
    public DateOnly? date { get; set; }
}
public class NumpInstructionSet : IHasGuid
{
    [Key]
    public Guid Guid { get; set; }
    public string Name { get; set; }
    public bool Enabled { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Modified { get; set; }
    public string Frequency { get; set; }
    public Guid? Notifications { get; set; }
    public Guid AssocIngest { get; set; }
    public bool AllowUpdateFields { get; set; }
    public bool AllowCreateAccount { get; set; }
    public bool AllowSearchLogging { get; set; }
    public int AccountExpirationDays { get; set; }
    public Guid? ParentTask { get; set; }
    public string? Description { get; set; }
    private DateTime nextRunTime { get; set; }
    [NotMapped]
    public CancellationTokenSource? CancelToken { get; set; }

    [NotMapped]
    public string CurrentStatus { get; set; }
    [NotMapped]
    public int CurrCsvRow { get; set; }

    [NotMapped]
    public int MaxCsvRow { get; set; }

    [NotMapped]
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
        get => JsonSerializer.Deserialize<Frequency>(Frequency);
        set
        {
            Frequency = JsonSerializer.Serialize(value);
            UpdateNextRunTime();
        }
    }
    private void UpdateNextRunTime()
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
    [NotMapped]
    public DateTime NextRunTime
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
        private set
        {
            // Set the internal variable _nextRunTime
            nextRunTime = value;
        }
    }
}