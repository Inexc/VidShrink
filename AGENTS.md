# AGENTS.md — Правила и архитектурные договорённости проекта VidShrink

## 1. О проекте
**VidShrink** — быстрое кроссплатформенное десктопное приложение для сжатия видео (Windows и Linux) на **C# / .NET 8 / Avalonia UI 11 + FFmpeg**.

### Название проекта
- Solution: `VidShrink.sln`
- Проекты: `VidShrink.Core` (net8.0), `VidShrink.App` (net8.0), `VidShrink.Tests` (net8.0)
- Папка настроек: `%AppData%/VidShrink` на Windows (`~/.config/VidShrink` на Linux через `Environment.SpecialFolder.ApplicationData`)
- Заголовок окна: «VidShrink»

---

## 2. Базовые ограничения и правила
1. **Ничего лишнего в репозитории**:
   - Ничего не удалять без необходимости.
   - Никаких git push.
   - Бинарники ffmpeg/ffprobe в репозиторий **не класть**.
   - Скачивать только NuGet-пакеты.
   - Секретов и телеметрии нет.
2. **Версии .NET и SDK**:
   - В корне `global.json` с фиксацией SDK 8.0.x (`rollForward: latestFeature`).
   - Все проекты таргетят `net8.0` (включая `VidShrink.App` — кроссплатформенная Avalonia 11, не `net8.0-windows`).
   - Не использовать возможности .NET 9+ и RC-пакеты.
3. **Языки**:
   - Идентификаторы, имена методов, классов, коммитов и комментарии в коде — **на английском**.
   - Тексты пользовательского интерфейса, сообщения об ошибках и `README.md` — **на русском**.
4. **Поэтапная работа**:
   - Работать строго по этапам.
   - После каждого этапа запускать `dotnet build` и `dotnet test`.
   - Коротко отчитываться и **ОСТАНАВЛИВАТЬСЯ**, ожидая команды пользователя для перехода к следующему этапу.

---

## 3. Кроссплатформенность (Windows и Linux)
1. **Core (VidShrink.Core)**:
   - Полностью платформо-независимый (никаких обращений к Windows Registry, Win32 API или жестко зашитых путей).
   - Каталог настроек: `Environment.SpecialFolder.ApplicationData` + `Path.Combine`.
   - Пустое устройство (null device для 1-го прохода): `NUL` на Windows, `/dev/null` на Linux (`IPlatformService.NullDevice`).
2. **Исполняемые файлы FFmpeg**:
   - Windows: `ffmpeg.exe` и `ffprobe.exe`.
   - Linux: `ffmpeg` и `ffprobe` (без расширения).
   - Порядок поиска:
     1. Путь из настроек пользователя (`settings.json`).
     2. Папка рядом с приложением: `tools/ffmpeg`, `tools`, `ffmpeg` или корень приложения.
     3. Системный `PATH`.
     4. Если не найден — понятная ошибка в UI и кнопка «Указать путь».
   - Интеграционные тесты пропускаются (`Skip`), если ffmpeg/ffprobe не установлены в системе.
3. **Запуск процессов**:
   - Только через `ProcessStartInfo.ArgumentList` (никакой склейки аргументов в строку).
   - Асинхронное чтение stdout и stderr, чтобы избежать блокировки буферов.
   - Корректная работа с пробелами и кириллицей в путях.
4. **Отмена кодирования**:
   - `process.Kill(entireProcessTree: true)` (работает на Windows и Linux).
   - Удаление недописанного выходного файла и временных логов двухпроходного кодирования.
5. **Открытие папки с результатом**:
   - Windows: `explorer.exe` с аргументом папки через `ArgumentList`.
   - Linux: `xdg-open` через `ArgumentList`.

---

## 4. Визуальная часть (Avalonia UI 11 + MVVM)
1. **Чистый MVVM**:
   - Фреймворк: `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`).
   - Code-behind (`*.axaml.cs`) **пустой** (только `InitializeComponent()`).
   - Все интерактивные элементы имеют `Name` или русскоязычный `AutomationProperties.Name`.
   - Перетаскивание файлов через `DragDrop` Avalonia, диалоги выбора файлов — через `StorageProvider`.
2. **Дизайн-токены и изоляция оформления**:
   - Во `Views` **НЕТ** захардкоженных цветов, кистей, шрифтов и размеров. Только `DynamicResource` / `StaticResource`.
   - `Resources/Theme.axaml`: цвета и кисти как токены (`SurfaceBrush`, `AccentBrush`, `BorderBrush`, и т.д.).
   - `Resources/Typography.axaml`: семейства шрифтов, размеры и начертания (`FontFamilyBody`, `FontSizeTitle`, и т.д.).
   - `Styles/Styles.axaml`: стили базовых контролов (`Button`, `TextBox`, `ComboBox`, `ProgressBar`, `ListBox`).
   - Базовая тема: встроенная `FluentTheme`.
   - Разметка нейтральная: `Grid`, `StackPanel`, аккуратные отступы, без тяжелых картинок и анимаций до этапа полировки дизайна.

---

