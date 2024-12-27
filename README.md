# New User Management Process (NUMP)

## Overview
The **New User Management Process (NUMP)** is a system designed to handle the automatic creation, updating, and management of Active Directory (AD) user accounts. NUMP streamlines the onboarding process for new employees, ensuring that their AD accounts are created and configured correctly, with minimal manual intervention.

NUMP can also handle updates to existing user accounts, such as changes in role, department, or location, allowing for more efficient and secure management of user data within Active Directory.

## Key Features
- **Automatic Account Creation**: When a new user is onboarded, NUMP automatically creates their Active Directory account with the necessary attributes (e.g., username, department, email).
- **Account Updates**: NUMP can update existing accounts based on role changes, department reassignments, or other administrative changes, ensuring the information in AD is always up-to-date.
- **Audit and Reporting**: NUMP generates logs and reports for all user creation and update events for compliance and troubleshooting purposes.

## How It Works
- **Account Creation**: NUMP processes user data, generates a username, and creates an Active Directory account. Attributes specified in the data ingestion are automatically updated.
- **Scheduled Execution**: NUMP is scheduled to run automatically at a specified time (e.g., 9:00 AM daily). At this time, it processes the latest CSV file to create and update Active Directory accounts.
- **Account Updates**: When an existing user’s details change (e.g., department change or promotion), NUMP updates their AD account automatically to reflect the changes.
- **Logging and Notifications**: Every action performed by NUMP (account creation, modification) is logged for auditing purposes. Notifications can be sent to administrators about key actions, such as account creation or a failed update.

## Benefits
- **Reduced Manual Work**: NUMP automates the repetitive process of creating and updating AD accounts, saving time for IT administrators.
- **Consistency**: Automated processes reduce the chance of human error, ensuring that all users have consistent account configurations.
- **Faster Onboarding**: New users can quickly get access to necessary resources, enhancing productivity right from the start.
- **Improved Security**: By maintaining up-to-date and accurate user data, NUMP ensures that permissions and access are granted based on the latest user roles and department information.

### TODO
- **Terminations**: Termination Options, Map CSV Value to Account Status
- **Notifications**: Notify on User Create, Notify on User Update
- **Robust Account Finding**: Secondary field to identify user or manager account

## Installation Instructions
1. Enable IIS Server on your host machine
2. Install the ASP.Net Hosting Bundle from https://dotnet.microsoft.com/en-us/download/dotnet/8.0 (Under Windows --> Hosting Bundle)
3. Drop files into a folder (e.g. C:\nump)
4. Grant your IIS Application Pool Identity user (e.g. ApplicationPoolIdentity) Read/Write access to your NUMP folder.
5. Start IIS
