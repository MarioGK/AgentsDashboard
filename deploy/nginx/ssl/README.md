# SSL Certificates

This directory should contain your TLS/SSL certificates for HTTPS termination.

## Required Files

- `cert.pem` - SSL certificate (full chain recommended)
- `key.pem` - Private key for the certificate

## Generating Self-Signed Certificates (Development)

For development and testing purposes, you can generate a self-signed certificate:

```bash
openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
  -keyout key.pem \
  -out cert.pem \
  -subj "/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"
```

## Using Let's Encrypt (Production)

For production, use Certbot to obtain certificates from Let's Encrypt:

### 1. Install Certbot

```bash
sudo apt-get update
sudo apt-get install certbot
```

### 2. Obtain Certificate (Standalone)

```bash
sudo certbot certonly --standalone -d yourdomain.com
```

### 3. Copy Certificates

```bash
sudo cp /etc/letsencrypt/live/yourdomain.com/fullchain.pem ./cert.pem
sudo cp /etc/letsencrypt/live/yourdomain.com/privkey.pem ./key.pem
sudo chown $USER:$USER ./cert.pem ./key.pem
chmod 600 ./key.pem
```

### 4. Auto-Renewal

Certbot sets up automatic renewal. To renew manually:

```bash
sudo certbot renew
sudo cp /etc/letsencrypt/live/yourdomain.com/fullchain.pem ./cert.pem
sudo cp /etc/letsencrypt/live/yourdomain.com/privkey.pem ./key.pem
```

## Using Existing Certificates

Copy your existing certificates:

```bash
cp /path/to/your/fullchain.pem ./cert.pem
cp /path/to/your/privkey.pem ./key.pem
chmod 600 ./key.pem
```

## Security Notes

- Never commit certificates to version control
- Ensure `key.pem` has restrictive permissions (600)
- Rotate certificates before expiration
- Use certificates from trusted CA for production
