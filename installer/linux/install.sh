#!/usr/bin/env bash
set -e

# VidShrink Linux Installation Script
# Usage:
#   ./install.sh                (Installs for current user to ~/.local/share/vidshrink)
#   sudo ./install.sh --system  (Installs system-wide to /opt/vidshrink)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_BIN=""

# Locate the published binary
if [ -f "$SCRIPT_DIR/VidShrink.App" ]; then
    SOURCE_BIN="$SCRIPT_DIR/VidShrink.App"
elif [ -f "$SCRIPT_DIR/../../publish/linux-x64/VidShrink.App" ]; then
    SOURCE_BIN="$(cd "$SCRIPT_DIR/../../publish/linux-x64" && pwd)/VidShrink.App"
elif [ -f "$SCRIPT_DIR/../publish/linux-x64/VidShrink.App" ]; then
    SOURCE_BIN="$(cd "$SCRIPT_DIR/../publish/linux-x64" && pwd)/VidShrink.App"
fi

if [ -z "$SOURCE_BIN" ] || [ ! -f "$SOURCE_BIN" ]; then
    echo "Ошибка: исполняемый файл VidShrink.App не найден."
    echo "Сначала выполните сборку проекта:"
    echo "  dotnet publish VidShrink.App -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o publish/linux-x64"
    exit 1
fi

SOURCE_DIR="$(dirname "$SOURCE_BIN")"

INSTALL_MODE="user"
if [ "$1" == "--system" ] || [ "$1" == "--opt" ]; then
    INSTALL_MODE="system"
fi

ICON_SRC="$SCRIPT_DIR/icon.png"
if [ ! -f "$ICON_SRC" ] && [ -f "$SCRIPT_DIR/../../VidShrink.App/Assets/icon.png" ]; then
    ICON_SRC="$(cd "$SCRIPT_DIR/../../VidShrink.App/Assets" && pwd)/icon.png"
fi

DESKTOP_SRC="$SCRIPT_DIR/vidshrink.desktop"

if [ "$INSTALL_MODE" == "system" ]; then
    if [ "$EUID" -ne 0 ]; then
        echo "Системная установка требует прав root. Запустите: sudo ./install.sh --system"
        exit 1
    fi

    TARGET_DIR="/opt/vidshrink"
    BIN_LINK="/usr/local/bin/vidshrink"
    APPS_DIR="/usr/share/applications"
    ICONS_DIR="/usr/share/icons/hicolor/512x512/apps"

    echo "Установка VidShrink для всех пользователей в $TARGET_DIR..."
    mkdir -p "$TARGET_DIR" "$APPS_DIR" "$ICONS_DIR"

    cp "$SOURCE_BIN" "$TARGET_DIR/vidshrink"
    chmod +x "$TARGET_DIR/vidshrink"
    cp "$SOURCE_DIR"/*.so "$TARGET_DIR/" 2>/dev/null || true
    ln -sf "$TARGET_DIR/vidshrink" "$BIN_LINK"

    if [ -f "$ICON_SRC" ]; then
        cp "$ICON_SRC" "$ICONS_DIR/vidshrink.png"
        chmod 644 "$ICONS_DIR/vidshrink.png"
    fi

    sed -e "s|^Exec=.*|Exec=$BIN_LINK %F|" -e "s|^Icon=.*|Icon=vidshrink|" "$DESKTOP_SRC" > "$APPS_DIR/vidshrink.desktop"
    chmod 644 "$APPS_DIR/vidshrink.desktop"

    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database "$APPS_DIR" 2>/dev/null || true
    fi

    echo "Установка завершена успешно!"
    echo "Запуск: vidshrink или через меню приложений."
else
    TARGET_APP_DIR="$HOME/.local/share/vidshrink"
    TARGET_BIN_DIR="$HOME/.local/bin"
    APPS_DIR="$HOME/.local/share/applications"
    ICONS_DIR="$HOME/.local/share/icons/hicolor/512x512/apps"

    echo "Установка VidShrink для текущего пользователя в $TARGET_APP_DIR..."
    mkdir -p "$TARGET_APP_DIR" "$TARGET_BIN_DIR" "$APPS_DIR" "$ICONS_DIR"

    TARGET_BIN="$TARGET_APP_DIR/vidshrink"
    cp "$SOURCE_BIN" "$TARGET_BIN"
    chmod +x "$TARGET_BIN"
    cp "$SOURCE_DIR"/*.so "$TARGET_APP_DIR/" 2>/dev/null || true
    ln -sf "$TARGET_BIN" "$TARGET_BIN_DIR/vidshrink"

    if [ -f "$ICON_SRC" ]; then
        cp "$ICON_SRC" "$ICONS_DIR/vidshrink.png"
        chmod 644 "$ICONS_DIR/vidshrink.png"
    fi

    sed -e "s|^Exec=.*|Exec=$TARGET_BIN %F|" -e "s|^Icon=.*|Icon=vidshrink|" "$DESKTOP_SRC" > "$APPS_DIR/vidshrink.desktop"
    chmod 644 "$APPS_DIR/vidshrink.desktop"

    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database "$APPS_DIR" 2>/dev/null || true
    fi

    echo "Установка завершена успешно!"
    echo "Убедитесь, что $TARGET_BIN_DIR добавлен в PATH (обычно по умолчанию в ~/.profile или ~/.bashrc)."
    echo "Запуск: vidshrink или через меню приложений."
fi
