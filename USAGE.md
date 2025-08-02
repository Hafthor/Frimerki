# Frímerki Email Client Configuration Guide

This guide explains how to configure various email clients to work with a Frímerki email server. For this example, we'll assume your email server is hosted at **@frimerki.net**.

## 📧 Account Information

Before configuring your email client, you'll need the following information from your email administrator:

- **Email Address**: `your-username@frimerki.net`
- **Password**: Your account password
- **Server Address**: `mail.frimerki.net` (or the specific server address provided)

## 🔒 Connection Settings

Frímerki supports both encrypted and unencrypted connections. **We strongly recommend using encrypted connections** for security.

### Standard Ports (Unencrypted with STARTTLS)
| Protocol | Port | Security | Notes |
|----------|------|----------|-------|
| IMAP | 143 | STARTTLS | Recommended for receiving email |
| POP3 | 110 | STARTTLS | Alternative for receiving email |
| SMTP | 587 | STARTTLS | Recommended for sending email |

### SSL/TLS Ports (Encrypted)
| Protocol | Port | Security | Notes |
|----------|------|----------|-------|
| IMAP | 993 | SSL/TLS | Secure receiving |
| POP3 | 995 | SSL/TLS | Secure receiving |
| SMTP | 465 | SSL/TLS | Secure sending |

### Legacy Ports
| Protocol | Port | Security | Notes |
|----------|------|----------|-------|
| SMTP | 25 | None/STARTTLS | Server-to-server only |

## 📱 Email Client Configuration

### Apple Mail (macOS/iOS)

#### Automatic Setup
1. Open **Mail** application
2. Go to **Mail** → **Preferences** → **Accounts** (macOS) or **Settings** → **Mail** → **Accounts** (iOS)
3. Click **Add Account** → **Other Mail Account**
4. Enter:
   - **Name**: Your display name
   - **Email**: `your-username@frimerki.net`
   - **Password**: Your password
5. Click **Sign In**

#### Manual Setup (if automatic fails)
1. Choose **Other Mail Account**
2. Fill in your details and click **Next**
3. Configure **Incoming Mail Server**:
   - **Account Type**: IMAP
   - **Mail Server**: `mail.frimerki.net`
   - **User Name**: `your-username@frimerki.net`
   - **Password**: Your password
   - **Port**: `993`
   - **Use SSL**: Yes
4. Configure **Outgoing Mail Server**:
   - **SMTP Server**: `mail.frimerki.net`
   - **User Name**: `your-username@frimerki.net`
   - **Password**: Your password
   - **Port**: `587`
   - **Use SSL**: Yes
5. Click **Create**

### Outlook (Windows/macOS)

#### Outlook 2019/2021/365
1. Open **Outlook**
2. Go to **File** → **Add Account**
3. Enter your email address: `your-username@frimerki.net`
4. Click **Connect**
5. Choose **IMAP**
6. Configure settings:
   - **Incoming Mail**:
     - Server: `mail.frimerki.net`
     - Port: `993`
     - Encryption: SSL/TLS
   - **Outgoing Mail**:
     - Server: `mail.frimerki.net`
     - Port: `587`
     - Encryption: STARTTLS
7. Enter your password and click **Connect**

#### Manual Configuration
1. Go to **File** → **Account Settings** → **Account Settings**
2. Click **New** → **Manual setup**
3. Choose **Internet Email**
4. Configure:
   - **User Information**:
     - Your Name: Your display name
     - Email Address: `your-username@frimerki.net`
   - **Server Information**:
     - Account Type: IMAP
     - Incoming server: `mail.frimerki.net`
     - Outgoing server: `mail.frimerki.net`
   - **Logon Information**:
     - User Name: `your-username@frimerki.net`
     - Password: Your password
5. Click **More Settings** → **Advanced**
6. Set:
   - **Incoming server (IMAP)**: `993`, SSL
   - **Outgoing server (SMTP)**: `587`, TLS

### Thunderbird (Windows/macOS/Linux)

#### Automatic Setup
1. Open **Thunderbird**
2. Click **Set up an existing email account**
3. Enter:
   - **Your name**: Your display name
   - **Email address**: `your-username@frimerki.net`
   - **Password**: Your password
4. Click **Continue**
5. Thunderbird should automatically detect settings
6. Click **Done**

#### Manual Setup
1. Click **Manual config** during setup
2. Configure:
   - **Incoming Server**:
     - Protocol: IMAP
     - Server: `mail.frimerki.net`
     - Port: `993`
     - Connection security: SSL/TLS
     - Authentication: Normal password
   - **Outgoing Server**:
     - Server: `mail.frimerki.net`
     - Port: `587`
     - Connection security: STARTTLS
     - Authentication: Normal password
3. Click **Re-test** to verify settings
4. Click **Done**

### Gmail App (Android/iOS)

#### Using Gmail App
1. Open **Gmail** app
2. Tap **Add account**
3. Select **Other**
4. Enter your email: `your-username@frimerki.net`
5. Choose **Personal (IMAP)**
6. Enter your password
7. Configure **Incoming server settings**:
   - **Username**: `your-username@frimerki.net`
   - **Password**: Your password
   - **Server**: `mail.frimerki.net`
   - **Port**: `993`
   - **Security type**: SSL/TLS
