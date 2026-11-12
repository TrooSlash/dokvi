# Export Markdown to PDF on Windows with Dokvi

[Download and requirements](../README.md#open-markdown-files-on-windows) · [По-русски](#сохранить-markdown-в-pdf-на-windows)

Dokvi exports the current Markdown document, including unsaved edits, to PDF. Tables, syntax-highlighted code, LaTeX math, Mermaid diagrams, and images are included.

1. Open your `.md` file in Dokvi. Use **Ctrl+O** or drag the file into the window.
2. Check the reading view or live preview. Give images and diagrams time to appear before exporting.
3. Press **Ctrl+P**, or choose the PDF command in **⋯**.
4. Choose a name and folder for the PDF, then open it to check page breaks and small details.

Wide tables use landscape pages and keep the column alignment from Markdown. Long code lines wrap for printing. Images, diagrams, and large formulas scale to fit the printable area. A large diagram can therefore need zooming to read comfortably; consider splitting it into smaller diagrams in the source.

Math and Mermaid rendering work offline. Remote images need an internet connection. Local images must be in the document's folder or its subfolders; save a new document there before using relative image paths.

Exporting a PDF does not save the Markdown source. Use **Ctrl+S** to save the `.md` file separately. If something is missing in the PDF, [report it](https://github.com/TrooSlash/Dokvi/issues) with a small Markdown example and the Dokvi version.

## Сохранить Markdown в PDF на Windows

[Скачать Dokvi и проверить требования](README.ru.md#как-открыть-markdown-файл-в-windows).

Dokvi сохраняет в PDF текущий текст, включая несохранённые изменения. В экспорт входят таблицы, код с подсветкой, формулы LaTeX, диаграммы Mermaid и изображения.

1. Откройте `.md` через **Ctrl+O** или перетащите файл в окно.
2. Проверьте документ в чтении или предпросмотре. Дождитесь появления картинок и диаграмм.
3. Нажмите **Ctrl+P** или выберите **«Сохранить PDF…»** в меню **«⋯»**.
4. Укажите имя и папку PDF, затем откройте результат и проверьте разрывы страниц и мелкие детали.

Широкие таблицы получают альбомные страницы и сохраняют выравнивание столбцов. Длинные строки кода переносятся для печати. Картинки, схемы и большие формулы уменьшаются до области печати. Сложная диаграмма может потребовать увеличения при чтении PDF; иногда удобнее разбить её на несколько схем в исходнике.

Формулы и Mermaid работают без интернета. Удалённым изображениям нужно подключение. Локальные картинки должны находиться в папке документа или её подпапках; новый документ сначала сохраните в нужную папку.

Экспорт PDF не сохраняет исходный Markdown. Для `.md` отдельно нажмите **Ctrl+S**. Если в PDF что-то пропало, [создайте Issue](https://github.com/TrooSlash/Dokvi/issues) с небольшим примером и версией Dokvi.
