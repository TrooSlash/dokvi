# Разработка Dokvi

[README](../README.md)

## Сборка

Запускайте команды из корня репозитория, рядом с `package.json`.
Нужны Windows x64, Node.js, npm и .NET Framework 4.8. Используемые версии
инструментов: Node.js 26.13.0 и npm 11.20.0. Для запуска приложения и оконных
тестов нужен системный WebView2 Evergreen Runtime.

```powershell
npm ci
npm run build
```

`build.mjs` собирает интерфейс в `ui`. `host/build.ps1` загружает WebView2 SDK с NuGet, если его нет в `tools`, затем компилирует оболочку системным C#-компилятором .NET Framework. Visual Studio для этого пути сборки не требуется. Результат: `dist/Dokvi-3.0.0.exe`.

Для пересборки только оболочки при готовой папке `ui`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File host/build.ps1
```

## Устройство проекта

| Путь | Назначение |
| --- | --- |
| `host/` | Windows-окно, файловые диалоги, сессии, интеграция с Проводником, тестовые точки входа |
| `src/` | Редактор, обработка Markdown, меню, стили и переводы |
| `scripts/` | Генерация меню, иконок и уведомлений о лицензиях |
| `ui/` | Собранный интерфейс, который встраивается в EXE |
| `test/` | Модульные, интеграционные, оконные тесты и измерения |
| `docs/` | Документация и снимки приложения |
| `site/` | Тексты и стили сайта, сведения об опубликованном выпуске |
| `.github/workflows/pages.yml` | Сборка и публикация сайта на GitHub Pages |

Пустой экран нативный. Один WebView2 обслуживает все открытые документы и освобождается после закрытия последней вкладки. Значки, кнопки и меню используют общие модули `ChromeIcons`, `ChromeControls` и шаблон меню.

Версия интерфейса берётся из `package.json` во время сборки. Версия Windows-оболочки указана в `host/Program.cs` и `host/app.manifest`. При выпуске обновляйте их вместе с двумя полями корневой версии в `package-lock.json`.

## Проверки

```powershell
npm run test:unit
npm test
```

`npm test` собирает оболочку с флагом `TESTING` и запускает основные сценарии
приложения в отдельных профилях внутри `dist/test-data`. Тестовые точки входа
исключены из релизного EXE.

Дополнительные наборы запускайте последовательно: у них общий
`dist/MD-Reader-Test.exe`. Нужен разблокированный рабочий стол; сценарии ввода
направляют нажатия и клики в тестовое окно.

| Команда | Проверяет |
| --- | --- |
| `npm run test:session` | Восстановление вкладок и черновиков после перезапуска |
| `npm run test:storage` | Запись, резервные копии, конфликты и отказы доступа; без окна |
| `npm run test:transition` | Переходы между пустым окном и документом |
| `npm run test:window` | Состояния окна |
| `npm run test:click` | Оконные кнопки реальными кликами |
| `npm run test:resize` | Перетаскивание восьми границ окна |
| `npm run test:shortcuts` | Горячие клавиши и файловые диалоги |
| `npm run test:input-focus` | Ввод после оконных кнопок и сохранения |
| `npm run test:focus-routing` | Маршрутизация Enter |
| `npm run test:taskbar-focus` | Возврат через панель задач; другие экземпляры Dokvi должны быть закрыты |
| `npm run test:frame` | Геометрия рамки |
| `npm run test:language` | RU/EN и сохранение выбора языка |
| `npm run test:quality` | Цвета, узкий редактор, загрузка снимков и отображение README |
| `npm run test:native-menu` | Меню и согласованность пустого окна с документом |
| `npm run test:memory` | Запуск, ввод и ресурсы дерева процессов на пяти документах |

Проверки Tab, Shift+Tab, Enter и Escape в обеих темах:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File test/native-menu.ps1 -KeyboardNavigation
powershell -NoProfile -ExecutionPolicy Bypass -File test/native-menu.ps1 -SkipBuild -DialogKeyboard
```

Таблицы и PDF:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File test/table-layout.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File test/pdf-tables.ps1 -SkipBuild
powershell -NoProfile -ExecutionPolicy Bypass -File test/pdf-content.ps1 -SkipBuild
```

PDF-скрипты выводят папки результатов. Передайте их скриптам `test/verify-pdf-tables.py` и `test/verify-pdf-content.py` с параметром `--render`. Для анализа нужны Python, `pdfplumber`, `Pillow` и Poppler (`pdftoppm` в PATH). Эти зависимости нужны только для проверки PDF.

Основные сценарии и измерение производительности с одной тестовой сборкой
и контролем её SHA-256:

```powershell
npm run build
npm run test:unit
powershell -NoProfile -ExecutionPolicy Bypass -File test/release.ps1 -RunDirectory quality/logs/local-release
powershell -NoProfile -ExecutionPolicy Bypass -File test/release-performance.ps1 -RunDirectory quality/logs/local-release
```

PDF и диалоги проверяются отдельными командами выше. Перед измерением CPU
завершите сеанс автоматизации интерфейса: внешний клиент может создавать
дополнительную нагрузку через UI Automation. `test/measurement-context.ps1`
останавливает замер при подключённом клиенте. Экранный диктор и доступность
приложения должны оставаться включёнными при проверке работы с диктором.

### Границы проверки

Тестовая конфигурация: Windows 10 Pro 19045, 96 DPI. Масштабирование Windows
при 125%, 150% и 200% ещё не проверено на другом ПК. Эмуляция плотности
пикселей WebView2 не заменяет проверку системного масштаба.

Для 3.0.0 не выполнялся повторный полный приёмочный прогон всех функций.

При измерении памяти учитывайте всё дерево процессов WebView2 и указывайте
метрику: private bytes или working set. Размер EXE не показывает расход памяти
с открытым документом.

## Упаковка

После сборки:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File host/package.ps1 -PublicSource -OutputDirectory .\dist\packages
```

В `dist/packages` появятся EXE, исходники, руководство, MIT, контрольная сумма и иконка Markdown-файлов. Архив исходников содержит README, его изображения и необходимые для сборки файлы. Локальные тестовые профили, журналы, SDK, `node_modules` и сборочные папки в него не входят.

Упаковщик создаёт только публичный архив. Параметр `-PublicSource` принимается
для совместимости и не меняет состав архива.

`test/installer.ps1` не входит в основные тесты и требует исходников установщика,
которых нет в репозитории. Путь к проекту с этими исходниками передаётся через
`-ProjectDirectory`.
