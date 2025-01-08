using System.DirectoryServices.AccountManagement;
using System.DirectoryServices;
using nump.Components.Classes;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using nump.Components.Database;
using Microsoft.EntityFrameworkCore;
using CsvHelper;
using System.Text.Json;
using System.DirectoryServices.ActiveDirectory;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using nump.Components.Pages.Settings;
using CsvHelper.Configuration;
using System.Globalization;
using Radzen;
namespace nump.Components.Services;
public partial class UserService
{

    protected readonly NumpContext _context;

    protected readonly NotifService _notify;
    protected readonly PasswordService _pw;
    public event EventHandler<TaskUpdatedEventArgs> OnTaskUpdated;

    Dictionary<string, string> adData = new Dictionary<string, string>();

    public UserService(NumpContext context, NotifService notify, PasswordService pw)
    {
        _context = context;
        _notify = notify;
        _pw = pw;

    }
    public class TaskUpdatedEventArgs : EventArgs
    {
        public TaskProcess task { get; }
        public TaskUpdatedEventArgs(TaskProcess Task)
        {
            task = Task;
        }
    }


    TaskLog? loggedTask;
    public List<string> samAccountNames = new List<string>();

    public UserService()
    {
        //GetSamAccountNames();
    }

    public async Task ActuallyDoTask(Guid taskId)
    {
        TaskProcess? task = _context.Tasks.Where(x => x.Guid == taskId).Include(p => p.IngestChild).ThenInclude(p => p.LocationMapChild).FirstOrDefault();
        if (task != null)
        {
            if (loggedTask != null)
            {
                loggedTask = null;
            }
            task.CancelToken = new CancellationTokenSource();
            await DoTask(task);
        }
        loggedTask = null;
        Guid? childTaskGuid = await _context.Tasks.Where(x => x.ParentTask == task.Guid && x.Enabled == true).Select(x => x.Guid).FirstOrDefaultAsync();
        if (childTaskGuid.HasValue && childTaskGuid.Value != Guid.Empty)
        {
            await ActuallyDoTask(childTaskGuid.Value);
        }
    }
    public async Task DoTask(TaskProcess task)
    {

        List<NotificationData> notifications = await _context.Notifications.Where(x => task.CompletedNotificationList != null ? task.CompletedNotificationList.Contains(x.Guid) : false || x.Guid == task.CreatedNotification || x.Guid == task.UpdatedNotification).ToListAsync();

        task.CurrentStatus = "Running";
        await Task.Yield();
        loggedTask = await SaveTaskLog(task, null);

        List<ADAttributeMap> enabledAttributes = task.IngestChild.attributeMap.Where(x => x.enabled).ToList();
        List<ADAttributeMap> requiredAttributes = enabledAttributes.Where(x => x.required).ToList();
        List<ADAttributeMap> updatableAttributes = enabledAttributes.Where(x => x.allowUpdate).ToList();
        List<ADAttributeMap> conformableAttributes = requiredAttributes.Concat(updatableAttributes).ToList();

        List<string> headersToCheck = conformableAttributes.Select(x => x.associatedColumn).ToList().ConvertAll(d => d.ToLower());
        LocationMap? currLocationMap = _context.LocationMaps.Where(x => x.Guid == task.IngestChild.locationMap).FirstOrDefault();

        if (task.IngestChild.adLocationColumn != null)
        {
            headersToCheck.Add(task.IngestChild.adLocationColumn.ToLower());
        }

        //Initialization
        if (task.IngestChild == null || !Directory.Exists(task.IngestChild.FileLocation))
        {
            return;
        }
        List<string> csvFiles = Directory.EnumerateFiles(task.IngestChild.FileLocation, "*.*", SearchOption.TopDirectoryOnly).Where(s => s.EndsWith(".csv")).ToList();

        foreach (string file in csvFiles)
        {
            await CheckCancel(task);
            string ldapFilter = "(&(objectClass=user)(userAccountControl:1.2.840.113556.1.4.803:=512)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))";
            List<DirectoryEntry> allUsers = await FindUser(ldapFilter: ldapFilter);
            //(!(userAccountControl:1.2.840.113556.1.4.803:=2))(objectCategory=person)
            List<string> headerRow = new List<string>();
            List<DirectoryEntry> allCreatedUsers = new List<DirectoryEntry>();
            List<DirectoryEntry> allUpdatedUsers = new List<DirectoryEntry>();
            task.MaxCsvRow = await GetRowCount(file);
            OnTaskUpdated?.Invoke(this, new TaskUpdatedEventArgs(task)); ;
            var csvData = new Dictionary<int, Dictionary<string, string>>();

            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                PrepareHeaderForMatch
                = args => args.Header.ToLower()
            };
            using (FileStream fileStream = new FileStream(file, FileMode.Open, FileAccess.Read))
            using (StreamReader reader = new StreamReader(fileStream))
            using (CsvReader csv = new CsvReader(reader, csvConfig))
            {
                csv.Read();
                csv.ReadHeader();
                headerRow = csv.HeaderRecord.ToList();


                if (!await CheckHeader(headerRow, headersToCheck))
                {

                    await StopTask(task, "HEADER MISMATCH");
                    return;
                }
                OnTaskUpdated?.Invoke(this, new TaskUpdatedEventArgs(task));
                int lineNumber = 0;

                while (csv.Read())
                {

                    var record = new Dictionary<string, string>();

                    // For each header, get the value from the current record and add it to the dictionary
                    for (int i = 0; i < headerRow.Count; i++)
                    {
                        var header = headerRow[i];
                        var value = csv.GetField(i);
                        record[header.ToLower()] = value;
                    }

                    // Add the record to the result dictionary with line index as the key
                    csvData[lineNumber] = record;

                    // Increment the line index
                    lineNumber++;
                }
                task.CurrCsvRow = 0;
                Stopwatch Timer = new Stopwatch();
                Timer.Start();
                foreach (var csvLine in csvData)
                {

                    Dictionary<string, string> requiredElements = new Dictionary<string, string>();

                    foreach (ADAttributeMap requiredAttribute in requiredAttributes)
                    {
                        requiredElements.Add(requiredAttribute.selectedAttribute, csvLine.Value[requiredAttribute.associatedColumn.ToLower()]);
                    }

                    var requiredKeys = new HashSet<string>(requiredElements.Keys, StringComparer.OrdinalIgnoreCase);

                    // Create the list of DirectoryEntries (using a foreach loop to avoid unnecessary allocations)
                    List<DirectoryEntry> dirObjects = new List<DirectoryEntry>();

                    // Iterate over all users (now in a dictionary) and check if they match the required elements
                    foreach (var currUser in allUsers)
                    {
                        bool matches = true;

                        // Check if the user properties match all required attributes
                        foreach (var attr in requiredElements)
                        {
                            // Skip user if the required attribute does not exist
                            if (!currUser.Properties.Contains(attr.Key) || currUser.Properties[attr.Key] == null)
                            {
                                matches = false;
                                break;
                            }

                            // Get the user's property value
                            var userPropertyValue = currUser.Properties[attr.Key]?.Value?.ToString();

                            // Compare the value (case-insensitive)
                            if (!string.Equals(userPropertyValue, attr.Value, StringComparison.OrdinalIgnoreCase))
                            {
                                matches = false;
                                break;
                            }
                        }
                    }

                        Dictionary<string, string> newUser = new Dictionary<string, string>()
                            {
                                {"taskName", task.Name}
                            };

                        if (enabledAttributes != null)
                        {
                            foreach (var columnItem in enabledAttributes)
                            {
                                var columnValue = csvLine.Value.Where(x => x.Key == columnItem.associatedColumn.ToLower()).Select(x => x.Value).FirstOrDefault().ToString();
                                newUser.Add(columnItem.selectedAttribute, columnValue);
                            }
                        }
                        if (dirObjects != null && dirObjects.Count() > 1)
                        {

                            if (task.AllowSearchLogging)
                            {
                                //await LogUserUpdate(task, newUser, "SEARCH", "MULTIPLE USERS FOUND");
                            }
                            await MoveOn(task, true);
                            continue;
                        }
                        if (dirObjects == null || dirObjects.Count == 0)
                        {
                            if (task.AllowSearchLogging)
                            {
                                //await LogUserUpdate(task, newUser, "SEARCH", "NOT FOUND");
                            }
                            //New user
                            if (!task.AllowCreateAccount)
                            {
                                //await LogUserUpdate(task, );
                                await MoveOn(task, true);
                                continue;

                            }
                            try
                            {
                                DirectoryEntry createdUser = await NewUser(task, conformableAttributes, csvLine.Value, newUser);
                                allCreatedUsers.Add(createdUser);
                                allUsers.Add(createdUser);

                            }
                            catch
                            {
                                Console.WriteLine("Failed to create user");
                            }
                            await MoveOn(task, true);
                            continue;
                        }
                        if (task.AllowSearchLogging)
                        {
                            // await LogUserUpdate(task, newUser, "SEARCH", "USER FOUND");
                        }

                        //update user
                        DirectoryEntry user = dirObjects[0];

                        if (!task.AllowUpdateFields)
                        {

                            //await LogUserUpdate(task, newUser, "UPDATE", "NOT ALLOWED");
                            await MoveOn(task, true);
                            continue;
                        }
                        int? updatedValues = await UpdateUser(conformableAttributes, newUser, user, task, csvLine.Value, true);
                        if (task.CurrCsvRow != task.MaxCsvRow)
                        {
                            task.CurrCsvRow++;
                        }
                        if (updatedValues > 0)
                        {
                            NotificationData? updatedUserNotification = notifications.Where(x => x.NotificationType == 3).FirstOrDefault();
                            if (updatedUserNotification != null)
                            {
                                List<DirectoryEntry> users = new List<DirectoryEntry>()
                            {
                                user
                            };
                                await HandleNotifications(task, loggedTask, updatedUserNotification, null, users);
                            }
                            allUpdatedUsers.Add(user);
                        }
                        await MoveOn(task, false);

                    }
                    Timer.Stop();
                    Console.WriteLine("Elapsed in MS: " + Timer.ElapsedMilliseconds);
                }
                loggedTask.CreatedUsers = allCreatedUsers.Count();
                loggedTask.UpdatedUsers = allUpdatedUsers.Count();
                List<NotificationData> completedNotification = notifications.Where(x => x.NotificationType == 1).ToList();
                if (completedNotification.Count > 0)
                {
                    foreach (NotificationData notification in completedNotification)
                    {
                        await HandleNotifications(task, loggedTask, notification, allCreatedUsers, allUpdatedUsers);
                    }
                }
                foreach (var user in allUsers)
                {
                    try
                    {
                        user.Dispose();
                    }
                    catch
                    {
                        Console.WriteLine("probably already disposed");
                    }
                }
            }
            await DoTaskSuccess(task);


        }
    private async Task CheckCancel(TaskProcess task)
    {
        if (task.CancelToken.Token.IsCancellationRequested)
        {
            await StopTask(task, "CANCELLED");
            throw new OperationCanceledException(task.CancelToken.Token);
        }
    }
    public async Task DoTaskSuccess(TaskProcess task)
    {
        string completedFolder = task.IngestChild.FileLocation;
        string destFile = "";

        if (task.CompletedFolder != null && task.CompletedFolder.Length > 0)
        {
            completedFolder = await ReplaceVariablesAnonymous(task.CompletedFolder, new Dictionary<string, string>()
            {
                {"taskName", task.Name}
            });
            if (!Directory.Exists(completedFolder))
            {
                Directory.CreateDirectory(completedFolder);
            }
            string[] files = Directory.GetFiles(task.IngestChild.FileLocation);
            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                destFile = Path.Combine(completedFolder, fileName);
                int count = 1;
                while (File.Exists(destFile))
                {
                    string tempFileName = $"{Path.GetFileNameWithoutExtension(fileName)}({count}){Path.GetExtension(fileName)}";
                    destFile = Path.Combine(completedFolder, tempFileName);
                    count++;
                }
                File.Move(file, destFile);
            }
        }
        if (task.RetentionDays != null && task.RetentionFolder.Length > 0)
        {
            string retentionFolder = task.IngestChild.FileLocation;
            if (task.RetentionFolder != null)
            {
                retentionFolder = await ReplaceVariablesAnonymous(task.RetentionFolder, new Dictionary<string, string>()
                {
                    {"taskName", task.Name}
                });
            }
            string[] files = Directory.GetFiles(retentionFolder, "*.csv", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                System.IO.FileInfo fi = new System.IO.FileInfo(file);
                if (fi.LastAccessTime < DateTime.Now.AddDays(-task.RetentionDays.Value))
                {
                    fi.Delete();
                }
            }
            // Delete empty subdirectories
            foreach (string dir in Directory.GetDirectories(retentionFolder, "*", SearchOption.AllDirectories))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
        }
        await StopTask(task, "SUCCESSFUL", destFile);

    }
    public async Task HandleNotifications(TaskProcess task, TaskLog taskLog, NotificationData notification, List<DirectoryEntry>? createdUsers = null, List<DirectoryEntry>? updatedUsers = null, string? password = null)
    {
        Dictionary<string, string> itemsToReplace = new Dictionary<string, string>()
        {
                {"taskName", task.Name},
                {"totalRows", task.MaxCsvRow.ToString()},
                {"logGuid", taskLog.Guid.ToString()},
        };
        DirectoryEntry? user = null;
        string body = notification.Body;
        if (notification == null)
        {
            return;
        }
        switch (notification.NotificationType)
        {
            case 1:
                itemsToReplace.Add("createdUserCount", taskLog.CreatedUsers.ToString());
                itemsToReplace.Add("updatedUserCount", taskLog.UpdatedUsers.ToString());
                body = await ReplaceUserVariables(body, createdUsers, 1, notification.Type);
                body = await ReplaceUserVariables(body, updatedUsers, 2, notification.Type);
                break;

            case 2:
                user = createdUsers[0];
                if (password != null)
                {
                    itemsToReplace.Add("password", password);

                }
                break;

            case 3:
                user = updatedUsers[0];
                break;

            default:
                break;

        }
        if (user != null)
        {
            Dictionary<string, string> userProperties = new Dictionary<string, string>();

            foreach (PropertyValueCollection property in user.Properties)
            {
                if (property.Value != null && property.Value.ToString() != string.Empty && (property.Value is string || property.Value is int))
                {
                    itemsToReplace.Add(property.PropertyName, property.Value.ToString());
                }
            }

        }

        body = await ReplaceVariablesAnonymous(body, itemsToReplace);
        notification.Subject = await ReplaceVariablesAnonymous(notification.Subject, itemsToReplace);

        Setting? emailSetting = await _context.Settings.Where(x => x.SettingName == "Email").FirstOrDefaultAsync();
        if (emailSetting == null)
        {
            Console.WriteLine("Email Not Configured!");
            return;
        }
        var data = JsonSerializer.Deserialize<Dictionary<string, dynamic>>(emailSetting.Data);
        string emailTypeString = data["emailType"].ToString();
        int emailType;
        int.TryParse(emailTypeString, out emailType);
        string result = "";
        if (emailType == 1)
        {
            string clientId = data["clientId"].ToString();
            string clientSecretEncrypted = data["clientSecret"].ToString();
            string clientSecret = await _pw.DecryptStringFromBase64_Aes(clientSecretEncrypted, null);
            string tenantId = data["tenantId"].ToString();
            string sendAsEmail = data["sendAsEmail"].ToString();
            result = await _notify.SendEmailOAuth(notification, body, clientId, clientSecret, tenantId, sendAsEmail);
            clientSecret = null;
            clientSecretEncrypted = null;

        }
        else if (emailType == 2)
        {
            string smtpServer = data["smtpServer"].ToString();
            int smtpPort = int.Parse(data["smtpPort"].ToString());
            string smtpUser = data["smtpUser"].ToString();
            string encryptedPassword = data["password"].ToString();
            string smtpPassword = await _pw.DecryptStringFromBase64_Aes(encryptedPassword, null);
            int secureType = data.ContainsKey("secureType") ? int.Parse(data["secureType"].ToString()) : 0;
            string displayName = data["displayName"].ToString();
            string mailbox = data["mailbox"].ToString();
            result = await _notify.SendEmailSMTP(notification, body, smtpServer, smtpPort, smtpUser, smtpPassword, secureType, mailbox, displayName);
        }
        Guid logged = await SaveNotificationLog(notification, result);
    }
    private async Task<int> GetRowCount(string file)
    {
        string[] lines = await File.ReadAllLinesAsync(file);
        return lines.Length - 1;
    }
    private async Task<bool> CheckHeader(List<string> headers, List<string> requiredColumns)
    {
        return requiredColumns.All(item => headers.Contains(item, StringComparer.OrdinalIgnoreCase));
    }
    private async Task MoveOn(TaskProcess task, bool nextRow)
    {
        if (nextRow)
        {
            task.CurrCsvRow++;

        }
        await Task.Yield();
        OnTaskUpdated?.Invoke(this, new TaskUpdatedEventArgs(task));
    }
    private async Task StopTask(TaskProcess task, string reason, string? fileLocation = null)
    {
        task.CurrentStatus = "Stopped";
        await SaveTaskLog(task, reason, fileLocation);
        OnTaskUpdated?.Invoke(this, new TaskUpdatedEventArgs(task));
        task.CurrCsvRow = 0;
        task.MaxCsvRow = 0;


    }
    private async Task<Guid> SaveNotificationLog(NotificationData notification, string result)
    {
        NotificationLog log = new NotificationLog
        {
            RunTime = DateTime.Now,
            NotificationGuid = notification.Guid,
            _notification = notification,
            Result = result
        };
        await _context.NotificationLogs.AddAsync(log);
        await _context.SaveChangesAsync();
        return log.Guid;
    }
    private async Task<TaskLog> SaveTaskLog(TaskProcess task, string? result, string? fileLocation = null)
    {
        if (loggedTask == null)
        {
            loggedTask = new TaskLog()
            {
                RunTime = DateTime.Now,
                TaskGuid = task.Guid,
                TaskDisplayName = task.Name,
                CurrentStatus = task.CurrentStatus,
                Result = result,
                CsvLocation = fileLocation
            };
        }
        loggedTask.CurrentStatus = task.CurrentStatus;
        loggedTask.Result = result;
        loggedTask.CsvLocation = fileLocation;

        if (loggedTask.Guid == Guid.Empty)
        {
            _context.TaskLogs.Add(loggedTask);
        }
        else
        {
            _context.TaskLogs.Update(loggedTask);
        }
        await _context.SaveChangesAsync();
        return loggedTask;
    }

    public async Task<int?> UpdateUser(List<ADAttributeMap> updatableAttributes, Dictionary<string, string> newUser, DirectoryEntry user, TaskProcess task, Dictionary<string, string> csvRecord, bool logUpdateUser)
    {
        int updatedValues = 0;

        foreach (ADAttributeMap attribute in updatableAttributes)
        {
            string? attributeValue = user.Properties[attribute.selectedAttribute]?.Value?.ToString();
            string? updatedValue = newUser.ContainsKey(attribute.selectedAttribute) ? newUser[attribute.selectedAttribute] : null;

            if (updatedValue != null && (attributeValue != updatedValue || attributeValue == null))
            {
                user.Properties[attribute.selectedAttribute].Value = updatedValue;
                updatedValues++;

                if (logUpdateUser)
                {
                    await LogUserUpdate(task, user, attribute.selectedAttribute, attributeValue, updatedValue, null);
                }
            }
        }

        if (newUser.TryGetValue("manager", out string? manager))
        {
            user.Properties["manager"].Value = manager;
        }

        if (updatedValues > 0 && logUpdateUser)
        {
            await LogUserUpdate(task, user, "UPDATE", "SUCCESSFUL", "SUCCESSFUL", csvRecord);
        }

        user.CommitChanges();
        return updatedValues;
    }


    public static string ConvertDistinguishedNameToCanonical(string distinguishedName)
    {
        // Split the DN by commas (e.g., CN=John Doe,OU=Employees,DC=example,DC=com)
        var parts = distinguishedName.Split(',')
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrEmpty(part))
            .ToArray();

        // Extract CN and DC values separately
        var cnPart = parts.FirstOrDefault(p => p.StartsWith("CN="))?.Substring(3); // Extract CN value
        var ouParts = parts.Where(p => p.StartsWith("OU=")).Select(p => p.Substring(3)).ToArray(); // Extract OU values
        var dcParts = parts.Where(p => p.StartsWith("DC=")).Select(p => p.Substring(3)).ToArray(); // Extract DC values

        // Combine DC parts into the domain name (e.g., "example.com")
        var domainName = string.Join(".", dcParts);
        // Combine the OU and CN parts to form the full canonical name
        var canonicalName = string.Join("/", ouParts.Concat(new[] { cnPart }).Where(x => x != null));

        // Return the canonical name in the format "domain.com/OU1/OU2/.../CN"
        return $"{domainName}/{canonicalName}";
    }

    public async Task<Dictionary<string, string>> GetCreds()
    {

        Dictionary<string, string> adSetting = new Dictionary<string, string>();
        Setting? setting = _context.Settings.Where(x => x.SettingName == "ActiveDirectory").FirstOrDefault();
        if (setting != null)
        {
            adSetting = JsonSerializer.Deserialize<Dictionary<string, string>>(setting.Data);

        }

        if (!adSetting.ContainsKey("domain"))
        {
            try
            {
                adSetting.Add("domain", Domain.GetCurrentDomain().Name);
            }

            catch
            {
                Console.WriteLine("not on a domain probs");
                throw;
            }

        }
        if (adSetting.ContainsKey("password"))
        {
            try
            {
                adSetting["password"] = await _pw.DecryptStringFromBase64_Aes(adSetting["password"]);
            }
            catch
            {
                Console.WriteLine("cant decrypt probs");
            }
        }

        return adSetting;
    }
    public async Task<List<DirectoryEntry>>? FindUser(Dictionary<string, string>? requiredElements = null, string? ldapFilter = null)
    {
        Dictionary<string, string> creds = new Dictionary<string, string>();
        try
        {
            creds = await GetCreds();
        }
        catch
        {
            return null;
        }
        List<DirectoryEntry> returnItem = new List<DirectoryEntry>();
        // Define the LDAP path for your Active Directory domain
        string ldapPath = $"LDAP://" + creds["domain"];
        // Create a DirectoryEntry object with the LDAP path
        DirectoryEntry entry = new DirectoryEntry(ldapPath);
        entry.Username = creds.ContainsKey("username") ? creds["username"] + "@" + creds["domain"] : null;
        entry.Password = creds.ContainsKey("password") ? creds["password"] : null;


        if (ldapFilter == null && requiredElements != null)
        {
            ldapFilter = await BuildLdapString(requiredElements);
        }


        // Create a DirectorySearcher to search the directory using the filter
        DirectorySearcher searcher = new DirectorySearcher(entry)
        {
            Filter = ldapFilter, // Set the search filter dynamically
            SearchScope = SearchScope.Subtree,
            PageSize = 1000,
            SizeLimit = 0
        };

        // Execute the search and get the first result
        SearchResultCollection searchResult = searcher.FindAll();
        if (searchResult.Count == 0)
        {
            return null;
        }
        foreach (SearchResult result in searchResult)
        {
            returnItem.Add(result.GetDirectoryEntry());
        }
        // Return the DirectoryEntry if a result is found
        return returnItem; // null if no result
    }
    public async Task<string> BuildLdapString(Dictionary<string, string> requiredElements, string matcher = "&")
    {
        var ldapFilterBuilder = new StringBuilder("(" + matcher);  // Start the AND clause
        if (requiredElements.Count == 0)
        {

        }
        foreach (var requiredElement in requiredElements)
        {
            string requiredElementValue = requiredElement.Value;

            // Handle objectGuid attribute specially
            if (requiredElement.Key.Equals("objectGUID", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Guid groupId = new Guid(requiredElementValue);
                    requiredElementValue = string.Join('\\', BitConverter
                                .ToString(groupId.ToByteArray())
                                .Split("-"))
                            .Insert(0, "\\");
                }
                catch
                {

                }

            }

            // Append the filter for the current attribute and value
            ldapFilterBuilder.Append($"({requiredElement.Key}={requiredElementValue})");
        }

        ldapFilterBuilder.Append(")");  // Close the AND clause
        return ldapFilterBuilder.ToString();
    }
    public async Task<string> ReplaceVariablesAnonymous(string sourceString, Dictionary<string, string> items)
    {
        string replacedString = sourceString;

        var regex = new Regex(@"\{\{([^\}]+)\}\}");
        var matches = regex.Matches(sourceString);

        foreach (Match match in matches)
        {
            string dateFormat = "MM/dd/yyyy";

            // Extract the property name from the placeholder (e.g., "firstName")
            string propertyName = match.Groups[1].Value;

            var value = items.ContainsKey(propertyName) ? items[propertyName] : null;
            if (value != null)
            {
                replacedString = replacedString.Replace(match.Value, value);
            }
            if (propertyName.Contains("@"))
            {
                dateFormat = propertyName.Split("@")[1];
                propertyName = propertyName.Split("@")[0];

            }
            switch (propertyName)
            {
                case string str when str.Equals("firstInitial", StringComparison.InvariantCultureIgnoreCase):
                    replacedString = items.ContainsKey("givenName") ? replacedString.Replace(match.Value, items["givenName"][0].ToString()) : replacedString;
                    break;
                case string str when str.Equals("lastInitial", StringComparison.InvariantCultureIgnoreCase):
                    replacedString = items.ContainsKey("sn") ? replacedString.Replace(match.Value, items["sn"][0].ToString()) : replacedString;
                    break;

                case string str when str.Equals("today", StringComparison.InvariantCultureIgnoreCase):
                    replacedString = replacedString.Replace(match.Value, DateTime.Now.ToString(dateFormat));
                    break;
                default:
                    break;
            }
        }
        return replacedString;
    }

    public async Task<string> ReplaceUserVariables(string sourceString, List<DirectoryEntry> users, int NotificationType, string type)
    {
        string word = "";
        switch (NotificationType)
        {
            case 1:
                word = "Created";
                break;
            case 2:
                word = "Updated";
                break;
        }
        string replacedString = sourceString;
        string? pattern = "";
        if (sourceString.Contains($"[All{word}Users]"))
        {
            pattern = @$"\[All{word}Users\](.*?)\[/All{word}Users\]";
        }
        Regex regex = new Regex(pattern);
        Match match = regex.Match(sourceString);

        if (match.Success && pattern != "")
        {
            string allUserData = "";
            // The group(1) contains the data between <AllUsers> and </AllUsers>
            string data = match.Groups[1].Value;
            if (users == null || users.Count() == 0)
            {
                allUserData = $"No users have been {word.ToLower()}.";
            }
            else
            {
                foreach (DirectoryEntry user in users)
                {
                    Dictionary<string, string> userProperties = new Dictionary<string, string>();

                    foreach (PropertyValueCollection property in user.Properties)
                    {
                        if (property.Value != null && property.Value.ToString() != string.Empty && property.Value is string)
                        {
                            userProperties[property.PropertyName] = property.Value.ToString();
                        }
                    }

                    string replacedData = await ReplaceVariablesAnonymous(data, userProperties);
                    allUserData += replacedData;
                    if (user != users.Last())
                    {
                        if (type == "HTML")
                        {
                            allUserData += "<br />";
                        }
                        if (type == "Text")
                        {
                            allUserData += Environment.NewLine;

                        }
                    }
                }
            }

            replacedString = Regex.Replace(replacedString, pattern, allUserData);
        }
        return replacedString;


    }
    private static Random _random = new Random();

    public async Task LogUserUpdate(TaskProcess task, DirectoryEntry user, string attribute, string? oldValue, string newValue, Dictionary<string, string>? csvObject)
    {
        Guid userGuid = await GetGuid(user.Properties["objectGuid"].Value);

        UserUpdateLog newLog = new UserUpdateLog
        {
            DateTime = DateTime.Now,
            RunId = loggedTask.Guid,
            UserName = user.Properties["userprincipalname"].Value.ToString(),
            UserGuid = userGuid,
            UpdatedAttribute = attribute,
            OldValue = oldValue == null ? "null" : oldValue,
            NewValue = newValue,
            CsvUserObject = csvObject != null ? JsonSerializer.Serialize(csvObject) : null
        };
        await _context.UserUpdateLogs.AddAsync(newLog);
        await _context.SaveChangesAsync();
    }
    public async Task LogUserCreate(TaskProcess task, DirectoryEntry user, string result, string? reason, Dictionary<string, string>? csvObject)
    {
        Guid guid = await GetGuid(user.Properties["objectGuid"].Value);
        // if guid is null create a failed log
        UserCreationLog newLog = new UserCreationLog
        {
            DateTime = DateTime.Now,
            RunId = loggedTask.Guid,
            UserName = user.Properties["userprincipalname"].Value.ToString(),
            UserGuid = guid,
            Result = result,
            Reason = reason != null ? reason : "",
            CreationDN = ConvertDistinguishedNameToCanonical(user.Properties["distinguishedName"].Value.ToString()),
            csvUserObject = csvObject != null ? JsonSerializer.Serialize(csvObject) : null
        };
        await _context.UserCreationLogs.AddAsync(newLog);
        await _context.SaveChangesAsync();
    }
    public static Dictionary<string, object> ConvertDynamicToDictionary(dynamic record)
    {
        var dictionary = new Dictionary<string, object>();

        // Convert the dynamic object into a dictionary using reflection
        var properties = (IDictionary<string, object>)record;

        foreach (var property in properties)
        {
            dictionary.Add(property.Key, property.Value);
        }

        return dictionary;
    }
    public async Task<Guid> GetGuid(object obj)
    {
        if (obj is byte[] byteArray && byteArray.Length == 16)
        {
            Guid guid = new Guid(byteArray);

            return guid;
        }
        return Guid.Empty;
    }

    /* NEW USER STUFF */
    public async Task<DirectoryEntry> NewUser(TaskProcess task, List<ADAttributeMap> updatableAttributes, Dictionary<string, string> csvRecord, Dictionary<string, string> user)
    {
        try
        {
            await SetUserDescription(task, csvRecord, user);
            await SetUserDisplayName(task, csvRecord, user);

            string ouPath = await GetTentativeOuPath(task, csvRecord);
            Dictionary<string, string> creds = await GetCreds();
            PrincipalContext context = new PrincipalContext(ContextType.Domain, creds["domain"], ouPath);
            string ldapPath = $"LDAP://" + creds["domain"] + "/" + ouPath;
            if (creds.ContainsKey("username") && creds.ContainsKey("password"))
            {
                context = new PrincipalContext(ContextType.Domain, creds["domain"], ouPath, creds["username"] + "@" + creds["domain"], creds["password"]);
            }
            string sam = await FindValidUsername(task.IngestChild.accountOption, user);
            sam = sam.ToLower();
            user.Add("samAccountName", sam);

            await SetUserEmail(task, csvRecord, user);
            await SetUserManager(task, csvRecord, user);

            UserPrincipal newUserPrincipal = CreateUserPrincipal(context, user, sam, task.CreateDomain, task.AccountExpirationDays);
            string acctPassword = await GeneratePassword(task, csvRecord, user);
            int attempts = 0;
            bool passwordSet = false;
            while (attempts < 4 && !passwordSet)
            {
                try
                {
                    newUserPrincipal.SetPassword(acctPassword);
                    passwordSet = true;
                }
                catch
                {
                    if (attempts < 3)
                    {
                        acctPassword = await GeneratePassword(task, csvRecord, user);
                    }
                    attempts++;
                }
            }
            if (!passwordSet)
            {
                throw new Exception("Failed to set password after multiple attempts.");
            }
            newUserPrincipal.ExpirePasswordNow();

            try
            {
                newUserPrincipal.Save();
            }
            catch
            {
                // Handle exception
            }

            DirectoryEntry newUser = (DirectoryEntry)newUserPrincipal.GetUnderlyingObject();
            await HandleGroups(task, newUser);
            await HandleNewUser(task, updatableAttributes, user, newUser, csvRecord, password: acctPassword);
            return newUser;
        }
        catch (COMException comEx)
        {
            // Optionally, log the HRESULT or other details from the exception
            return null;
        }
    }

    private async Task HandleGroups(TaskProcess task, DirectoryEntry newUser)
    {
        if (task.IngestChild.LocationMapChild.DefaultGroupList != null)
        {
            List<Guid> groupGuids = task.IngestChild.LocationMapChild.DefaultGroupList;

            foreach (Guid guid in groupGuids)
            {
                Dictionary<string, string> requiredElements = new Dictionary<string, string>()
                {
                    {"objectClass", "group"},
                    {"objectGuid", guid.ToString()}
                };
                List<DirectoryEntry> groupsToAdd = await FindUser(requiredElements);
                if (groupsToAdd == null || groupsToAdd.Count() > 1)
                {
                    Console.WriteLine("not found or found too many");
                    continue;
                }
                else
                {
                    try
                    {
                        DirectoryEntry groupToAdd = groupsToAdd[0];
                        groupToAdd.Properties["member"].Add(newUser.Path);
                        groupToAdd.CommitChanges();
                    }
                    catch (Exception ex)
                    {
                        //delete dispose of user object and fail out
                    }

                }
            }
        }
    }
    private async Task SetUserDescription(TaskProcess task, Dictionary<string, string> csvRecord, Dictionary<string, string> user)
    {
        if (task.IngestChild.accountOption.AccountDescriptionValue != null)
        {
            switch (task.IngestChild.accountOption.AccountDescriptionType)
            {
                case "Custom":
                    string desc = await ReplaceVariablesAnonymous(task.IngestChild.accountOption.AccountDescriptionValue, user);
                    user.Add("description", desc);
                    break;
                case "Column":
                    user.Add("description", csvRecord[task.IngestChild.accountOption.AccountDescriptionValue].ToString());
                    break;
                default:
                    break;
            }
        }
    }

    private async Task SetUserDisplayName(TaskProcess task, Dictionary<string, string> csvRecord, Dictionary<string, string> user)
    {
        if (task.IngestChild.accountOption.DisplayNameValue != null)
        {
            switch (task.IngestChild.accountOption.DisplayNameType)
            {
                case "Custom":
                    string display = await ReplaceVariablesAnonymous(task.IngestChild.accountOption.DisplayNameValue, user);
                    user.Add("displayName", display);
                    break;
                case "Column":
                    user.Add("displayName", csvRecord[task.IngestChild.accountOption.DisplayNameValue].ToString());
                    break;
                default:
                    break;
            }
        }
    }

    private async Task<string> GetTentativeOuPath(TaskProcess task, Dictionary<string, string> csvRecord)
    {
        string ouPath = "";
        if (task.IngestChild.LocationMapChild != null)
        {
            string tentativeOuPath = await GetOUDistinguishedName(task.IngestChild, csvRecord);
            if (!string.IsNullOrEmpty(tentativeOuPath))
            {
                ouPath = tentativeOuPath;
            }
        }
        return ouPath;
    }

    private async Task SetUserEmail(TaskProcess task, Dictionary<string, string> csvRecord, Dictionary<string, string> user)
    {
        switch (task.IngestChild.emailOption.option)
        {
            case "Custom":
                user.Add("mail", await ReplaceVariablesAnonymous(task.IngestChild.emailOption.value, user));
                break;
            case "Column":
                user.Add("mail", csvRecord[task.IngestChild.emailOption.value].ToString());
                break;
            default:
                break;
        }
    }

    private async Task SetUserManager(TaskProcess task, Dictionary<string, string> csvRecord, Dictionary<string, string> user)
    {
        switch (task.IngestChild.managerOption.Option)
        {
            case "Custom":
                user.Add("manager", await ReplaceVariablesAnonymous(task.IngestChild.managerOption.Value.ToLower(), user));
                break;
            case "Column":
                Dictionary<string, string> requiredElements = new Dictionary<string, string>
                {
                    {task.IngestChild.managerOption.SourceColumn, csvRecord[task.IngestChild.managerOption.Value.ToLower()].ToString()}
                };
                var Managers = await FindUser(requiredElements);
                if (Managers != null)
                {
                    user.Add("manager", Managers[0].Properties["distinguishedName"].Value.ToString());
                }
                break;
            default:
                break;
        }
    }

    private UserPrincipal CreateUserPrincipal(PrincipalContext context, Dictionary<string, string> user, string sam, string domain, int accountExpirationDays)
    {
        return new UserPrincipal(context)
        {
            Name = user.ContainsKey("displayName") ? user["displayName"] : null,
            GivenName = user.ContainsKey("givenName") ? user["givenName"] : null,
            Surname = user.ContainsKey("sn") ? user["sn"] : null,
            DisplayName = user.ContainsKey("displayName") ? user["displayName"] : null,
            SamAccountName = sam,
            UserPrincipalName = sam + "@" + domain,
            Enabled = true,
            Description = user.ContainsKey("description") ? user["description"] : null,
            EmailAddress = user.ContainsKey("mail") ? user["mail"] : null,
            AccountExpirationDate = accountExpirationDays > 0 ? DateTime.Now.AddDays(accountExpirationDays) : null
        };
    }

    private async Task<string> GeneratePassword(TaskProcess task, Dictionary<string, string> csvRecord, Dictionary<string, string> user)
    {
        string acctPassword = "";
        switch (task.IngestChild.accountOption.PasswordCreationType)
        {
            case "Custom":
                acctPassword = await ReplaceVariablesAnonymous(task.IngestChild.accountOption.PasswordCreationValue, user);
                break;
            case "RandomPassword":
                acctPassword = _pw.GeneratePassword(task.IngestChild.accountOption.PasswordOptions);
                break;
            case "Column":
                acctPassword = csvRecord[task.IngestChild.accountOption.PasswordCreationValue].ToString();
                break;
            case "RandomPassphrase":
                acctPassword = _pw.GeneratePassphrase(task.IngestChild.accountOption.PasswordOptions);
                break;
            default:
                break;
        }
        Console.WriteLine("PASSWORD @@@" + acctPassword);
        return acctPassword;
    }
    public async Task<string> FindValidUsername(AccountOptions accountOption, Dictionary<string, string> user)
    {

        string tentativeUPN = String.Empty;
        switch (accountOption.CreationType)
        {
            case "Custom":
                tentativeUPN = await ReplaceVariablesAnonymous(accountOption.CreationValue, user);
                tentativeUPN = Regex.Replace(tentativeUPN, @"[^\w]", "");
                Dictionary<string, string> upn = new Dictionary<string, string>()
                {
                    {"cn", tentativeUPN}
                };
                List<DirectoryEntry> existingUser = await FindUser(upn);
                if (existingUser != null)
                {
                    int i = 0;
                    string sourceUpn = tentativeUPN;
                    do
                    {
                        tentativeUPN = sourceUpn + i;
                        upn["cn"] = tentativeUPN;

                        existingUser = await FindUser(upn);
                        i++;
                    } while (existingUser != null);
                }
                break;
            case "Column":
                tentativeUPN = user[accountOption.CreationValue];
                break;
            default:
                break;
        };

        return tentativeUPN;
    }
    private async Task HandleNewUser(TaskProcess task, List<ADAttributeMap> updatableAttributes, Dictionary<string, string> user, DirectoryEntry newUser, Dictionary<string, string> csvRecord, string? password = null)
    {
        string result = "FAILED";
        try
        {
            await UpdateUser(updatableAttributes, user, newUser, task, csvRecord, false);
            result = "SUCCESSFUL";
            NotificationData? newUserNotification = _context.Notifications.Where(x => x.Guid == task.CreatedNotification).FirstOrDefault();
            if (newUserNotification != null)
            {
                List<DirectoryEntry> users = new List<DirectoryEntry> { newUser };
                await HandleNotifications(task, loggedTask, newUserNotification, users, null, password);
            }
            await LogUserCreate(task, newUser, result, null, csvRecord);
        }
        catch(Exception ex)
        {
            await LogUserCreate(task, newUser, result, ex.Message, csvRecord);
            newUser.DeleteTree();
            newUser.Dispose();

        }

    }
    public async Task<string?> GetOUDistinguishedName(IngestData currentIngest, Dictionary<string, string> csvRecord)
    {
        string locationValue = String.Empty;
        try
        {
            locationValue = csvRecord[currentIngest.adLocationColumn.ToLower()].ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        if (locationValue == "")
        {
            return String.Empty;
        }
        string? adGuid = currentIngest.LocationMapChild?.LocationList?.Where(x => x.SourceColumnValue == locationValue).Select(x => x.AdOUGuid).FirstOrDefault();
        if (adGuid == null)
        {
            adGuid = currentIngest.LocationMapChild?.DefaultLocation;
        }
        Dictionary<string, string> requiredElements = new Dictionary<string, string>
                {
                    {"objectGuid", adGuid ?? throw new ArgumentNullException(nameof(adGuid))}
                };

        List<DirectoryEntry>? ouItem = await FindUser(requiredElements);
        if (ouItem != null && ouItem.Count == 1 && ouItem[0] != null)
        {
            return ouItem[0].Properties["distinguishedName"]?.Value?.ToString();
        }
        return null;
    }
}


