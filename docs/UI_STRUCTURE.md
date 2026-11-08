# UI Structure & Design Token Guide

Данный документ описывает структуру пользовательского интерфейса приложения **VidShrink**, иерархию представлений (Views), имена элементов, токены оформления и правила модификации внешнего вида без нарушения бизнес-логики.

---

## 1. Архитектура изоляции визуального слоя

1. **Строгое разделение логики и оформления**:
   - Во всех `.axaml` файлах представлений (**Views**) **запрещено** прописывать жёсткие HEX-цвета (напр. `#FFFFFF`), абсолютные размеры шрифтов и несистемные отступы.
   - Все визуальные параметры подключаются исключительно через `{DynamicResource TokenName}` или `{StaticResource TokenName}`.
   - Code-behind (`*.axaml.cs`) остаётся **пустым** (содержит только вызов `InitializeComponent()`). Вся логика реализована в ViewModels (`CommunityToolkit.Mvvm`).
2. **Локализация и доступность**:
   - Каждый интерактивный контрол содержит идентификатор `Name` и русскоязычное описание `AutomationProperties.Name`.

---

## 2. Дерево экранов и элементы интерфейса

### 2.1. Главное окно (`MainWindow.axaml`)
- **Заголовок**: «VidShrink»
- **Разметка**: Корневой `Grid` с секциями:
  1. **Шапка (Header)**:
     - `TxtTitle`: Заголовок приложения «VidShrink» (`AutomationProperties.Name="Заголовок VidShrink"`).
     - `TxtSubtitle`: Краткое описание статуса/готовности (`AutomationProperties.Name="Статус системы"`).
     - `BtnSettings`: Кнопка открытия окна настроек (`AutomationProperties.Name="Настройки"`).
  2. **Зона добавления файлов (DropZone / FileSelector)**:
     - `BorderDropZone`: Интерактивная зона с поддержкой Avalonia `DragDrop`.
     - `BtnBrowseFiles`: Кнопка «Выбрать файлы» (`AutomationProperties.Name="Выбрать файлы для сжатия"`).
     - `TxtDropPrompt`: Подсказка «Перетащите видео сюда или нажмите для выбора».
  3. **Список очереди (JobQueue / FilesList)**:
     - `ListQueue`: Список добавленных видеофайлов (`AutomationProperties.Name="Очередь видеофайлов"`).
     - Карточка элемента очереди:
       - Имя файла, разрешение, длительность, исходный размер.
       - Статус (`В очереди`, `Анализ (проход 1)`, `Сжатие (проход 2)`, `Готово`, `Ошибка`, `Отменено`).
       - Прогресс-бар `PbJobProgress` (`AutomationProperties.Name="Прогресс задачи"`).
       - Итог сжатия: «было → стало (экономия X%)».
       - Кнопки действий над элементом: «Удалить из очереди», «Открыть папку с результатом».
  4. **Панель параметров сжатия (SettingsPanel)**:
     - `CmbPreset`: Выбор пресета (`AutomationProperties.Name="Выбор пресета сжатия"`). Пресеты: *Для мессенджера*, *Discord*, *Почта*, *Максимальное качество*, *Баланс*, *Минимальный размер*, *Своё*.
     - `TbTargetSize`: Поле ввода целевого размера в МБ (`AutomationProperties.Name="Целевой размер в мегабайтах"`).
     - `CmbCodec`: Выбор кодека (`H.264`, `H.265 / HEVC`, `AV1`).
     - `CmbHardware`: Выбор аппаратного ускорения (`NVENC`, `QSV`, `AMF`, `VAAPI`, `Только процессор`). Недоступные адаптеры скрываются.
     - `CmbResolution`: Разрешение (`Оригинал`, `1080p`, `720p`, `480p`).
     - `CmbAudio`: Звуковая дорожка (`AAC 96k`, `AAC 128k`, `AAC 192k`, `Копировать`, `Без звука`).
     - Секция обрезки: `TbTimeStart` («Начало»), `TbTimeEnd` («Конец»).
  5. **Панель оценки и действий (Footer / ActionPanel)**:
     - `TxtBitrateEstimate`: Расчётный битрейт видео и предупреждения калькулятора («слишком маленький лимит для этого видео, уменьши разрешение»).
     - `TxtSizeEstimate`: Оценка итогового размера.
     - `BtnStart`: Кнопка «Начать сжатие» (`AutomationProperties.Name="Начать сжатие"`).
     - `BtnCancel`: Кнопка «Отмена» (`AutomationProperties.Name="Отменить сжатие"`).

