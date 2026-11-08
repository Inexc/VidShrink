# VidShrink — Быстрое кроссплатформенное сжатие видео

<p align="center">
  <img src="design/branding/lockup/vidshrink-horizontal-dark.png" alt="VidShrink" width="460" />
</p>

<p align="center">
  <a href="https://dotnet.microsoft.com/download/dotnet/8.0"><img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 8" /></a>
  <a href="https://avaloniaui.net/"><img src="https://img.shields.io/badge/Avalonia%20UI-11.0-purple?logo=avalonia" alt="Avalonia UI 11" /></a>
  <a href="https://ffmpeg.org/"><img src="https://img.shields.io/badge/FFmpeg-Powered-007808?logo=ffmpeg&logoColor=white" alt="FFmpeg" /></a>
  <img src="https://img.shields.io/badge/Platform-Windows%20%7C%20Linux-blue" alt="Platform: Windows | Linux" />
  <img src="https://img.shields.io/badge/License-MIT-green" alt="License: MIT" />
</p>

**VidShrink** — быстрое, простое и легковесное десктопное приложение для точного сжатия видео на **Windows** и **Linux**.

Позволяет в пару кликов подогнать размер видео под лимиты Discord, Telegram, WhatsApp, Email, или сжать файл с сохранением максимального визуального качества через кодеки H.264, HEVC (H.265) и AV1 с поддержкой аппаратного ускорения (NVIDIA NVENC, Intel QSV, AMD AMF, Linux VAAPI).

---

## 📥 Скачать приложение (Releases)

Вы можете загрузить последнюю версию во вкладке **[GitHub Releases](../../releases/latest)**:

| Платформа | Формат | Описание | Инструкция |
| :--- | :--- | :--- | :--- |
| **Windows 10 / 11** | **`VidShrinkSetup.exe`** | Полноценный установщик | Запустите файл, он установит программу в `Program Files` и создаст ярлыки в меню «Пуск» и на рабочем столе. |
| **Windows 10 / 11** | **`VidShrink-win-x64-portable.zip`** | Портативная версия | Распакуйте в любую удобную папку и запустите `VidShrink.App.exe` (без прав администратора). |
| **Linux (Ubuntu, Debian, Fedora, Arch)** | **`VidShrink-linux-x64.tar.gz`** | Пакет с авто-установщиком | Распакуйте архив и выполните: `chmod +x install.sh && ./install.sh`. Добавит ярлык в системное меню. |

> [!NOTE]
> Приложение собрано в режиме **Self-Contained Single-File** — вам **не нужно** отдельно устанавливать .NET Runtime.

---

## ⚙️ Требование: FFmpeg

Для работы приложения требуются системные утилиты `ffmpeg` и `ffprobe`. Они не включены в архив из лицензионных соображений и ставятся одной командой:

### Windows
Установите через терминал:
```powershell
winget install Gyan.FFmpeg
# или: scoop install ffmpeg
```
*Либо:* положите `ffmpeg.exe` и `ffprobe.exe` в подпапку `tools/ffmpeg/` рядом с программой или укажите путь вручную в настройках.

### Linux
```bash
# Ubuntu / Debian / Linux Mint:
sudo apt update && sudo apt install -y ffmpeg

# Arch Linux / Manjaro:
sudo pacman -S ffmpeg

# Fedora:
sudo dnf install ffmpeg
```

---

## 🚀 Основные возможности

- **Сжатие в точный целевой размер (Target Size)**:
  - Автоматический расчет оптимального видеобитрейта с точностью до 1 кбит/с и запасом 3% на контейнер MP4/MKV.
  - Двухпроходное кодирование (2-Pass) для `libx264` и `libx265`.
  - Автоматический повтор с пониженным битрейтом ($\times 0.92$), если размер готового файла вышел за лимит.
- **Сжатие по визуальному качеству (CRF)**:
  - Постоянное качество без ограничения размера (CRF 18 — макс. качество, CRF 23 — баланс).