## 5. Алгоритмы и логика кодирования
1. **BitrateCalculator**:
   - Формула: `targetBytes * 8 * 0.97 / duration - audioBitrate` (запас 3% на контейнер MP4/MKV).
   - Если результат ниже разумного порога (или аудио вытесняет видео), возвращать предупреждение: «слишком маленький лимит для этого видео, уменьши разрешение».
   - **Никаких отрицательных или нулевых значений** битрейта (гарантированный clamp на безопасный минимум).
2. **Режимы двухпроходного кодирования**:
   - `libx264`: двухпроходный режим (`-pass 1 -passlogfile <log>` и `-pass 2 -passlogfile <log>`). Первый проход пишет в `-f null NUL` (Windows) или `/dev/null` (Linux) с `-an`.
   - `libx265`: двухпроходный режим через `-x265-params pass=1:stats=<log>` и `pass=2:stats=<log>`.
   - `libsvtav1` и все аппаратные кодировщики (`nvenc`, `qsv`, `amf`, `vaapi`): **только один проход** с `-b:v`, `-maxrate`, `-bufsize` (в UI помечать результат как «приблизительно»).
3. **Общие параметры FFmpeg**:
   - Пиксельный формат: `-pix_fmt yuv420p` (для 8-бит видео).
   - Стриминг/веб: `-movflags +faststart` для mp4/mov.
   - HEVC/H.265 в MP4: обязательно тег `-tag:v hvc1`.
   - Масштабирование: оригинал, 1080p, 720p, 480p через фильтр `scale=-2:H` (только уменьшение, никогда не увеличивать: `scale=-2:'min(H,ih)'`).
4. **Аппаратное кодирование**:
   - Windows: `h264_nvenc`/`hevc_nvenc`/`av1_nvenc`, `h264_qsv`/`hevc_qsv`/`av1_qsv`, `h264_amf`/`hevc_amf`/`av1_amf`.
   - Linux: `h264_nvenc`/`hevc_nvenc`, `h264_vaapi`/`hevc_vaapi` (с аргументами `-vaapi_device /dev/dri/renderD128` и фильтром `format=nv12,hwupload`).
   - Доступность: опрос `ffmpeg -encoders`, а затем реальная 1-секундная проверка через `lavfi` (`color=c=black:s=256x256:d=1`). Недоступные адаптеры в UI не показываются.
5. **Повтор при превышении размера**:
   - Если итоговый размер после кодирования превысил лимит, один раз автоматически повторить со сниженным битрейтом (`x 0.92`) и уведомить пользователя.
6. **Выходной файл**:
   - Имя вида `имя_compressed.mp4` рядом с исходником или в папке из настроек.
   - Никогда не перезаписывать оригинал. При совпадении имен добавлять суффикс `_1`, `_2` и т.д.
7. **Парсинг прогресса**:
   - FFmpeg запускается с `-progress pipe:1 -nostats`.
   - Построчный парсинг пар `key=value`: `out_time_us`, `speed`, `total_size`, `progress=continue|end`.
   - Расчет процента выполнения, текущей скорости и ETA.

---

## 6. Пресеты (`presets.json`)
Файл `presets.json` лежит в Core и копируется в рабочую папку пользователя при отсутствии. Пресеты редактируемые:
1. «Для мессенджера» (TargetSize, 16 МБ, H.264, AAC 128 kbps).
2. «Discord» (TargetSize, 8 МБ, H.264, AAC 96 kbps).
3. «Почта» (TargetSize, 20 МБ, H.264, 720p, AAC 96 kbps).
4. «Максимальное качество» (Quality CRF 18, H.264).
5. «Баланс» (Quality CRF 23, H.264).
6. «Минимальный размер» (TargetSize, 5 МБ, 720p, H.264).
7. «Своё» (Кастомные настройки пользователя).

---

## 7. Этапы разработки
- [x] **Этап 1**: Solution и проекты (`net8.0`, Avalonia 11, xUnit), `global.json`, Core: модели, `FfmpegLocator`, `ProbeService` (ffprobe JSON -> `MediaInfo`), `BitrateCalculator`, `PresetCatalog` + `presets.json`, `CommandBuilder` (список строк `ArgumentList`, `NUL`/`/dev/null`, VAAPI, x265-params, обрезка `-ss`/`-to`). Тесты Core (59 тестов пройдены).
- [x] **Этап 2**: `EncoderService` (запуск процесса, `ProgressParser`, двухпроходный цикл, повтор x0.92 при превышении, отмена Process.Kill, очистка временных файлов), `HardwareDetector` (lavfi testsrc), `JobQueue`. Тесты на парсер прогресса и очередь; интеграционные тесты с пропуском без ffmpeg (107 тестов пройдены).
- [x] **Этап 3**: UI на Avalonia 11: `MainWindow` и `SettingsWindow`, MVVM, Drag-and-Drop файлов, очередь, оценка размера и битрейта, настройки `%AppData%/VidShrink/settings.json`, логи `%AppData%/VidShrink/logs` (125 тестов пройдены).
- [x] **Этап 4**: `README.md` (установка ffmpeg, Linux `sudo apt install ffmpeg`, запуск `chmod +x`), `docs/UI_STRUCTURE.md`, профили публикации `win-x64` и `linux-x64` (self-contained, single-file), финальная верификация (125 тестов пройдены, публикация win-x64 и linux-x64 протестирована).
