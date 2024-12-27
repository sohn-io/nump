# New User Management Process (NUMP)

## Overview
The **New User Management Process (NUMP)** is a system designed to handle the automatic creation, updating, and management of Active Directory (AD) user accounts. NUMP streamlines the onboarding process for new employees, ensuring that their AD accounts are created and configured correctly, with minimal manual intervention.

NUMP can also handle updates to existing user accounts, such as changes in role, department, or location, allowing for more efficient and secure management of user data within Active Directory.

## Key Features
- **Automatic Account Creation**: When a new user is onboarded, NUMP automatically creates their Active Directory account with the necessary attributes (e.g., username, department, email).
- **Account Updates**: NUMP can update existing accounts based on role changes, department reassignments, or other administrative changes, ensuring the information in AD is always up-to-date.
- **Audit and Reporting**: NUMP generates logs and reports for all user creation and update events for compliance and troubleshooting purposes.
- **Integration with HR Systems**: NUMP can integrate with Human Resources (HR) software via.

## How It Works
- **Data Input**: User data, such as first name, last name, job title, department, and email,
- **Account Creation**: NUMP processes the data, generates a username, and creates an Active Directory account. Attributes such as email, phone number, department are automatically populated.
- **Scheduled Execution**: NUMP is scheduled to run automatically at a specified time (e.g., 9:00 AM daily). At this time, it processes the latest CSV file to create and update Active Directory accounts.
- **Account Updates**: When an existing user’s details change (e.g., department change or promotion), NUMP updates their AD account automatically to reflect the changes.
- **Logging and Notifications**: Every action performed by NUMP (account creation, modification) is logged for auditing purposes. Notifications can be sent to administrators about key actions, such as account creation or a failed update.

## Benefits
- **Reduced Manual Work**: NUMP automates the repetitive process of creating and updating AD accounts, saving time for IT administrators.
- **Consistency**: Automated processes reduce the chance of human error, ensuring that all users have consistent account configurations.
- **Faster Onboarding**: New users can quickly get access to necessary resources, enhancing productivity right from the start.
- **Improved Security**: By maintaining up-to-date and accurate user data, NUMP ensures that permissions and access are granted based on the latest user roles and department information.

### TODO:
- **Terminations**: Termination Options, Map CSV Value to Account Status
- **Notifications**: Notify on User Create, Notify on User Update
- **Robust Account Finding**: Secondary field to identify user or manager account
