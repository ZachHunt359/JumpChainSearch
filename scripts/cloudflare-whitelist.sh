#!/bin/bash
#
# Cloudflare IP Whitelist Script
# Allows Cloudflare IPs through UFW firewall
#
# This is necessary because:
# 1. Your domain uses Cloudflare proxy (orange cloud)
# 2. All traffic comes through Cloudflare, not directly to your server
# 3. Monitoring services like UptimeRobot also go through Cloudflare

echo "=========================================="
echo "Cloudflare IP Whitelist for UFW"
echo "=========================================="
echo ""

# Cloudflare IPv4 ranges (from https://www.cloudflare.com/ips-v4/)
CF_IPV4=(
  "173.245.48.0/20"
  "103.21.244.0/22"
  "103.22.200.0/22"
  "103.31.4.0/22"
  "141.101.64.0/18"
  "108.162.192.0/18"
  "190.93.240.0/20"
  "188.114.96.0/20"
  "197.234.240.0/22"
  "198.41.128.0/17"
  "162.158.0.0/15"
  "104.16.0.0/13"
  "104.24.0.0/14"
  "172.64.0.0/13"
  "131.0.72.0/22"
)

# Cloudflare IPv6 ranges (from https://www.cloudflare.com/ips-v6/)
CF_IPV6=(
  "2400:cb00::/32"
  "2606:4700::/32"
  "2803:f800::/32"
  "2405:b500::/32"
  "2405:8100::/32"
  "2a06:98c0::/29"
  "2c0f:f248::/32"
)

echo "Adding Cloudflare IPv4 ranges..."
for ip in "${CF_IPV4[@]}"; do
  echo "  Allowing $ip"
  sudo ufw allow from $ip to any port 80 proto tcp comment 'Cloudflare IPv4'
  sudo ufw allow from $ip to any port 443 proto tcp comment 'Cloudflare IPv4'
done

echo ""
echo "Adding Cloudflare IPv6 ranges..."
for ip in "${CF_IPV6[@]}"; do
  echo "  Allowing $ip"
  sudo ufw allow from $ip to any port 80 proto tcp comment 'Cloudflare IPv6'
  sudo ufw allow from $ip to any port 443 proto tcp comment 'Cloudflare IPv6'
done

echo ""
echo "=========================================="
echo "Cloudflare IPs whitelisted successfully!"
echo "=========================================="
echo ""
echo "Note: With Cloudflare proxy enabled, ALL traffic comes through"
echo "Cloudflare IPs, including monitoring services like UptimeRobot."
echo ""
echo "To verify: sudo ufw status numbered"