### 2.2. Окно настроек (`SettingsWindow.axaml`)
- **Заголовок**: «Настройки — VidShrink»
- **Элементы**:
  - `TbFfmpegPath`: Путь к папке или исполняемому файлу FFmpeg (`AutomationProperties.Name="Путь к FFmpeg"`).
  - `BtnBrowseFfmpeg`: Кнопка обзора «Указать путь» (`AutomationProperties.Name="Обзор пути FFmpeg"`).
  - `TbOutputDir`: Папка для сохранения результатов (`AutomationProperties.Name="Папка сохранения"`).
  - `BtnBrowseOutputDir`: Кнопка обзора папки сохранения.
  - `CbPreferHardware`: Чекбокс «Использовать аппаратное кодирование по умолчанию» (`AutomationProperties.Name="Предпочитать аппаратное кодирование"`).
  - `BtnSaveSettings`: Кнопка «Сохранить настройки» (`AutomationProperties.Name="Сохранить настройки"`).

---

## 3. Реестр токенов оформления

### 3.1. Цветовые токены (`Resources/Theme.axaml`)
| Имя токена (Brush) | Исходный цвет | Назначение |
|---|---|---|
| `BrushBackground` | `#0F1117` | Основной фон окон приложения |
| `BrushSurface` | `#1A1D26` | Фон панелей, боковых секций и заголовков |
| `BrushCard` | `#222633` | Фон карточек очереди и выпадающих списков |
| `BrushCardHover` | `#2A2F40` | Фон элементов при наведении курсора |
| `BrushCardBorder` | `#343A4E` | Цвет границ карточек и разделителей |
| `BrushAccent` | `#5865F2` | Основной акцентный цвет (кнопки действий, прогресс) |
| `BrushAccentHover` | `#6975F5` | Акцент при наведении |
| `BrushAccentPressed`| `#4752C4` | Акцент при нажатии |
| `BrushTextPrimary` | `#F2F3F5` | Основной текст (высокая контрастность) |
| `BrushTextSecondary`| `#B5BAC1` | Вторичный текст, подписи параметров |
| `BrushTextMuted` | `#7B828D` | Неактивный текст, плейсхолдеры |
| `BrushSuccess` | `#57F287` | Статус «Готово», положительная экономия |
| `BrushWarning` | `#FEE75C` | Предупреждения калькулятора битрейта |
| `BrushError` | `#ED4245` | Ошибки кодирования и валидации |
| `BrushInfo` | `#5865F2` | Информационные сообщения |

### 3.2. Токены отступов и радиусов скруглений (`Resources/Theme.axaml`)
- Отступы: `MarginNone`, `MarginExtraSmall` (4), `MarginSmall` (8), `MarginMedium` (16), `MarginLarge` (24).
- Внутренние отступы: `PaddingButton` (16,10), `PaddingBadge` (8,4), `PaddingMedium` (16).
- Скругления: `CornerRadiusSmall` (4), `CornerRadiusMedium` (8), `CornerRadiusLarge` (12), `CornerRadiusRound` (999).

### 3.3. Типографика (`Resources/Typography.axaml`)
- Семейства шрифтов:
  - `FontFamilyDefault`: Inter / системный стек UI без засечек.
  - `FontFamilyMonospace`: Consolas / Cascadia Code / Monospace стек для таймкодов и параметров.
- Размеры шрифтов:
  - `FontSizeTitle`: 22 pt (заголовки окон).
  - `FontSizeSubtitle`: 16 pt (подзаголовки блоков).
  - `FontSizeBodyLarge`: 14 pt (основные акцентные надписи).
  - `FontSizeBody`: 13 pt (стандартный текст интерфейса).
  - `FontSizeSmall`: 11 pt (метки, подсказки, метаданные видео).
  - `FontSizeCaption`: 10 pt (бейдж статуса, проценты).

---

## 4. Что МОЖНО и что НЕЛЬЗЯ менять дизайнеру

### ЧТО МОЖНО МЕНЯТЬ:
1. Значения HEX-цветов в `Color*` и `Brush*` в `Resources/Theme.axaml` (например, смена темной темы на светлую или пастельную).
2. Размеры шрифтов, начертания и семейство шрифтов в `Resources/Typography.axaml`.
3. Отступы (`Margin*`, `Padding*`) и радиусы скругления (`CornerRadius*`).
4. Шаблоны элементов управления (`ControlTheme` / `Style`) в `Styles/Styles.axaml` (анимации наведения, визуальный вид `ProgressBar`, тени, стили `Button` и `TextBox`).

### ЧТО НЕЛЬЗЯ МЕНЯТЬ:
1. `x:Name` и имена элементов управления, к которым обращаются тесты или ViewModels.
2. Привязки `{Binding ...}` к свойствам и командам ViewModel.
3. Добавлять логику в code-behind (`*.axaml.cs`).
4. Заменять токены на захардкоженные значения цветов/шрифтов.
