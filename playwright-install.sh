#!/usr/bin/env bash
# Script to install Playwright browsers for the AgentsDashboard.PlaywrightTests project
# Usage: ./playwright-install.sh [chromium|firefox|webkit]

set -e

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEST_PROJECT="$PROJECT_DIR/tests/AgentsDashboard.PlaywrightTests"
PLAYWRIGHT_NODE="$TEST_PROJECT/bin/Debug/net10.0/.playwright/node/linux-x64/node"
PLAYWRIGHT_CLI="$TEST_PROJECT/bin/Debug/net10.0/.playwright/package/cli.js"

# Build the test project if needed
if [ ! -f "$PLAYWRIGHT_NODE" ]; then
    echo "Building Playwright test project..."
    dotnet build "$TEST_PROJECT/AgentsDashboard.PlaywrightTests.csproj" -v q
fi

# Default browsers to install
BROWSERS="${1:-chromium}"

echo "Installing Playwright browsers: $BROWSERS"
"$PLAYWRIGHT_NODE" "$PLAYWRIGHT_CLI" install $BROWSERS

echo "Playwright browsers installed successfully!"
echo "Installed browsers:"
ls -la ~/.cache/ms-playwright/ | grep "^d" | grep -v "^\.$\|^\.\.$" | awk '{print $NF}'