- **Поддержка современных кодеков**:
  - H.264 (`libx264`) — максимальная совместимость.
  - H.265 / HEVC (`libx265`) с тегом `-tag:v hvc1` для мгновенного воспроизведения на устройствах Apple.
  - AV1 (`libsvtav1`) — эффективное сжатие нового поколения.
  - MP4 с флагом `-movflags +faststart` для потокового онлайн-воспроизведения без ожидания полной загрузки.
- **Аппаратное ускорение (GPU)**:
  - **Windows**: NVIDIA NVENC (`h264_nvenc`, `hevc_nvenc`, `av1_nvenc`), Intel QuickSync (`qsv`), AMD AMF (`amf`).
  - **Linux**: NVIDIA NVENC и Intel/AMD VAAPI (`h264_vaapi`, `hevc_vaapi`).
  - Фоновое тестирование доступности GPU: нерабочие кодировщики в интерфейсе скрываются.
- **Удобная очередь и пакетная обработка**:
  - Drag-and-Drop нескольких файлов прямо в окно программы.
  - Прогресс-бар, скорость кодирования (например, `3.2x`), оставшееся время (ETA) и процент экономии места.
  - Быстрая кнопка перехода в папку с готовым файлом (`explorer` на Windows, `xdg-open` на Linux).
  - Безопасность: оригинальные файлы никогда не перезаписываются.
- **Масштабирование и обрезка**:
  - Разрешения: Оригинал, 1080p, 720p, 480p (только уменьшение, без апскейла).
  - Быстрая обрезка начала и конца видео без потери качества до кодирования (`-ss` и `-to`).

---

## 🎛 Пресеты сжатия (`presets.json`)

В приложение встроены готовые профили:
1. **Для мессенджера**: 16 МБ, H.264, AAC 128 kbps.
2. **Discord**: 8 МБ (под лимит бесплатных вложений), H.264, AAC 96 kbps.
3. **Почта**: 20 МБ, 720p, H.264, AAC 96 kbps.
4. **Максимальное качество**: режим CRF 18, H.264, AAC 192 kbps.
5. **Баланс**: режим CRF 23, H.264, AAC 128 kbps.
6. **Минимальный размер**: 5 МБ, 720p, H.264, AAC 96 kbps.
7. **Своё**: гибкая ручная настройка любых параметров.

---

## 💻 Сборка и публикация из исходного кода

### Требования к среде разработки
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Inno Setup 6 (для сборки Windows-установщика)

### Сборка и запуск тестов
```bash
# Сборка решения
dotnet build VidShrink.sln

# Запуск тестов (125 тестов xUnit)
dotnet test

# Запуск приложения в режиме разработки
dotnet run --project VidShrink.App
```

### Публикация автономных бинарников
```powershell
# Windows x64:
dotnet publish VidShrink.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/win-x64

# Linux x64:
dotnet publish VidShrink.App -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o publish/linux-x64
```

### Сборка установщика Windows:
```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\windows\setup.iss
```
Готовый установщик появится в `installer/VidShrinkSetup.exe`.

---

## 📁 Структура проекта

```
VidShrink/
├── VidShrink.Core/         # Чистая кроссплатформенная бизнес-логика и службы FFmpeg
├── VidShrink.App/          # UI-клиент на Avalonia 11 (MVVM, XAML, темы и стили)
├── VidShrink.Tests/        # Набор тестов (xUnit)
├── installer/
│   ├── windows/setup.iss   # Скрипт Inno Setup 6 для создания .exe установщика
│   └── linux/              # install.sh, vidshrink.desktop, icon.png
├── design/                 # Векторные и растровые логотипы, иконки, спецификации
└── .github/workflows/      # Автоматическая сборка и релизы на GitHub Actions
```

---

## 📄 Лицензия

Проект распространяется под свободной лицензией **MIT**. Подробнее см. [LICENSE](LICENSE).
