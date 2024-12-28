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
        public NumpInstructionSet task { get; }
        public TaskUpdatedEventArgs(NumpInstructionSet Task)
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

    public async Task ActuallyDoTask(NumpInstructionSet task)
    {
        task.CancelToken = new CancellationTokenSource();
        await DoTask(task);
        loggedTask = null;
        NumpInstructionSet childTask = _context.Tasks.Where(x => x.ParentTask == task.Guid && x.Enabled == true).FirstOrDefault();
        if (childTask != null)
        {
            childTask.CancelToken = new CancellationTokenSource();
            await DoTask(childTask);
        }
    }
    public async Task DoTask(NumpInstructionSet task)
    {
        IngestData? ingest = await _context.IngestData.Where(x => x.Guid == task.AssocIngest).FirstAsync();

        List<NotificationData> notifications = await _context.Notifications.Where(x => x.Guid == task.CompletedNotification || x.Guid == task.CreatedNotification || x.Guid == task.UpdatedNotification).ToListAsync();

        task.CurrentStatus = "Running";
        loggedTask = await SaveTaskLog(task, null);

        List<ADAttributeMap> enabledAttributes = ingest.attributeMap.Where(x => x.enabled).ToList();
        List<ADAttributeMap> requiredAttributes = enabledAttributes.Where(x => x.required).ToList();
        List<ADAttributeMap> updatableAttributes = enabledAttributes.Where(x => x.allowUpdate).ToList();
        List<ADAttributeMap> conformableAttributes = enabledAttributes.Where(x => x.required || x.allowUpdate).ToList();

        List<string> headersToCheck = conformableAttributes.Select(x => x.associatedColumn).ToList();
        LocationMap? currLocationMap = _context.LocationMaps.Where(x => x.Guid == ingest.locationMap).FirstOrDefault();

        if (ingest.adLocationColumn != null)
        {
            headersToCheck.Add(ingest.adLocationColumn);
        }

        //Initialization
        if (ingest == null || !Directory.Exists(ingest.fileLocation))
        {
            return;
        }
        List<string> csvFiles = Directory.EnumerateFiles(ingest.fileLocation, "*.*", SearchOption.TopDirectoryOnly).Where(s => s.EndsWith(".csv")).ToList();
        foreach (string file in csvFiles)
        {
            await CheckCancel(task);
            List<string> headerRow = new List<string>();
            List<DirectoryEntry> allCreatedUsers = new List<DirectoryEntry>();
            List<DirectoryEntry> allUpdatedUsers = new List<DirectoryEntry>();
            task.MaxCsvRow = await GetRowCount(file);
            OnTaskUpdated?.Invoke(this, new TaskUpdatedEventArgs(task)); ;
            Dictionary<int, Dictionary<string, string>> csvData = new Dictionary<int, Dictionary<string, string>>();
            using (FileStream fileStream = new FileStream(file, FileMode.Open, FileAccess.Read))
            using (StreamReader reader = new StreamReader(fileStream))
            using (CsvReader csv = new CsvReader(reader, new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)))
            {
                csv.Read();
                csv.ReadHeader();
                headerRow = csv.HeaderRecord.ToList();


                if (!await CheckHeader(headerRow, headersToCheck))
                {

                    await StopTask(task, "HEADER MISMATCH");
                    return;
                }
                task.CurrCsvRow = 0;
                OnTaskUpdated?.Invoke(this, new TaskUpdatedEventArgs(task)); ;
                int lineNumber = 0;

                while (csv.Read())
                {
                    await CheckCancel(task);

                    Dictionary<string, string> requiredElements = new Dictionary<string, string>();
                    var currCsvRecord = csv.GetRecord<dynamic>();
                    Dictionary<string, object> csvRecordDictionary = ConvertDynamicToDictionary(currCsvRecord);

                    foreach (ADAttributeMap requiredAttribute in requiredAttributes)
                    {
                        requiredElements.Add(requiredAttribute.selectedAttribute, csv.GetField<string>(requiredAttribute.associatedColumn));
                    }
                    List<DirectoryEntry> dirObjects = await FindUser(requiredElements);

                    Dictionary<string, string> newUser = new Dictionary<string, string>()
                            {
                                {"taskName", task.Name}
                            };

                    if (enabledAttributes != null)
                    {
                        foreach (var columnItem in enabledAttributes)
                        {
                            var columnValue = csvRecordDictionary[columnItem.associatedColumn].ToString();
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
                    if (dirObjects == null)
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
                            DirectoryEntry createdUser = await NewUser(ingest, conformableAttributes, csvRecordDictionary, newUser, currLocationMap, task);
                            allCreatedUsers.Add(createdUser);
                        }
                        catch
                        {

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
                    int? updatedValues = await UpdateUser(conformableAttributes, newUser, user, task, csvRecordDictionary, true);
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

            }
            loggedTask.CreatedUsers = allCreatedUsers.Count();
            loggedTask.UpdatedUsers = allUpdatedUsers.Count();
            NotificationData? completedNotification = notifications.Where(x => x.NotificationType == 1).FirstOrDefault();
            if (completedNotification != null)
            {
                await HandleNotifications(task, loggedTask, completedNotification, allCreatedUsers, allUpdatedUsers);
            }
        }

        await StopTask(task, "SUCCESSFUL");


    }
    private async Task CheckCancel(NumpInstructionSet task)
    {
        if (task.CancelToken.Token.IsCancellationRequested)
        {
            await StopTask(task, "CANCELLED");
            throw new OperationCanceledException(task.CancelToken.Token);
        }
    }
    public async Task HandleNotifications(NumpInstructionSet task, TaskLog taskLog, NotificationData notification, List<DirectoryEntry>? createdUsers = null, List<DirectoryEntry>? updatedUsers = null)
    {
        Dictionary<string, string> itemsToReplace = new Dictionary<string, string>()
        {
                {"taskName", task.Name},
                {"totalRows", task.MaxCsvRow.ToString()},
                {"logGuid", taskLog.Guid.ToString()},
        };
        DirectoryEntry? user = null;
        string body = notification.body;
        if (notification == null)
        {
            return;
        }
        switch (notification.NotificationType)
        {
            case 1:
                itemsToReplace.Add("createdUserCount", taskLog.CreatedUsers.ToString());
                itemsToReplace.Add("updatedUserCount", taskLog.UpdatedUsers.ToString());
                body = await ReplaceUserVariables(body, createdUsers, 1, notification.type);
                body = await ReplaceUserVariables(body, updatedUsers, 2, notification.type);
                break;

            case 2:
                user = createdUsers[0];
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
    private async Task MoveOn(NumpInstructionSet task, bool nextRow)
    {
        if (nextRow)
        {
            task.CurrCsvRow++;

        }
        await Task.Yield();
        OnTaskUpdated?.Invoke(this, new TaskUpdatedEventArgs(task));
    }
    private async Task StopTask(NumpInstructionSet task, string reason)
    {
        task.CurrentStatus = "Stopped";
        await SaveTaskLog(task, reason);
        OnTaskUpdated?.Invoke(this, new TaskUpdatedEventArgs(task));

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
    private async Task<TaskLog> SaveTaskLog(NumpInstructionSet task, string? result)
    {
        if (loggedTask == null)
        {
            loggedTask = new TaskLog()
            {
                RunTime = DateTime.Now,
                TaskGuid = task.Guid,
                TaskDisplayName = task.Name,
                CurrentStatus = task.CurrentStatus,
                Result = result
            };
        }
        loggedTask.CurrentStatus = task.CurrentStatus;
        loggedTask.Result = result;

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
    public async Task<DirectoryEntry> NewUser(IngestData currentIngest, List<ADAttributeMap> updatableAttributes, Dictionary<string, object> csvRecord, Dictionary<string, string> user, LocationMap? locationMap, NumpInstructionSet task)
    {
        try
        {

            if (currentIngest.accountOption.AccountDescriptionValue != null)
            {
                switch (currentIngest.accountOption.AccountDescriptionType)
                {
                    case "Custom":
                        string desc = await ReplaceVariablesAnonymous(currentIngest.accountOption.AccountDescriptionValue, user);
                        user.Add("description", desc);
                        break;
                    case "Column":
                        user.Add("description", csvRecord[currentIngest.accountOption.AccountDescriptionValue].ToString());
                        break;
                    default:
                        break;
                }
            }
            if (currentIngest.accountOption.DisplayNameValue != null)
            {
                switch (currentIngest.accountOption.DisplayNameType)
                {
                    case "Custom":
                        string display = await ReplaceVariablesAnonymous(currentIngest.accountOption.DisplayNameValue, user);
                        user.Add("displayName", display);
                        break;
                    case "Column":
                        user.Add("displayName", csvRecord[currentIngest.accountOption.DisplayNameValue].ToString());
                        break;
                    default:
                        break;
                }
            }
            /* @@@ Location @@@ */
            string ouPath = await GetOUDistinguishedName(currentIngest, csvRecord, locationMap);

            if (locationMap != null)
            {
                string tentativeOuPath = await GetOUDistinguishedName(currentIngest, csvRecord, locationMap);
                if (tentativeOuPath != null && tentativeOuPath != String.Empty && tentativeOuPath != "")
                {

                    ouPath = tentativeOuPath;
                }
            }
            Dictionary<string, string> creds = await GetCreds();
            List<DirectoryEntry> returnItem = new List<DirectoryEntry>();
            // Define the LDAP path for your Active Directory domain
            string ldapPath = $"LDAP://" + creds["domain"] + "/" + ouPath;
            string username = creds.ContainsKey("username") ? creds["username"] : null;
            string password = creds.ContainsKey("password") ? creds["password"] : null;
            PrincipalContext context = new PrincipalContext(ContextType.Domain, creds["domain"], ouPath, username + "@" + creds["domain"], password);

            /* @@@ Account Name @@@ */
            string sam = await FindValidUsername(currentIngest.accountOption, user);
            sam = sam.ToLower();
            user.Add("samAccountName", sam);

            switch (currentIngest.emailOption.option)
            {
                case "Custom":
                    user.Add("mail", await ReplaceVariablesAnonymous(currentIngest.emailOption.value, user));
                    break;
                case "Column":
                    user.Add("mail", csvRecord[currentIngest.emailOption.value].ToString());
                    break;
                default:
                    break;
            }
            switch (currentIngest.managerOption.option)
            {
                case "Custom":
                    user.Add("manager", await ReplaceVariablesAnonymous(currentIngest.managerOption.value, user));
                    break;
                case "Column":
                    Dictionary<string, string> requiredElements = new Dictionary<string, string>()
                {
                    {currentIngest.managerOption.sourceColumn, csvRecord[currentIngest.managerOption.value].ToString()}
                };
                    var Managers = await FindUser(requiredElements);
                    if (Managers != null)
                    {
                        user.Add("manager", Managers[0].Properties["distinguishedName"].Value.ToString());
                    }
                    else
                    {

                    }
                    break;
                default:
                    break;
            }
            UserPrincipal newUserPrincipal = new UserPrincipal(context)
            {
                GivenName = user.ContainsKey("givenName") ? user["givenName"] : null,
                Surname = user.ContainsKey("sn") ? user["sn"] : null,
                DisplayName = user.ContainsKey("displayName") ? user["displayName"] : null,
                SamAccountName = sam,
                UserPrincipalName = sam + "@" + creds["domain"],
                Enabled = true,
                Description = user.ContainsKey("description") ? user["description"] : null,
                EmailAddress = user.ContainsKey("mail") ? user["mail"] : null,
                AccountExpirationDate = task.AccountExpirationDays > 0 ? DateTime.Now.AddDays(task.AccountExpirationDays) : null

            };
            newUserPrincipal.PasswordNotRequired = false;

            string acctPassword = "";
            switch (currentIngest.accountOption.PasswordCreationType)
            {
                case "Custom":
                    acctPassword = await ReplaceVariablesAnonymous(currentIngest.accountOption.PasswordCreationValue, user);
                    break;
                case "RandomPassword":
                    acctPassword = _pw.GeneratePassword(currentIngest.accountOption.PasswordOptions);
                    break;
                case "Column":
                    acctPassword = csvRecord[currentIngest.accountOption.PasswordCreationValue].ToString();
                    break;
                case "RandomPassphrase":
                    acctPassword = _pw.GeneratePassphrase(currentIngest.accountOption.PasswordOptions);
                    break;
                default:
                    break;
            }
            for (int i = 0; i < 5; i++)
            {

            }

            newUserPrincipal.SetPassword(acctPassword);
            newUserPrincipal.ExpirePasswordNow();
            try
            {
                newUserPrincipal.Save();
            }
            catch
            {
            }
            acctPassword = "";

            DirectoryEntry newUser = (DirectoryEntry)newUserPrincipal.GetUnderlyingObject();
            try
            {
                await UpdateUser(updatableAttributes, user, newUser, task, csvRecord, false);
                await LogUserCreate(task, newUser, "SUCCESSFUL", null, csvRecord);
                NotificationData? newUserNotification = _context.Notifications.Where(x => x.Guid == task.CreatedNotification).FirstOrDefault();
                if (newUserNotification != null)
                {
                    List<DirectoryEntry> users = new List<DirectoryEntry>()
                                {
                                    newUser
                                };
                    await HandleNotifications(task, loggedTask, newUserNotification, users);
                }


            }
            catch
            {

                newUserPrincipal.Delete();
                newUserPrincipal.Save();
                await LogUserCreate(task, newUser, "FAILED", null, csvRecord);
            }
            return newUser;
        }

        catch (COMException comEx)
        {
            return null;
            // Optionally, log the HRESULT or other details from the exception

        }
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
    public async Task<string?> GetOUDistinguishedName(IngestData currentIngest, Dictionary<string, object> csvRecord, LocationMap locationMap)
    {
        string locationValue = String.Empty;
        try
        {
            locationValue = csvRecord[currentIngest.adLocationColumn].ToString();
        }
        catch (Exception ex)
        {

        }
        if (locationValue == "")
        {
            return String.Empty;
        }
        string? adGuid = locationMap.locationList.Where(x => x.sourceColumnValue == locationValue).Select(x => x.adOUGuid).FirstOrDefault();
        if (adGuid == null)
        {
            adGuid = locationMap.defaultLocation;
        }
        Dictionary<string, string> requiredElements = new Dictionary<string, string>
                {
                    {"objectGuid", adGuid}
                };

        List<DirectoryEntry> ouItem = await FindUser(requiredElements);
        if (ouItem != null && ouItem.Count() == 1)
        {
            return ouItem[0].Properties["distinguishedName"].Value.ToString();
        }
        return null;
    }
    public async Task<int?> UpdateUser(List<ADAttributeMap> updatableAttributes, Dictionary<string, string> newUser, DirectoryEntry user, NumpInstructionSet task, Dictionary<string, object> csvRecord, bool logUpdateUser)
    {
        int updatedValues = 0;
        foreach (ADAttributeMap attribute in updatableAttributes)
        {
            string? attributeValue = user.Properties[attribute.selectedAttribute]?.Value?.ToString();
            var updatedValue = newUser.Any(k => k.Key == attribute.selectedAttribute) ? newUser[attribute.selectedAttribute] : null;
            if (updatedValue == null)
            {
                continue;
            }
            if (attributeValue != updatedValue || attributeValue == null)
            {
                user.Properties[attribute.selectedAttribute].Value = updatedValue;
                updatedValues++;

                if (logUpdateUser)
                {
                    await LogUserUpdate(task, user, attribute.selectedAttribute, attributeValue, updatedValue, null);
                }
            }

        }
        if (newUser.ContainsKey("manager"))
        {
            user.Properties["manager"].Value = newUser["manager"];
        }
        if (updatedValues > 0 && logUpdateUser)
        {
            await LogUserUpdate(task, user, "UPDATE", "SUCCESSFUL", "SUCCESSFUL", csvRecord);
        }
        user.CommitChanges();
        return updatedValues;
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
                return new Dictionary<string, string>();
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
        Dictionary<string, string> creds = await GetCreds();
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
        Console.WriteLine(ldapFilterBuilder.ToString());
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
                word = "Modified";
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

    public async Task LogUserUpdate(NumpInstructionSet task, DirectoryEntry user, string attribute, string? oldValue, string newValue, Dictionary<string, object>? csvObject)
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
    public async Task LogUserCreate(NumpInstructionSet task, DirectoryEntry? user, string result, string? reason, Dictionary<string, object>? csvObject)
    {
        Guid guid = await GetGuid(user.Properties["objectGuid"].Value);

        UserCreationLog newLog = new UserCreationLog
        {
            DateTime = DateTime.Now,
            RunId = loggedTask.Guid,
            UserName = user != null ? user.Properties["userprincipalname"].Value.ToString() : "",
            UserGuid = guid,
            Result = result,
            Reason = reason != null ? reason : "",
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
}

