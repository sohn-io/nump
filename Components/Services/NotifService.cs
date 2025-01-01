using Microsoft.Identity.Client;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Net;
using System.Net.Mail;
using nump.Components.Classes;
using nump.Components.Database;
using System.DirectoryServices;
using System.Text.RegularExpressions;
using MailKit.Net.Smtp;
using MimeKit;
using MimeKit.Text;
using MailKit.Security;
namespace nump.Components.Services;
public partial class NotifService
{

    public async Task<string> SendEmailOAuth(NotificationData notification, string body, string clientId, string clientSecret, string tenantId, string userEmail)
    {

        // Set up the Microsoft Graph API endpoint and version

        string graphApiEndpoint = "https://graph.microsoft.com/v1.0";

        // Set up the client application ID and secret

        Microsoft.Identity.Client.AuthenticationResult? authResult = null;
        try
        {
            var authBuilder = ConfidentialClientApplicationBuilder.Create(clientId)
                .WithAuthority($"https://login.microsoftonline.com/{tenantId}/v2.0")
                .WithClientSecret(clientSecret)
                .Build();
            authResult = await authBuilder.AcquireTokenForClient(new[] { "https://graph.microsoft.com/.default" })
                .ExecuteAsync();
        }
        catch (Exception ex)
        {
            return "FAILED";
        }


        // Set up the HTTP client and add the access token to the authorization header
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);
        // Set up the email message
        var emailMessage = new
        {
            message = new
            {
                subject = notification.Subject,
                body = new
                {
                    contentType = notification.Type,
                    content = body
                },
                toRecipients = new List<dynamic>()
                {

                },
                ccRecipients = (notification.CcRecipients.Count() > 0) ? new List<dynamic>() : null,
                bccRecipients = (notification.BccRecipients.Count() > 0) ? new List<dynamic>() : null,

            }
        };
        if (notification.SendRecipientsList != null)
        {
            foreach (string recipient in notification.SendRecipientsList)
            {
                emailMessage.message.toRecipients.Add(
                    new
                    {
                        emailAddress = new
                        {
                            address = recipient
                        }
                    }
                );
            };
        }
        if (notification.CcRecipientsList != null)
        {
            foreach (string recipient in notification.CcRecipientsList)
            {
                emailMessage.message.ccRecipients.Add(
                    new
                    {
                        emailAddress = new
                        {
                            address = recipient
                        }
                    }
                );
            };
        }
        if (notification.BccRecipientsList != null)
        {
            foreach (string recipient in notification.BccRecipientsList)
            {
                emailMessage.message.bccRecipients.Add(
                    new
                    {
                        emailAddress = new
                        {
                            address = recipient
                        }
                    }
                );
            };
        }
        // Convert the email message to a JSON string and send the email via Microsoft Graph
        var jsonMessage = JsonConvert.SerializeObject(emailMessage);
        var response = await httpClient.PostAsync($"{graphApiEndpoint}/users/{userEmail}/sendMail", new StringContent(jsonMessage, System.Text.Encoding.UTF8, "application/json"));
        if (response.IsSuccessStatusCode)
        {
            return "SUCCESS";
        }
        else
        {
            Console.WriteLine("Failed to send email. Status code: " + response.StatusCode);

            return "FAILED";
        }
    }
    public async Task<string> SendEmailSMTP(NotificationData notification, string body, string smtpServer, int smtpPort, string smtpUser, string smtpPassword, int secureType)
    {

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(smtpUser, smtpUser));
        message.Subject = notification.Subject;
        // Create the email message
        foreach (string recipient in notification.SendRecipientsList)
        {
            message.To.Add(new MailboxAddress(recipient, recipient));
        }
        if (notification.CcRecipientsList != null)
        {
            foreach (string recipient in notification.CcRecipientsList)
            {
                message.Cc.Add(new MailboxAddress(recipient, recipient));
            }
        }
        if (notification.BccRecipientsList != null)
        {
            foreach (string recipient in notification.BccRecipientsList)
            {
                message.Bcc.Add(new MailboxAddress(recipient, recipient));
            }
        }

        message.Body = new TextPart(notification.Type)
        {
            Text = body
        };

        // Add recipient

        // Set up the SMTP client
        SecureSocketOptions secureOptions = SecureSocketOptions.Auto;

            switch (secureType)
            {
                case 1:
                    secureOptions = SecureSocketOptions.None;
                    break;

                case 2:
                    secureOptions = SecureSocketOptions.SslOnConnect;
                    break;

                case 3:
                    secureOptions = SecureSocketOptions.StartTls;
                    break;

                default:
                    secureOptions = SecureSocketOptions.Auto;
                    break;
            }

        using (var smtpClient = new MailKit.Net.Smtp.SmtpClient())
        {
            try
            {
                // Connect to the SMTP server (for example, Gmail)
                await smtpClient.ConnectAsync(smtpServer, smtpPort, secureOptions);

                // Authenticate with the SMTP server
                await smtpClient.AuthenticateAsync(smtpUser, smtpPassword);

                // Send the email
                await smtpClient.SendAsync(message);
                return "SUCCESS";

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending email: {ex.Message}");
                return "FAILED";
            }
            finally
            {
                // Disconnect and dispose of the client
                smtpClient.Disconnect(true);
                smtpClient.Dispose();
            }
        }
    }
}