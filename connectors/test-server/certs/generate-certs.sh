#!/bin/bash

# TLS Certificate Generation Script for PlayHouse Test Server
# This script generates self-signed certificates for development and testing purposes.
# DO NOT use these certificates in production environments.

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

echo "===================================="
echo "PlayHouse Test Server"
echo "TLS Certificate Generation"
echo "===================================="
echo ""

# Clean up existing certificates
echo "[1/3] Cleaning up existing certificates..."
rm -f server.key server.crt server.pfx

# Generate RSA private key and self-signed certificate
echo "[2/3] Generating RSA key and self-signed certificate..."
openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
  -keyout server.key \
  -out server.crt \
  -subj "/C=KR/ST=Seoul/L=Seoul/O=PlayHouse/OU=Development/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,DNS:test-server,DNS:playhouse-test-server,IP:127.0.0.1,IP:0.0.0.0"

# Convert to PFX format for .NET (with password: password)
echo "[3/3] Converting to PFX format for .NET..."
openssl pkcs12 -export \
  -out server.pfx \
  -inkey server.key \
  -in server.crt \
  -passout pass:password

echo ""
echo "===================================="
echo "Certificate generation completed!"
echo "===================================="
echo ""
echo "Generated files:"
echo "  - server.key  (Private key)"
echo "  - server.crt  (Certificate)"
echo "  - server.pfx  (PKCS#12 for .NET, password: password)"
echo ""
echo "Certificate details:"
echo "  - Valid for: 365 days"
echo "  - Common Name: localhost"
echo "  - Subject Alternative Names: localhost, test-server, playhouse-test-server, 127.0.0.1, 0.0.0.0"
echo ""
echo "Usage:"
echo "  The certificates are automatically used by docker-compose.yml"
echo "  PFX password is set to 'password' in docker-compose.yml environment variables"
echo ""
echo "WARNING: These are self-signed certificates for development only!"
echo "         DO NOT use in production environments."
echo ""
