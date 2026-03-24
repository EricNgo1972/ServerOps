#!/bin/bash
set -e

if [ "$(id -u)" -ne 0 ]; then
  echo "install.sh must be run as root."
  exit 1
fi

if command -v apt-get >/dev/null 2>&1; then
  apt-get update
  apt-get install -y dotnet-runtime-8.0 unzip curl
elif command -v dnf >/dev/null 2>&1; then
  dnf install -y dotnet-runtime-8.0 unzip curl
fi

mkdir -p /apps /apps/_history /apps/_logs /opt/serverops
cp serverops.service /etc/systemd/system/serverops.service
systemctl daemon-reload
systemctl enable serverops.service
systemctl restart serverops.service
