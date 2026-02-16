#!/usr/bin/env bash
set -e

SOLUTION_PATH="$(cd "$(dirname "$0")" && pwd)/src/AgentsDashboard.slnx"

apt-get update
apt-get install -y wget ca-certificates curl docker.io docker-compose-plugin

wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0 --install-dir /root/.dotnet

export DOTNET_ROOT=/root/.dotnet
export PATH="$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools"

rm -f dotnet-install.sh

echo 'export DOTNET_ROOT=/root/.dotnet' >> "$HOME/.bashrc"
echo 'export PATH=$PATH:/root/.dotnet:/root/.dotnet/tools' >> "$HOME/.bashrc"

dotnet --list-sdks
