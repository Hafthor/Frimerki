# Frímerki Email Server Installation Guide

**Frímerki** is a lightweight, self-contained email server built with C# and .NET 8. This guide covers installation, configuration, and security setup, with special emphasis on SSL/TLS encryption support.

## 📋 Prerequisites

### System Requirements
- **Operating System**: Linux, Windows, or macOS
- **Runtime**: .NET 8 SDK or Runtime
- **Memory**: Minimum 512MB RAM (1GB+ recommended)
- **Storage**: 1GB+ available disk space
- **Network**: Access to standard email ports (25, 143, 110, 587, 993, 995)

### Dependencies
- .NET 8 SDK (for building from source)
- SQLite (included with .NET)
- Optional: Reverse proxy (nginx, Apache) for production SSL termination

## 🚀 Installation Methods

### Method 1: Build from Source

1. **Clone the repository:**
   ```bash
   git clone https://github.com/Hafthor/Frimerki.git
   cd Frimerki
   ```

2. **Restore dependencies:**
   ```bash
   dotnet restore
   ```

3. **Build the application:**
   ```bash
   dotnet build --configuration Release
   ```

4. **Run the server:**
   ```bash
   dotnet run --project src/Frimerki.Server --configuration Release
   ```

### Method 2: Published Release (Recommended for Production)

1. **Publish a self-contained release:**
   ```bash
   dotnet publish src/Frimerki.Server -c Release -r linux-x64 --self-contained -o ./publish
   ```

2. **Deploy to target server:**
   ```bash
   # Copy published files to server
   scp -r ./publish/* user@server:/opt/frimerki/

   # On the server, make executable
   chmod +x /opt/frimerki/Frimerki.Server
   ```

3. **Run the application:**
   ```bash
   cd /opt/frimerki
   ./Frimerki.Server
   ```

## ⚙️ Configuration

### Basic Configuration

Frímerki uses `appsettings.json` for configuration. Key settings include:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=frimerki.db",
    "GlobalConnection": "Data Source=frimerki_global.db",
    "DomainDatabasePath": "./data/domains/"
  },
  "Server": {
    "MaxMessageSize": "25MB",
    "StorageQuotaPerUser": "1GB",
    "EnableSMTP": true,
    "EnableIMAP": true,
    "EnablePOP3": true,
    "EnableWebAPI": true
  },
  "Ports": {
    "SMTP": 25,
    "SMTP_TLS": 587,
    "SMTP_SSL": 465,
    "IMAP": 143,
    "IMAP_SSL": 993,
    "POP3": 110,
    "POP3_SSL": 995,
    "WebAPI": 8080,
    "WebAPI_SSL": 8443
  }
}
```

### Directory Structure

After installation, create the following directory structure:

```
/opt/frimerki/
├── Frimerki.Server                 # Main executable
├── appsettings.json               # Main configuration
├── appsettings.Production.json    # Production overrides
├── data/
│   ├── domains/                   # Domain-specific databases
│   └── attachments/              # Email attachments
├── certs/                        # SSL certificates
├── logs/                         # Application logs
└── backups/                      # Database backups
```

Create directories:
```bash
mkdir -p /opt/frimerki/{data/domains,data/attachments,certs,logs,backups}
chown -R frimerki:frimerki /opt/frimerki
```

## 🔐 SSL/TLS Encryption Setup

Frímerki supports multiple approaches for securing email communications:

### Option 1: Self-Signed Certificates (Development/Testing)

1. **Generate self-signed certificates:**
   ```bash
   # Create certificate directory
   mkdir -p /opt/frimerki/certs
   cd /opt/frimerki/certs

   # Generate private key
   openssl genrsa -out frimerki.key 2048

   # Generate certificate signing request
   openssl req -new -key frimerki.key -out frimerki.csr \
     -subj "/C=US/ST=State/L=City/O=Organization/CN=mail.yourdomain.com"

   # Generate self-signed certificate
   openssl x509 -req -days 365 -in frimerki.csr -signkey frimerki.key -out frimerki.crt

   # Create PFX bundle (required for .NET)
   openssl pkcs12 -export -out frimerki.pfx -inkey frimerki.key -in frimerki.crt
   ```

2. **Configure SSL in appsettings.json:**
   ```json
   {
     "Security": {
       "RequireSSL": true,
       "CertificatePath": "./certs/",
       "CertificateFileName": "frimerki.pfx",
       "CertificatePassword": "your-certificate-password"
     }
   }
   ```

### Option 2: Let's Encrypt Certificates (Production)

1. **Install Certbot:**
   ```bash
   # Ubuntu/Debian
   sudo apt update && sudo apt install certbot

   # CentOS/RHEL
   sudo yum install certbot
   ```

2. **Obtain certificates:**
   ```bash
   # Stop Frímerki temporarily
   sudo systemctl stop frimerki

   # Obtain certificate
   sudo certbot certonly --standalone -d mail.yourdomain.com

   # Convert to PFX format
   sudo openssl pkcs12 -export -out /opt/frimerki/certs/frimerki.pfx \
     -inkey /etc/letsencrypt/live/mail.yourdomain.com/privkey.pem \
     -in /etc/letsencrypt/live/mail.yourdomain.com/cert.pem \
     -certfile /etc/letsencrypt/live/mail.yourdomain.com/chain.pem

   # Set permissions
   sudo chown frimerki:frimerki /opt/frimerki/certs/frimerki.pfx
   sudo chmod 600 /opt/frimerki/certs/frimerki.pfx
   ```

3. **Setup automatic renewal:**
   ```bash
   # Create renewal script
   sudo tee /etc/cron.d/frimerki-ssl-renewal > /dev/null <<EOF
   0 2 * * * root /usr/bin/certbot renew --quiet && /opt/frimerki/scripts/update-ssl.sh
   EOF
   ```

4. **Create SSL update script:**
   ```bash
   sudo tee /opt/frimerki/scripts/update-ssl.sh > /dev/null <<'EOF'
   #!/bin/bash
   cd /opt/frimerki/certs
   openssl pkcs12 -export -out frimerki.pfx.new \
     -inkey /etc/letsencrypt/live/mail.yourdomain.com/privkey.pem \
     -in /etc/letsencrypt/live/mail.yourdomain.com/cert.pem \
     -certfile /etc/letsencrypt/live/mail.yourdomain.com/chain.pem \
     -password pass:your-certificate-password

   if [ $? -eq 0 ]; then
     mv frimerki.pfx.new frimerki.pfx
     chown frimerki:frimerki frimerki.pfx
     chmod 600 frimerki.pfx
     systemctl restart frimerki
   fi
   EOF

   sudo chmod +x /opt/frimerki/scripts/update-ssl.sh
   ```

### Option 3: Reverse Proxy SSL Termination (Recommended for Production)

Using nginx or Apache to handle SSL termination provides better performance and easier certificate management.

**Nginx Configuration:**
```nginx
# /etc/nginx/sites-available/frimerki
upstream frimerki_api {
    server 127.0.0.1:8080;
}

