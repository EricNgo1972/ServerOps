#!/bin/bash

set -euo pipefail

APP_NAME=$1
ZIP_PATH=$2
TARGET_DIR=/opt/serverops/apps/$APP_NAME

echo "Stopping service..."
systemctl stop "$APP_NAME"

echo "Backup..."
if [ -d "$TARGET_DIR/current" ]; then
  cp -r "$TARGET_DIR/current" "$TARGET_DIR/backup_$(date +%s)"
fi

echo "Deploy..."
rm -rf "$TARGET_DIR/current"
mkdir -p "$TARGET_DIR/current"
unzip "$ZIP_PATH" -d "$TARGET_DIR/current"

echo "Starting..."
systemctl start "$APP_NAME"

echo "Done"
