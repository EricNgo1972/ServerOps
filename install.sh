#!/bin/bash
set -e

if command -v apt-get >/dev/null 2>&1; then
  sudo apt-get update
  sudo apt-get install -y dotnet-runtime-8.0 unzip curl
elif command -v dnf >/dev/null 2>&1; then
  sudo dnf install -y dotnet-runtime-8.0 unzip curl
fi

sudo mkdir -p /apps /apps/_history /apps/_logs /opt/serverops
sudo cp serverops.service /etc/systemd/system/serverops.service
sudo systemctl daemon-reload
sudo systemctl enable serverops.service
sudo systemctl restart serverops.service
