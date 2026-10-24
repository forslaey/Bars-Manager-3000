using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class GuideSection
    {
        internal readonly string Title, Body;
        internal GuideSection(string title, string body) { Title = title; Body = body; }
        internal string Document { get { return Title + "\n\n" + Body.Replace("\r\n", "\n"); } }
        public override string ToString() { return Title; }
    }

    internal static class UserGuide
    {
        internal static readonly GuideSection[] Sections = {
            new GuideSection("Начало работы",
                "BARS Manager 3000 объединяет пять инструментов: управление службами, менеджер библиотек, установку программ, распаковку утилит и первичные системные настройки. Компоненты Windows открываются из менеджера библиотек.\n\n" +
                "1. Запусти EXE менеджера и выбери нужную карточку в главном окне.\n2. Проверь выбранный файл, папку или строку перед действием.\n3. Изменения запускаются кнопками соответствующего модуля. Если Windows запросит права администратора, проверь действие и подтверди запрос.\n4. Дождись результата и прочитай журнал. Успешное завершение стороннего установщика само по себе не заменяет проверку результата.\n\n" +
                "Кнопка «В меню» возвращает в главное окно. При активной операции сначала следуй предупреждению. В компактных окнах обслуживания Windows и утилит может потребоваться дождаться завершения.\n\n" +
                "Значок темы переключает светлое и тёмное оформление. Выбор сохраняется для следующих запусков. «О программе» содержит сведения о программе и авторе."),
            new GuideSection("Службы: установка и удаление",
                "Модуль управляет службами BARS на текущем компьютере.\n\n" +
                "Установка\n1. Выбери EXE службы или перетащи его в область выбора файла.\n2. Проверь найденную регистрацию. Если её нет, нажми «Установить службу».\n3. При необходимости открой «Настройка установки». Обычный режим вызывает EXE с ключом -i; отдельный режим .NET предназначен для совместимых служб с выбором учётной записи. Не выбирай его для произвольного EXE.\n4. Подтверди запрос Windows и дождись повторной проверки регистрации.\n\n" +
                "Удаление\nВыбери EXE и проверь связанные службы перед «Удалить службу». Обычный режим использует ключ -u. Если один EXE зарегистрировал несколько служб, процедура может удалить их все. Удаление регистрации не является удалением всех файлов программы.\n\n" +
                "Учётная запись\nВ настройках установки можно выбрать пользователя Windows. Запоминание пароля необязательно; сохранённые данные привязаны к текущему пользователю и компьютеру. Не передавай свои настройки другим людям.\n\n" +
                "Если EXE продолжает работать, прекращение ожидания не означает его принудительное завершение. Проверь регистрацию повторно и прочитай журнал."),
            new GuideSection("Службы: управление и состояния",
                "Выбери строку в таблице: действия выполняются для этой службы. «Запустить» запускает её, «Остановить» останавливает. В поле «Режим запуска» выбери нужный вариант и нажми «Применить». Для изменения Windows может запросить права администратора.\n\n" +
                "«Обновить статус» повторно читает службы Windows. Переключатель проверки каждые 10 секунд включает автоматическое обновление, пока модуль открыт. «Открыть службы» открывает стандартную оснастку Windows.\n\n" +
                "Что означают состояния\n«Работает» — Windows сообщает, что служба запущена. Это ещё не подтверждает её реальный отклик. «Остановлена» — служба не запущена. «Запускается», «Останавливается» и «Возобновляется» — переходные состояния. «Приостановлена» — выполнение приостановлено. «Не проверено» означает, что свежего подтверждения нет. При ошибке чтения может остаться последнее известное состояние: оно не считается свежей проверкой.\n\n" +
                "Под заголовком состояния нет повторных подписей. Подробность сообщения можно прочитать при наведении на заголовок; подробности ошибок и предупреждений также попадают в журнал.\n\n" +
                "Чтобы перезапустить службу вручную, сначала останови её и дождись подтверждённой остановки, затем запусти и проверь результат. Не выполняй перезапуск во время важной операции службы без согласования."),
            new GuideSection("Службы: сбор логов и ошибки",
                "1. Выбери службу в таблице.\n2. Нажми «Собрать логи».\n3. В окне сбора выбери нужные папки и место сохранения ZIP.\n4. Дождись сообщения о сохранении архива. Сбор не требует остановки службы.\n\n" +
                "При отмене незавершённый ZIP не сохраняется. Если файлов нет или они недоступны, прочитай сообщение об ошибке и проверь путь и права доступа.\n\n" +
                "Журнал текущего сеанса показывает действия менеджера и их результат. Это не постоянный монитор внутренних ошибок службы и не замена её собственным логам. Перед передачей архива проверь, нет ли в нём паролей, персональных данных и настроек подключения.\n\n" +
                "Если действие завершилось с ошибкой, прочитай подробности в журнале, обнови статус и при необходимости открой оснастку служб Windows. Если Windows не подтвердила ожидаемое состояние, не считай действие завершённым успешно."),
            new GuideSection("Библиотеки: проверка и выбор",
                "Менеджер библиотек проверяет компоненты Windows и сравнивает их с выбранным комплектом. Проверка сама по себе ничего не устанавливает.\n\n" +
                "1. Открой «Менеджер библиотек».\n2. Выбери папку комплекта «Библиотеки», если она не найдена автоматически.\n3. Запусти проверку и сравни версии в системе и в комплекте. Учитываются полная версия, разрядность и поколение компонента.\n4. Отметь нужные компоненты и выбери действие. Перед запуском проверь очередь.\n\n" +
                "«Установлено» означает, что компонент обнаружен. «Не найдено» — подходящая установка не подтверждена. «Нужна проверка» означает недостаток надёжных данных. Рекомендация об обновлении учитывает сравнение версий. Более новая установленная версия не должна заменяться старой из комплекта.\n\n" +
                "Для Visual C++ доступны два профиля с обязательным RuntimePack Lite. RuntimePack Full не используется. Разные поколения и разрядности могут требоваться одновременно: наличие x64 не заменяет x86. Зависимости SQL CLR/SMO и базовый runtime для ASP.NET добавляются автоматически. После установки, удаления или переустановки TXT-журнал автоматически сохраняется в папке «Журнал установки библиотек» внутри выбранной папки «Библиотеки». Кнопка «Открыть журнал» открывает эту папку. В журнале указаны компоненты, версии, результаты и причины остановки очереди."),
            new GuideSection("Библиотеки: действия и отмена",
                "«По рекомендации» обновляет подходящий компонент без предварительного удаления. «Переустановка» явно удаляет выбранные зарегистрированные продукты соответствующего поколения и разрядности, затем запускает пакет комплекта. «Удаление» использует зарегистрированные деинсталляторы. Если надёжного способа удаления нет, действие останавливается.\n\n" +
                "Перед подтверждением проверь очередь и предупреждения. Сортировка таблицы не меняет порядок зависимостей. При ошибке, требовании перезагрузки или неподтверждённом результате очередь останавливается.\n\n" +
                "Отмена\nНажми «Отменить операцию» и прочитай предупреждение. После подтверждения новые пакеты не запускаются; менеджер завершает процессы собственного задания. Общая служба Windows Installer и чужие установщики не завершаются. При закрытии во время активной операции также требуется подтверждение.\n\n" +
                "Принудительное прерывание не гарантирует откат. Системная часть Windows Installer может продолжить завершение операции или откат. После отмены дождись завершения системной операции и повтори проверку компонентов. Не запускай параллельно другую установку для исправления ещё незавершённой."),
            new GuideSection("Первичные системные настройки", "Четыре независимые группы: политики локальных паролей, режим администратора без запросов UAC, все профили брандмауэра и временная защита Defender. При открытии выполняется проверка. Изменения — только после нажатия кнопки и подтверждения.\n\nПрофиль BARS повторяет согласованный список политик. «Стандарт Windows» применяет стандартные значения, а не прежние настройки компьютера. Существующие пароли и состав группы администраторов не меняются. Параметры, отсутствующие в этой версии Windows, пропускаются с сообщением. На контроллере домена локальный профиль паролей не применяется; доменные политики имеют приоритет.\n\nПосле изменения UAC нужна перезагрузка. После изменения политик паролей перезагрузка также предлагается, но автоматически не выполняется. Брандмауэр переключается для доменного, частного и общедоступного профилей. SmartScreen для приложений отключается отдельной кнопкой для всех пользователей; «Стандарт Windows» возвращает режим предупреждений. Уже открытое предупреждение нужно закрыть и запустить приложение заново. Политики организации имеют приоритет. Defender может блокировать отключение защитой от изменений и включаться обратно самостоятельно. Результат проверяется по фактическому состоянию; блокировка не считается успехом."),
            new GuideSection("Компоненты Windows",
                "Открой менеджер библиотек и нажми «Компоненты». В отдельном окне показаны профиль IIS и компоненты Telnet/TFTP. При открытии состояние Windows проверяется автоматически.\n\n" +
                "1. Дождись окончания проверки.\n2. Отметь нужные компоненты.\n3. Проверь подпись действия: установка, удаление или оба действия.\n4. Подтверди операцию и дождись окончания.\n\n" +
                "Изменения выполняются средствами Windows без автоматической перезагрузки. Если система требует перезагрузку, сохрани работу и запланируй её. Отдельная кнопка «Перезагрузка» находится в менеджере библиотек; сначала прочитай подтверждение.\n\n" +
                "Обслуживание Windows нельзя безопасно прервать закрытием окна: дождись завершения. Если Windows запросит источник файлов или сообщит об ошибке, используй её сообщение для диагностики. «Назад» возвращает в менеджер библиотек."),
            new GuideSection("Программы: установка и обновление",
                "Поддерживаются Notepad++, 7-Zip, AnyDesk и RuDesktop. При открытии выполняется локальная проверка: установщики автоматически не скачиваются.\n\n" +
                "1. Выбери папку «Библиотеки», её подпапку _soft или контейнер с папкой «Библиотеки».\n2. Проверь четыре строки программ: версию в Windows и доступный пакет.\n3. Для получения свежих сведений из интернета нажми «Проверить версии». Эта кнопка получает метаданные и не скачивает установщики.\n4. Отметь программы и запусти установку или обновление. Та же или более новая установленная версия не предлагается как обновление старым пакетом.\n\n" +
                "Если подходящего локального пакета нет, установщик загружается при установке/обновлении с официального источника. Новый файл проверяется и сохраняется прямо в _soft выбранного комплекта. Если _soft отсутствует, она создаётся при загрузке. При недоступной для записи папке выбери доступный источник.\n\n" +
                "После сохранения нового пакета удаляются только распознанные старые установщики той же программы и разрядности. Посторонние и более новые файлы сохраняются. Скачанные установщики остаются в _soft для других компьютеров. Не изменяй пакет после проверки: неподтверждённый файл запускаться не должен."),
            new GuideSection("Программы: доступ и удаление",
                "Для AnyDesk и RuDesktop можно настроить неконтролируемый доступ при установке либо отдельно для уже установленного клиента. Введи пароль дважды: от 8 до 128 символов; значения должны совпадать. Запоминание пароля для текущей учётной записи необязательно. Прочитай предупреждения окна перед продолжением.\n\n" +
                "Наличие установленной программы и её пароля не гарантирует сетевую доступность: проверь подключение с другого компьютера. Настройка доступа может зависеть от версии и правил самого клиента.\n\n" +
                "Для Notepad++ и 7-Zip доступны предусмотренные модулем настройки ассоциаций файлов. После установки проверь их в Windows; поведение может зависеть от настроек пользователя.\n\n" +
                "Удаление\nВыбери нужные программы и действие удаления. Прочитай подтверждение: удаление может очищать настройки текущего пользователя и известные общие/служебные настройки. Другие профили пользователей не очищаются. Перед удалением Notepad++ сохрани важные несохранённые документы; резервные документы не заменяют собственную копию.\n\n" +
                "Отмена и выход во время операции требуют подтверждения. Прерывание стороннего установщика не гарантирует откат: проверь результат заново."),
            new GuideSection("Утилиты: установка и удаление",
                "Модуль распаковывает Piglet (DK-QStart.exe), fiLinn (cspPanel.exe), tftpd32 и WinSCP.\n\n" +
                "1. В поле «Будет взято из» выбери комплект утилит.\n2. В поле «Путь распаковки» укажи назначение.\n3. Реши, нужны ли ярлыки на рабочем столе: по умолчанию они включены.\n4. Нажми «Установить» и дождись результата.\n\n" +
                "Это копирование файлов утилит. Для четырёх распознанных EXE настраивается запуск от администратора для текущего пользователя; сами EXE не редактируются.\n\n" +
                "При удалении проверь путь и подтверждение. Менеджер удаляет только распознанные каталоги утилит и относящиеся к ним ярлыки. Не указывай папку с важными посторонними данными. Если каталог не распознан, не пытайся обойти проверку переименованием файлов — сначала проверь выбранный путь."),
            new GuideSection("Реальный отклик службы и Swagger",
                "В текущей версии автообновление читает состояния Windows. Проверки реального отклика и автоматического перезапуска по зависанию пока нет.\n\n" +
                "Почему «Работает» недостаточно\nСлужба может быть запущена, но не обрабатывать запросы. Отсутствие ошибок в логах, низкая загрузка процессора и открытый порт не доказывают исправность. Нужен безопасный диагностический запрос, который выполняет сама служба.\n\n" +
                "Что такое Swagger\nЭто страница с описанием API: какие запросы понимает служба, какие параметры нужны и что она возвращает. Доступность страницы Swagger не равна исправности всех функций.\n\n" +
                "Как найти проверку\n1. Открой известный адрес Swagger нужной службы в браузере. Адрес зависит от настройки конкретной службы.\n2. Найди методы со словами health, ready, live, ping или status. Такие имена — лишь подсказка; прочитай описание.\n3. Проверь назначение метода, параметры, авторизацию и ожидаемый ответ. Для мониторинга подходит только подтверждённая безопасная проверка без изменения данных. Не запускай неизвестные команды через «Try it out». Даже метод GET сначала нужно проверить по описанию.\n4. Передай администратору адрес Swagger или описание выбранного метода без паролей и токенов. По нему можно определить пригодность для мониторинга.\n\n" +
                "Как может работать будущий монитор\nПроверять Windows и отдельный диагностический адрес с тайм-аутом. Различать «Отвечает», «Не ответила за отведённое время», «Ответила с ошибкой» и «Проверка не настроена». После нескольких последовательных сбоев показывать предупреждение и предлагать ручной перезапуск с подтверждением. Ошибка базы данных или авторизации требует своего решения: перезапуск службы может не помочь.\n\n" +
                "Универсально доказать зависание любой службы снаружи невозможно. Для служб без подходящего API нужна отдельная проверка по их протоколу или поддержка разработчика. Конкретные адреса BARS в этой версии не настроены и не проверены."),
            new GuideSection("Поиск по инструкции",
                "Поле «Поиск по всей инструкции» ищет фразу в названиях и полном тексте разделов без учёта регистра. Слева остаются только разделы с совпадениями. Справа показан выбранный раздел, найденные фрагменты подсвечены.\n\n" +
                "«Далее» и «Назад» переходят между всеми совпадениями, в том числе в других разделах. После последнего совпадения поиск продолжается с первого. Счётчик показывает номер текущего совпадения и общее количество.\n\n" +
                "Ctrl+F переводит курсор в поиск. F3 ищет следующее совпадение, Shift+F3 — предыдущее. «Сбросить» очищает запрос и возвращает все разделы. Текст справа можно выделить и скопировать сочетанием Ctrl+C. Размер окна можно менять; длинные разделы прокручиваются.\n\n" +
                "Инструкция встроена в EXE и доступна без интернета. Закрой окно кнопкой «Закрыть» для возврата в лаунчер.")
        };

        internal static List<int> Find(string text, string query)
        {
            var result = new List<int>();
            if (String.IsNullOrWhiteSpace(query)) return result;
            for (int start = 0; start <= text.Length - query.Length; )
            {
                int index = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) break;
                result.Add(index); start = index + query.Length;
            }
            return result;
        }
    }

    internal sealed class UserGuideForm : ManagerModuleForm
    {
        private readonly BrandTextBox search = new BrandTextBox();
        private readonly GuideSectionListBox sections = new GuideSectionListBox();
        private readonly GuideReader reader = new GuideReader();
        private readonly GuideReaderScrollBar readerScroll=new GuideReaderScrollBar();
        private readonly GuideSectionScrollBar sectionScroll=new GuideSectionScrollBar();
        private readonly Label results = new Label();
        private readonly Label searchCaption;
        private readonly BrandButton previous, next;
        private readonly List<Tuple<GuideSection, int>> hits = new List<Tuple<GuideSection, int>>();
        private int activeHit = -1;
        private bool binding;
        private string Query { get { return search.Text.Trim(); } }

        public UserGuideForm() : base("Инструкция — BARS Manager 3000", new Size(1100, 780))
        {
            MinimumSize = new Size(940, 660); StartPosition = FormStartPosition.CenterParent;
            Root.RowStyles[0].Height = 72;
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            var find = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, Margin = Padding.Empty };
            find.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); find.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            for (int i = 0; i < 3; i++) find.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            find.RowStyles.Add(new RowStyle(SizeType.Absolute,24));find.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            searchCaption = Caption("Поиск по всей инструкции:", false);
            searchCaption.Font = new Font(Font, FontStyle.Bold);
            find.Controls.Add(searchCaption, 0, 0);
            search.Dock = DockStyle.None;search.Anchor=AnchorStyles.Left|AnchorStyles.Right; search.AutoSize = true; search.Margin = new Padding(0, 0, 10, 0); search.AccessibleName = "Поиск по всей инструкции";
            find.Controls.Add(search, 0, 1);search.Height=search.PreferredHeight;search.FontChanged+=delegate{search.Height=search.PreferredHeight;};
            previous = Button("Назад", false); next = Button("Далее", false); var clear = Button("Сбросить", false); clear.Margin = Padding.Empty;
            previous.Click += delegate { MoveHit(-1); }; next.Click += delegate { MoveHit(1); }; clear.Click += delegate { search.Clear(); search.Focus(); };
            find.Controls.Add(previous, 2, 1); find.Controls.Add(next, 3, 1); find.Controls.Add(clear, 4, 1); body.Controls.Add(find, 0, 0);
            results.Dock = DockStyle.Fill; results.TextAlign = ContentAlignment.MiddleLeft; results.Tag = "muted"; results.Margin = Padding.Empty; results.AccessibleName = "Результаты поиска";
            body.Controls.Add(results, 0, 1);
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 275)); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sections.Dock = DockStyle.Fill; sections.IntegralHeight = false; sections.BorderStyle = BorderStyle.FixedSingle; sections.Margin = Padding.Empty;
            sections.Font = new Font("Segoe UI", 10F); sections.AccessibleName = "Разделы инструкции";
            sections.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                Color background = selected ? Colors.SurfaceAlt : Colors.Surface;
                using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
                var bounds = sections.TitleBounds(e.Graphics,e.Bounds);
                TextRenderer.DrawText(e.Graphics, sections.Items[e.Index].ToString(), sections.Font, bounds, Colors.Text,
                    GuideSectionListBox.TitleFlags | TextFormatFlags.VerticalCenter);
                if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
            };
            reader.Dock = DockStyle.Fill; reader.ReadOnly = true; reader.BorderStyle = BorderStyle.None; reader.Margin = Padding.Empty;
            reader.Font = new Font("Segoe UI", 11F); reader.DetectUrls = false; reader.WordWrap = true; reader.HideSelection = true;
            reader.ScrollBars = RichTextBoxScrollBars.None; reader.AccessibleName = "Текст инструкции";
            var readingCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(16), Margin = Padding.Empty };
            readerScroll.Dock=DockStyle.Right;readerScroll.Width=14;readerScroll.Bind(reader);readingCard.Controls.Add(reader);readingCard.Controls.Add(readerScroll);
            var sectionSlot=new Panel{Dock=DockStyle.Fill,Margin=new Padding(0,0,14,0)};sectionScroll.Dock=DockStyle.Right;sectionScroll.Width=14;sectionScroll.Bind(sections);sectionSlot.Controls.Add(sections);sectionSlot.Controls.Add(sectionScroll);content.Controls.Add(sectionSlot, 0, 0); content.Controls.Add(readingCard, 1, 0); body.Controls.Add(content, 0, 2);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 8, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            footer.Controls.Add(Label("Ctrl+F — поиск; F3 / Shift+F3 — переход к совпадению", true), 0, 0);
            var close = Button("Закрыть", false); close.Margin = Padding.Empty; close.Click += delegate { Close(); }; footer.Controls.Add(close, 1, 0); body.Controls.Add(footer, 0, 3);
            Root.Controls.Add(body, 0, 1);
            search.TextChanged += delegate { Filter(); };
            sections.SelectedIndexChanged += delegate { if (!binding) ShowSection(true); };
            SetTheme(Colors.Dark, false); Filter();
        }

        private void Filter()
        {
            GuideSection selected = sections.SelectedItem as GuideSection;
            binding = true; sections.BeginUpdate(); sections.Items.Clear(); hits.Clear(); activeHit = -1;
            foreach (GuideSection section in UserGuide.Sections)
            {
                List<int> positions = UserGuide.Find(section.Document, Query);
                if (Query.Length == 0 || positions.Count > 0) sections.Items.Add(section);
                foreach (int position in positions) hits.Add(Tuple.Create(section, position));
            }
            if (sections.Items.Count > 0) sections.SelectedItem = selected != null && sections.Items.Contains(selected) ? selected : sections.Items[0];
            sections.EndUpdate();sections.RefreshHeights();binding = false; ShowSection(true);
        }

        private void ShowSection(bool selectFirst)
        {
            reader.BeginFormatting();try{
            GuideSection section = sections.SelectedItem as GuideSection;
            reader.Text = section == null ? "Ничего не найдено. Сократи запрос или нажми «Сбросить»." : section.Document;
            reader.SelectAll(); reader.SelectionColor = Colors.Text; reader.SelectionBackColor = Colors.Surface;
            reader.SelectionFont = reader.Font;
            if (section != null)
            {
                reader.Select(0, section.Title.Length);
                using (var titleFont = new Font(reader.Font.FontFamily, 15F, FontStyle.Bold)) reader.SelectionFont = titleFont;
                foreach (int position in UserGuide.Find(reader.Text, Query))
                { reader.Select(position, Query.Length); reader.SelectionBackColor = Colors.Dark ? Color.FromArgb(100, 85, 24) : Color.FromArgb(255, 235, 150); }
            }
            reader.Select(0, 0);
            if (selectFirst) activeHit = hits.FindIndex(hit => hit.Item1 == section);
            SelectHit();
            }finally{reader.EndFormatting();}
        }

        private void MoveHit(int delta)
        {
            if (hits.Count == 0) return;
            activeHit = (activeHit + delta + hits.Count) % hits.Count;
            binding = true; sections.SelectedItem = hits[activeHit].Item1; binding = false;
            ShowSection(false);
        }

        private void SelectHit()
        {
            previous.Enabled = next.Enabled = hits.Count > 0;
            ((TableLayoutPanel)results.Parent).RowStyles[1].Height=(float)((Query.Length==0?8:24)*Root.RowStyles[0].Height/72F);results.Visible=Query.Length>0;
            results.Text = Query.Length == 0 ? "" : hits.Count == 0 ? "Совпадений нет" :
                "Совпадение " + (activeHit + 1) + " из " + hits.Count + "; разделов: " + sections.Items.Count;
            if (activeHit >= 0 && activeHit < hits.Count)
            { reader.Select(hits[activeHit].Item2, Query.Length); reader.ScrollToCaret();reader.Select(hits[activeHit].Item2,0); }
            else { reader.Select(0, 0); reader.ScrollToCaret(); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F)) { search.Focus(); search.SelectAll(); return true; }
            if (keyData == Keys.F3 || keyData == (Keys.Shift | Keys.F3)) { MoveHit(keyData == Keys.F3 ? 1 : -1); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnThemeChanged()
        {
            if (searchCaption != null) searchCaption.ForeColor = Colors.Dark ? Color.Yellow : Color.Black;
            if (reader == null || sections == null) return;
            sectionScroll.Palette=Colors;sectionScroll.BackColor=Colors.Surface;readerScroll.Palette=Colors;readerScroll.BackColor=Colors.Surface;reader.BackColor = sections.BackColor = Colors.Surface; reader.ForeColor = sections.ForeColor = Colors.Text;
            if (reader.IsHandleCreated) ShowSection(false);
        }
    }
}
