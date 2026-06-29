#!/bin/bash
# ─── E2E MQTT Flow Test ──────────────────────────────────────────────────────
# Tests the full USB Token signing flow through MQTT broker.
# Usage: ./scripts/test-e2e.sh [host] [port]
#
# Examples:
#   ./scripts/test-e2e.sh                          # local Docker (localhost:1883)
#   ./scripts/test-e2e.sh mqtt.intellisoftjsc.cloud 8883  # production

set -e

HOST=${1:-localhost}
PORT=${2:-1883}
AGENT_ID="e2e-test-$$"

# Detect mosquitto tools
if command -v mosquitto_pub &>/dev/null; then
  PUB=mosquitto_pub
  SUB=mosquitto_sub
elif [ -f /opt/homebrew/bin/mosquitto_pub ]; then
  PUB=/opt/homebrew/bin/mosquitto_pub
  SUB=/opt/homebrew/bin/mosquitto_sub
else
  echo "ERROR: mosquitto-clients not found. Install with: brew install mosquitto"
  exit 1
fi

# TLS args for production
TLS_ARGS=""
if [ "$PORT" = "8883" ]; then
  TLS_ARGS="--cafile /etc/ssl/cert.pem"
fi

echo "═══════════════════════════════════════════════════════════"
echo "  Vimes MQTT E2E Test"
echo "  Broker: $HOST:$PORT"
echo "  AgentId: $AGENT_ID"
echo "═══════════════════════════════════════════════════════════"
echo ""

# Step 1: Agent publishes presence
echo "[1/4] Agent publishes presence (retained)..."
$PUB -h $HOST -p $PORT $TLS_ARGS \
  -u vmsign-agent -P VMSignAgent@Sign2024 \
  -t "vmsignagent/$AGENT_ID/status" -r \
  -m "{\"service\":\"vimes-vmsign-agent\",\"agentId\":\"$AGENT_ID\",\"host\":\"test-pc\",\"httpPort\":9999,\"online\":true,\"certs\":[{\"serial\":\"TEST001\",\"subject\":\"CN=E2E Test\",\"algorithm\":\"RSA\",\"certificate\":\"MIIB\"}],\"ts\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"}"
echo "  ✓ Done"

# Step 2: SDK discovers agent
echo ""
echo "[2/4] SDK discovers agents..."
DISCOVERED=$($SUB -h $HOST -p $PORT $TLS_ARGS \
  -u sdk-server -P Sdk@Sign2024 \
  -t "vmsignagent/+/status" -W 3 -C 1 2>/dev/null)
if echo "$DISCOVERED" | grep -q "$AGENT_ID"; then
  echo "  ✓ Agent discovered: $AGENT_ID"
else
  echo "  ✗ FAILED: Agent not found"
  exit 1
fi

# Step 3: Agent listens + SDK sends sign request
echo ""
echo "[3/4] Sign request → response flow..."

# Agent listener (background) — receives req, sends response
(
  REQ=$($SUB -h $HOST -p $PORT $TLS_ARGS \
    -u vmsign-agent -P VMSignAgent@Sign2024 \
    -t "vmsignagent/$AGENT_ID/sign/req" -C 1 -W 10 2>/dev/null)
  if [ -n "$REQ" ]; then
    $PUB -h $HOST -p $PORT $TLS_ARGS \
      -u vmsign-agent -P VMSignAgent@Sign2024 \
      -t "vmsignagent/$AGENT_ID/sign/res" \
      -m '{"correlationId":"e2e-test-corr","success":true,"signatureBase64":"dGVzdC1zaWduYXR1cmU=","certificateBase64":"dGVzdC1jZXJ0","algorithm":"RSA","error":null}'
  fi
) &
AGENT_PID=$!
sleep 1

# SDK sends sign request
$PUB -h $HOST -p $PORT $TLS_ARGS \
  -u sdk-server -P Sdk@Sign2024 \
  -t "vmsignagent/$AGENT_ID/sign/req" \
  -m '{"correlationId":"e2e-test-corr","hashBase64":"dGVzdGhhc2g=","serial":"TEST001","pin":null}'
echo "  ✓ Sign request sent"

# Step 4: SDK receives response
echo ""
echo "[4/4] SDK receives sign response..."
RESPONSE=$($SUB -h $HOST -p $PORT $TLS_ARGS \
  -u sdk-server -P Sdk@Sign2024 \
  -t "vmsignagent/$AGENT_ID/sign/res" -C 1 -W 10 2>/dev/null)

kill $AGENT_PID 2>/dev/null
wait $AGENT_PID 2>/dev/null

if echo "$RESPONSE" | grep -q '"success":true'; then
  echo "  ✓ Response received: success=true"
else
  echo "  ✗ FAILED: No valid response"
  exit 1
fi

# Cleanup retained message
$PUB -h $HOST -p $PORT $TLS_ARGS \
  -u vmsign-agent -P VMSignAgent@Sign2024 \
  -t "vmsignagent/$AGENT_ID/status" -r -n 2>/dev/null || true

echo ""
echo "═══════════════════════════════════════════════════════════"
echo "  ✓ ALL TESTS PASSED"
echo "═══════════════════════════════════════════════════════════"
