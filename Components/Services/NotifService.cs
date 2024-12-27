using Microsoft.Identity.Client;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Net;
using System.Net.Mail;
using nump.Components.Classes;
using nump.Components.Database;
using System.DirectoryServices;
using System.Text.RegularExpressions;

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
                subject = notification.header,
                body = new
                {
                    contentType = notification.type,
                    content = body
                },
                toRecipients = new List<dynamic>()
                {

                },
                ccRecipients = (notification.ccRecipients.Count() > 0) ? new List<dynamic>() : null,
                bccRecipients = (notification.bccRecipients.Count() > 0) ? new List<dynamic>() : null,

            }
        };
        if (notification.sendRecipientsList != null)
        {
            foreach (string recipient in notification.sendRecipientsList)
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
        if (notification.ccRecipientsList != null)
        {
            foreach (string recipient in notification.ccRecipientsList)
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
        if (notification.bccRecipientsList != null)
        {
            foreach (string recipient in notification.bccRecipientsList)
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
            Console.WriteLine("Email sent successfully.");
            return "SUCCESS";
        }
        else
        {
            Console.WriteLine("Failed to send email. Status code: " + response.StatusCode);

            return "FAILED";
        }
    }
    public async Task SendEmailSMTP()
    {
        // SMTP server details (Gmail in this example)
        string smtpServer = "smtp.gmail.com";
        int smtpPort = 587;  // Port for TLS/STARTTLS
        string smtpUser = "your-email@gmail.com"; // Your Gmail address
        string smtpPassword = "your-password";    // Your Gmail password

        // Create the email message
        var mailMessage = new MailMessage
        {
            From = new MailAddress(smtpUser),
            Subject = "Test Email from SMTP",
            Body = "This is a test email sent using SMTP in C#.",
            IsBodyHtml = false // Set to true if sending HTML content
        };
        mailMessage.To.Add("recipient@example.com"); // Add recipient

        // Set up the SMTP client
        var smtpClient = new SmtpClient(smtpServer, smtpPort)
        {
            Credentials = new NetworkCredential(smtpUser, smtpPassword), // Username & password
            EnableSsl = true,  // Enable SSL/TLS
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        try
        {
            // Send the email
            smtpClient.Send(mailMessage);
            Console.WriteLine("Email sent successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending email: {ex.Message}");
        }
    }
}