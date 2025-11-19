# Systemd Deployment Guide

## Prerequisites

- .NET 9.0 Runtime installed on the target system
- systemd-based Linux distribution (Ubuntu, Debian, RHEL, etc.)
- sudo/root access

## Installation Steps

### 1. Publish the Application

```bash
dotnet publish -c Release -o /opt/claude-memory-api
```

### 2. Create Data Directory

```bash
sudo mkdir -p /var/lib/claude-memory
sudo chown www-data:www-data /var/lib/claude-memory
```

### 3. Copy Systemd Unit File

```bash
sudo cp deployment/systemd/claude-memory-api.service /etc/systemd/system/
```

### 4. Reload Systemd and Enable Service

```bash
sudo systemctl daemon-reload
sudo systemctl enable claude-memory-api.service
```

### 5. Start the Service

```bash
sudo systemctl start claude-memory-api.service
```

### 6. Check Status

```bash
sudo systemctl status claude-memory-api.service
```

## Configuration

Edit the service file to customize:

- `User` and `Group`: Change from `www-data` to your preferred user
- `WorkingDirectory`: Path to published application
- `Environment` variables: Configure memory options
- `Memory__DataDirectory`: Path to data storage
- `Memory__StorageEngine`: `simple` or `wal`
- Other Memory__* settings as needed

## Logs

View logs using journalctl:

```bash
# Follow logs in real-time
sudo journalctl -u claude-memory-api -f

# View last 100 lines
sudo journalctl -u claude-memory-api -n 100

# View logs since boot
sudo journalctl -u claude-memory-api -b
```

## Maintenance

```bash
# Stop service
sudo systemctl stop claude-memory-api.service

# Restart service
sudo systemctl restart claude-memory-api.service

# Disable service (prevent auto-start on boot)
sudo systemctl disable claude-memory-api.service
```

## Security Hardening

The provided service file includes basic security settings:
- `NoNewPrivileges=true`: Prevents privilege escalation
- `PrivateTmp=true`: Isolated /tmp directory

For additional hardening, consider:

```ini
[Service]
ReadWritePaths=/var/lib/claude-memory
ProtectSystem=strict
ProtectHome=true
PrivateDevices=true
```

Add these under the `[Service]` section in the unit file.

## Reverse Proxy Setup (Nginx Example)

```nginx
server {
    listen 80;
    server_name your-domain.com;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

## Troubleshooting

### Service won't start

```bash
# Check service status
sudo systemctl status claude-memory-api.service

# Check for errors in logs
sudo journalctl -u claude-memory-api -n 50 --no-pager
```

### Permission issues

Ensure the service user has read/write access to:
- Application directory (`/opt/claude-memory-api`)
- Data directory (`/var/lib/claude-memory`)

```bash
sudo chown -R www-data:www-data /opt/claude-memory-api
sudo chown -R www-data:www-data /var/lib/claude-memory
```