# HTTPS for Web API
server {
    listen 443 ssl http2;
    server_name mail.yourdomain.com;

    ssl_certificate /etc/letsencrypt/live/mail.yourdomain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/mail.yourdomain.com/privkey.pem;

    # Modern SSL configuration
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384;
    ssl_prefer_server_ciphers off;

    location / {
        proxy_pass http://frimerki_api;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}

# HTTP redirect
server {
    listen 80;
    server_name mail.yourdomain.com;
    return 301 https://$server_name$request_uri;
}
```

**Stream proxy for email protocols:**
```nginx
# /etc/nginx/nginx.conf - add to main context
stream {
    # SMTP with STARTTLS
    server {
        listen 587;
        proxy_pass 127.0.0.1:587;
        proxy_timeout 1s;
        proxy_responses 1;
    }

    # IMAP with STARTTLS
    server {
        listen 143;
        proxy_pass 127.0.0.1:143;
        proxy_timeout 1s;
        proxy_responses 1;
    }

    # POP3 with STARTTLS
    server {
        listen 110;
        proxy_pass 127.0.0.1:110;
        proxy_timeout 1s;
        proxy_responses 1;
    }
}
```

## 🛡️ Security Configuration

### Firewall Setup

Configure firewall to allow email ports:

```bash
# UFW (Ubuntu)
sudo ufw allow 25/tcp    # SMTP
sudo ufw allow 587/tcp   # SMTP with STARTTLS
sudo ufw allow 465/tcp   # SMTP over SSL
sudo ufw allow 143/tcp   # IMAP
sudo ufw allow 993/tcp   # IMAP over SSL
sudo ufw allow 110/tcp   # POP3
sudo ufw allow 995/tcp   # POP3 over SSL
sudo ufw allow 80/tcp    # HTTP (for Let's Encrypt)
sudo ufw allow 443/tcp   # HTTPS

# iptables (CentOS/RHEL)
sudo iptables -A INPUT -p tcp --dport 25 -j ACCEPT
sudo iptables -A INPUT -p tcp --dport 587 -j ACCEPT
sudo iptables -A INPUT -p tcp --dport 465 -j ACCEPT
sudo iptables -A INPUT -p tcp --dport 143 -j ACCEPT
sudo iptables -A INPUT -p tcp --dport 993 -j ACCEPT
sudo iptables -A INPUT -p tcp --dport 110 -j ACCEPT
sudo iptables -A INPUT -p tcp --dport 995 -j ACCEPT
sudo iptables -A INPUT -p tcp --dport 80 -j ACCEPT
sudo iptables -A INPUT -p tcp --dport 443 -j ACCEPT
```

### Production Security Settings

```json
{
  "Security": {
    "RequireSSL": true,
    "EnableRateLimit": true,
    "MaxFailedLogins": 3,
    "JWTSecret": "your-very-secure-256-bit-key-here",
    "PasswordMinLength": 12,
    "PasswordRequireSpecialChars": true,
    "SessionTimeout": "24h"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Frimerki.Security": "Warning"
    }
  }
}
```

## 🔧 System Service Setup

### Systemd Service (Linux)

1. **Create user account:**
   ```bash
   sudo useradd -r -s /bin/false frimerki
   sudo usermod -a -G ssl-cert frimerki  # For certificate access
   ```

2. **Create systemd service:**
   ```bash
   sudo tee /etc/systemd/system/frimerki.service > /dev/null <<EOF
   [Unit]
   Description=Frimerki Email Server
   After=network.target

   [Service]
   Type=notify
   User=frimerki
   Group=frimerki
   WorkingDirectory=/opt/frimerki
   ExecStart=/opt/frimerki/Frimerki.Server
   ExecReload=/bin/kill -HUP \$MAINPID
   Restart=always
   RestartSec=10
   SyslogIdentifier=frimerki

   # Security settings
   NoNewPrivileges=yes
   ProtectSystem=strict
   ProtectHome=yes
   ReadWritePaths=/opt/frimerki/data /opt/frimerki/logs

   # Environment
   Environment=ASPNETCORE_ENVIRONMENT=Production
   Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

   [Install]
   WantedBy=multi-user.target
   EOF
   ```

3. **Enable and start service:**
   ```bash
   sudo systemctl daemon-reload
   sudo systemctl enable frimerki
   sudo systemctl start frimerki
   sudo systemctl status frimerki
   ```

### Windows Service

1. **Install as Windows service:**
   ```powershell
   # Using sc command
   sc create Frimerki binPath="C:\Program Files\Frimerki\Frimerki.Server.exe" start=auto
   sc description Frimerki "Frimerki Email Server"

   # Start the service
   sc start Frimerki
   ```

2. **Or use NSSM (Non-Sucking Service Manager):**
   ```powershell
   # Download and install NSSM from https://nssm.cc/
   nssm install Frimerki "C:\Program Files\Frimerki\Frimerki.Server.exe"
   nssm set Frimerki AppDirectory "C:\Program Files\Frimerki"
   nssm start Frimerki
   ```

## 📧 Initial Setup and Testing

### 1. First Run Configuration

1. **Start the server:**
   ```bash
   sudo systemctl start frimerki
   ```

2. **Access the web interface:**
   - Open browser to `https://mail.yourdomain.com`
   - Or `http://localhost:8080` for local testing

3. **Create initial domain:**
   ```bash
   curl -X POST https://mail.yourdomain.com/api/domains \
     -H "Content-Type: application/json" \
     -d '{"name": "yourdomain.com", "isActive": true}'
   ```

4. **Create first user:**
   ```bash
   curl -X POST https://mail.yourdomain.com/api/users \
     -H "Content-Type: application/json" \
     -d '{
       "email": "admin@yourdomain.com",
       "password": "SecurePassword123!",
       "domainId": 1,
       "role": "DomainAdmin"
     }'
   ```

### 2. Email Client Configuration

**IMAP Settings:**
- **Server**: mail.yourdomain.com
- **Port**: 143 (STARTTLS) or 993 (SSL/TLS)
- **Security**: STARTTLS or SSL/TLS
- **Authentication**: Normal password

**SMTP Settings:**
- **Server**: mail.yourdomain.com
- **Port**: 587 (STARTTLS) or 465 (SSL/TLS)
- **Security**: STARTTLS or SSL/TLS
- **Authentication**: Normal password

**POP3 Settings:**
- **Server**: mail.yourdomain.com
- **Port**: 110 (STARTTLS) or 995 (SSL/TLS)
- **Security**: STARTTLS or SSL/TLS
- **Authentication**: Normal password

### 3. Testing Email Flow

1. **Test SMTP (sending):**
   ```bash
   echo "Subject: Test Email

   This is a test email." | sendmail -f test@yourdomain.com recipient@example.com
   ```

2. **Test IMAP (reading):**
   ```bash
   # Using openssl for secure connection
   openssl s_client -connect mail.yourdomain.com:993 -quiet
   # Then run IMAP commands:
   # A1 LOGIN user@yourdomain.com password
   # A2 SELECT INBOX
   # A3 FETCH 1 BODY[]
   ```

## 📊 Monitoring and Maintenance

### Log Management

Logs are written to:
- **Application logs**: `/opt/frimerki/logs/frimerki-YYYY-MM-DD.txt`
- **System logs**: `journalctl -u frimerki`

### Health Monitoring

1. **Health check endpoint:**
   ```bash
   curl https://mail.yourdomain.com/api/health
   ```

2. **Monitor service status:**
   ```bash
   # Service status
   sudo systemctl status frimerki

   # View recent logs
   sudo journalctl -u frimerki -f

   # Check listening ports
   sudo netstat -tlnp | grep Frimerki
   ```

### Backup Strategy

1. **Database backup script:**
   ```bash
   #!/bin/bash
   BACKUP_DIR="/opt/frimerki/backups"
   DATE=$(date +%Y%m%d_%H%M%S)

   # Backup global database
   sqlite3 /opt/frimerki/frimerki_global.db ".backup $BACKUP_DIR/global_$DATE.db"

   # Backup domain databases
   find /opt/frimerki/data/domains -name "*.db" -exec cp {} $BACKUP_DIR/ \;

   # Compress old backups
   find $BACKUP_DIR -name "*.db" -mtime +7 -exec gzip {} \;

   # Remove backups older than 30 days
   find $BACKUP_DIR -name "*.gz" -mtime +30 -delete
   ```

2. **Add to crontab:**
   ```bash
   # Backup databases daily at 2 AM
   0 2 * * * /opt/frimerki/scripts/backup.sh
   ```

## 🚨 Troubleshooting

### Common Issues

1. **Port binding errors:**
   ```bash
   # Check if ports are available
   sudo netstat -tlnp | grep :25
   sudo netstat -tlnp | grep :587

   # Change ports in appsettings.json if needed
   ```

2. **SSL certificate issues:**
   ```bash
   # Test certificate
   openssl x509 -in /opt/frimerki/certs/frimerki.crt -text -noout

   # Test SSL connection
   openssl s_client -connect mail.yourdomain.com:993
   ```

3. **Database connection issues:**
   ```bash
   # Check database permissions
   ls -la /opt/frimerki/*.db

   # Test database connectivity
   sqlite3 /opt/frimerki/frimerki_global.db "SELECT name FROM sqlite_master WHERE type='table';"
   ```

4. **Authentication failures:**
   ```bash
   # Check user exists
   sqlite3 /opt/frimerki/data/domains/domain_yourdomain_com.db "SELECT * FROM Users WHERE Email='user@yourdomain.com';"

   # Reset user password via API
   curl -X PUT https://mail.yourdomain.com/api/users/1/password \
     -H "Content-Type: application/json" \
     -d '{"newPassword": "NewSecurePassword123!"}'
   ```

### Performance Tuning

1. **SQLite optimization:**
   ```bash
   # Add to appsettings.json
   "ConnectionStrings": {
     "DefaultConnection": "Data Source=frimerki.db;Cache=Shared;Journal Mode=WAL;Synchronous=Normal;"
   }
   ```

2. **Memory settings:**
   ```bash
   # Increase available memory for .NET
   export DOTNET_GCHeapHardLimit=800000000  # 800MB
   ```

## 🔗 Additional Resources

- **Project Repository**: [GitHub - Frimerki](https://github.com/Hafthor/Frimerki)
- **API Documentation**: Available at `/swagger` endpoint
- **Protocol Documentation**: See `SPECIFICATION.md`
- **Security Guide**: See `SECURITY.md`

## 🆘 Support

For issues and support:
1. Check the troubleshooting section above
2. Review application logs in `/opt/frimerki/logs/`
3. Check systemd logs with `journalctl -u frimerki`
4. Create an issue on the GitHub repository

---

**Note**: This installation guide assumes a production environment. For development, see the simplified setup in `README.md`.