8. Configure **Outgoing server settings**:
   - **SMTP server**: `mail.frimerki.net`
   - **Port**: `587`
   - **Security type**: STARTTLS
   - **Username**: `your-username@frimerki.net`
   - **Password**: Your password

### Android Native Email App

1. Open **Email** app
2. Tap **Add Account** → **Other**
3. Enter email and password
4. Choose **IMAP account**
5. Configure **Incoming settings**:
   - **IMAP server**: `mail.frimerki.net`
   - **Security type**: SSL/TLS
   - **Port**: `993`
   - **Username**: `your-username@frimerki.net`
6. Configure **Outgoing settings**:
   - **SMTP server**: `mail.frimerki.net`
   - **Security type**: STARTTLS
   - **Port**: `587`
   - **Username**: `your-username@frimerki.net`

### Windows Mail App

1. Open **Mail** app
2. Click **Add account**
3. Select **Other account**
4. Enter:
   - **Email address**: `your-username@frimerki.net`
   - **Password**: Your password
5. Click **Sign in**
6. If manual setup is needed:
   - **Account name**: Your display name
   - **Incoming email server**: `mail.frimerki.net:993:1`
   - **Outgoing email server**: `mail.frimerki.net:587:2`

## 🔧 Advanced Configuration

### IMAP vs POP3

**IMAP (Recommended)**
- ✅ Keeps emails synchronized across multiple devices
- ✅ Server-side folder management
- ✅ Better for mobile and multi-device usage
- ⚠️ Uses more server storage

**POP3**
- ✅ Downloads emails to local device
- ✅ Works offline after download
- ✅ Uses less server storage
- ⚠️ Not synchronized across devices

### Security Settings Comparison

| Security Type | Description | Recommended |
|---------------|-------------|-------------|
| **SSL/TLS** | Full encryption from start | ✅ **Best** |
| **STARTTLS** | Upgrades to encryption | ✅ **Good** |
| **None** | No encryption | ❌ **Avoid** |

### Folder Mapping

Frímerki uses standard IMAP folder names:
- **INBOX** - Incoming messages
- **Sent** - Sent messages
- **Drafts** - Draft messages
- **Trash** - Deleted messages
- **Junk** - Spam messages

### Connection Troubleshooting

#### Common Issues and Solutions

1. **"Cannot connect to server"**
   - ✅ Check server address: `mail.frimerki.net`
   - ✅ Verify port numbers (993 for IMAP SSL, 587 for SMTP STARTTLS)
   - ✅ Ensure internet connection is working

2. **"Authentication failed"**
   - ✅ Double-check username: `your-username@frimerki.net` (full email address)
   - ✅ Verify password is correct
   - ✅ Check if account is locked due to too many failed attempts

3. **"SSL/TLS connection failed"**
   - ✅ Try STARTTLS instead of SSL/TLS
   - ✅ Use port 143 with STARTTLS for IMAP
   - ✅ Use port 587 with STARTTLS for SMTP

4. **"Server certificate error"**
   - ✅ Check if your server uses a self-signed certificate
   - ✅ Add security exception if you trust the server
   - ✅ Contact your administrator for proper SSL certificate

#### Testing Connection

You can test your connection using command-line tools:

**Test IMAP connection:**
```bash
# SSL connection
openssl s_client -connect mail.frimerki.net:993 -quiet

# STARTTLS connection
openssl s_client -connect mail.frimerki.net:143 -starttls imap -quiet
```

**Test SMTP connection:**
```bash
# STARTTLS connection
openssl s_client -connect mail.frimerki.net:587 -starttls smtp -quiet

# SSL connection
openssl s_client -connect mail.frimerki.net:465 -quiet
```

## 📋 Quick Reference

### Recommended Settings Summary

| Setting | Value |
|---------|-------|
| **Email** | `your-username@frimerki.net` |
| **Incoming Server** | `mail.frimerki.net` |
| **Incoming Port** | `993` (IMAP SSL) or `143` (IMAP STARTTLS) |
| **Incoming Security** | SSL/TLS or STARTTLS |
| **Outgoing Server** | `mail.frimerki.net` |
| **Outgoing Port** | `587` (STARTTLS) or `465` (SSL) |
| **Outgoing Security** | STARTTLS or SSL/TLS |
| **Authentication** | Normal password |
| **Username** | Full email address |

### Mobile Quick Setup

For mobile devices, use these settings for fastest setup:
- **IMAP**: `mail.frimerki.net:993` (SSL/TLS)
- **SMTP**: `mail.frimerki.net:587` (STARTTLS)
- **Username**: Full email address
- **Authentication**: Normal password

## 🆘 Support

If you continue to experience issues:

1. **Check server status** - Contact your email administrator
2. **Firewall/Antivirus** - Temporarily disable to test connectivity
3. **Corporate networks** - May block email ports (check with IT department)
4. **Alternative ports** - Some ISPs block standard email ports

### Contact Information

For technical support with your Frímerki email account:
- Contact your email administrator
- Check server status at your organization's status page
- Review firewall settings if on a corporate network

---

**Note**: Replace `mail.frimerki.net` and `your-username@frimerki.net` with the actual server address and email account provided by your administrator.
