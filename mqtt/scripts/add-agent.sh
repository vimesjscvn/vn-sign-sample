#!/bin/bash
# ─── Add a new MQTT agent account ────────────────────────────────────────────
# Usage: ./scripts/add-agent.sh <username> <password>
#
# For the shared-account approach, you don't need this script.
# Only use if you want per-agent accounts for stricter ACL control.

set -e

USERNAME=${1:?Usage: $0 <username> <password>}
PASSWORD=${2:?Usage: $0 <username> <password>}
PASSWD_FILE="$(dirname "$0")/../config/passwd"

if [ ! -f "$PASSWD_FILE" ]; then
  echo "ERROR: Password file not found at $PASSWD_FILE"
  exit 1
fi

mosquitto_passwd -b "$PASSWD_FILE" "$USERNAME" "$PASSWORD"
echo "✓ Added user '$USERNAME' to $PASSWD_FILE"
echo ""
echo "Remember to restart the broker:"
echo "  docker compose restart mqtt-broker"
echo ""
echo "And add ACL entry in config/acl:"
echo "  user $USERNAME"
echo "  topic readwrite vmsignagent/$USERNAME/#"
