/* Lamha — what changed in each version, in both interface languages. Shown in the Windows app's Settings → Updates
 * ("What's new"), and turned into the GitHub release notes by tools/release-notes.mjs in the release workflow, which
 * stops when the version being released has no entry here. Newest first; each note is [arabic, english]. */
// eslint-disable-next-line no-unused-vars
var LamhaChangelog = [
  { v: "1.9.8", date: "2026-10-01", notes: [
    ["تطبيق Windows يستهلك ذاكرة أقل بكثير: نحو ١١٥ ميغابايت بعد الاستخدام بدل ١٧٥، وقراءة الجملة حول الكلمة في البرامج الأخرى صارت أخف وأسرع.",
      "The Windows app uses much less memory: about 115 MB after use instead of 175, and reading the sentence around a word in other programs is lighter and quicker."],
    ["تطبيق Windows يرسم دون معالج الرسوميات ليوفّر الذاكرة. لحركة أسلس شغّل الإعدادات ← المظهر ← الرسم بمعالج الرسوميات (يُطبَّق عند إعادة التشغيل).",
      "The Windows app draws without the graphics card to save memory. For smoother animations, turn on Settings → Appearance → Draw with the graphics card (applies when Lamha restarts)."],
    ["أسرع دون إنترنت: مع «الإنترنت أولًا» تظهر الكلمة من القاموس المحلي خلال ثانيتين ونصف حين لا تجيب الشبكة، بدل نحو عشر ثوانٍ، وفورًا حين لا يوجد اتصال أصلًا.",
      "Faster with no internet: with Online first, a word comes from the offline dictionary within two and a half seconds when the network doesn't answer, instead of about ten, and at once when there's no connection at all."],
    ["تطبيق Windows: أُزيل زر «كتابة» الذي يظهر بنقرتين على مربع فارغ في البرامج الأخرى، لأنه لم يكن يظهر في كثير منها. Alt+Shift+W دون تحديد نص يفتح «كتابة جديدة» كما كان، وإضافة Firefox تحتفظ بزرها.",
      "Windows app: the Write button that appeared on a double-click in an empty box in other programs is gone, because it didn't show in many of them. Alt+Shift+W with nothing selected opens Write new as before, and the Firefox extension keeps its button."]
  ]},
  { v: "1.9.7", date: "2026-10-01", notes: [
    ["تطبيق Windows: الإعدادات ← التحديثات تعرض ما الجديد في إصدارك فقط؛ الإصدارات السابقة على صفحة لمحة في GitHub.",
      "Windows app: Settings → Updates shows what's new in your version only; earlier versions are on Lamha's GitHub page."],
    ["تطبيق Windows: العناصر المثبّتة في الحافظة تبقى في أعلى القائمة ونتائج البحث، والقائمة السريعة ما زالت تبدأ بآخر ما نسخته.",
      "Windows app: pinned clipboard items stay at the top of the list and of search results; the quick panel still starts on what you copied last."],
    ["القوائم المنسدلة وأسئلة التأكيد صارت بتصميم لمحة نفسه في كل مكان، بدل شكل النظام وأزراره.",
      "Dropdown menus and confirmation questions now look like the rest of Lamha everywhere, instead of the system's style and buttons."],
    ["علامة لمحة تُظهر ما تفعله أثناء الانتظار: صفحات كتاب تُقلَّب للكلمة، وعدسة تقرأ الجملة، وأسطر تُكتب حين يعمل الذكاء الاصطناعي.",
      "While you wait, the Lamha mark shows what it's doing: a book's pages turn for a word, a lens reads a sentence, and lines are written while the AI works."]
  ]},
  { v: "1.9.6", date: "2026-09-29", notes: [
    ["نسخة من بياناتك: احفظ بطاقات المراجعة وتقدّمك وسجل الكلمات وسجل أخطائك في ملف، واسترجعها على هذا الجهاز أو غيره دون أن يُحذف شيء (الإعدادات ← الخصوصية والسجل).",
      "A copy of your data: save your review cards and progress, word history and mistake journal in a file, and restore it on this device or another without losing anything (Settings → Privacy & history)."],
    ["بطاقات المراجعة مشتركة بين إضافة Firefox وتطبيق Windows حين يكونان متصلين: ما تضيفه أو تراجعه أو تحذفه في أحدهما يصل إلى الآخر.",
      "Review cards are shared between the Firefox extension and the Windows app once they're linked: what you add, review or delete in one reaches the other."],
    ["الإبلاغ عن مشكلة: تقرير بإعداداتك وآخر الأخطاء، دون نصوصك أو كلماتك أو مفاتيحك، تقرؤه أولًا ثم تنسخه أو ترسله على GitHub.",
      "Report a problem: a report of your settings and the latest errors, without your text, words or keys, that you read first and then copy or send on GitHub."],
    ["أسرع دون إنترنت: حين لا تجيب الشبكة تظهر الكلمة من القاموس المحلي خلال ثانيتين بدل الانتظار قرابة دقيقة، وتطبيق Windows يعرف متى لا يوجد اتصال فلا ينتظره.",
      "Faster offline: when the network doesn't answer, a word comes from the offline dictionary within two seconds instead of nearly a minute, and the Windows app knows when there's no connection at all."],
    ["أسرع عمومًا: أول بحث في القاموس المحلي، والبحث عبر الإنترنت مع الجملة، وتطبيق Windows مع قائمة مراجعة كبيرة.",
      "Faster in general: the first offline lookup, online lookups with their sentence, and the Windows app with a big review deck."],
    ["أمان أكثر: نوافذ تطبيق Windows لا تستطيع استخدام الكاميرا أو الميكروفون أو الموقع، والتطبيق يتحقق من ملفاته عند التشغيل، والمواقع لا تستطيع تقليد نقراتك لفتح لمحة.",
      "Safer: the Windows app's windows can't use the camera, microphone or location, the app checks its own files when it starts, and websites can't fake your clicks to open Lamha."],
    ["إصلاحات: كلمة «constructor» في القاموس المحلي، وأيام المواظبة ليلة تغيير الساعة، والكلمة التي تبحث عنها مجددًا تعود إلى أعلى السجل، وزر النطق في النافذة الرئيسية يستخدم صوت النظام دون إنترنت، ورسائل أوضح في مربع الترجمة السريعة، ورابط معطوب لم يعد يخفي مقالة ويكيبيديا.",
      "Fixes: the word “constructor” in the offline dictionary, streaks on the night the clocks change, a word you look up again goes back to the top of your history, the main window's 🔊 uses the system voice offline, clearer messages in the quick-translate box, and a broken link no longer hides a Wikipedia article."]
  ] },
  { v: "1.9.5", date: "2026-09-28", notes: [
    ["إصلاح: تنزيل ويكيبيديا من الإعدادات كان يتوقف فورًا برسالة «انقطع الاتصال». صار يعمل، والتنزيلات التي توقفت تكمل بزر «إعادة المحاولة».",
      "Fix: downloading Wikipedia in Settings stopped at once with “The connection dropped”. It works now, and stopped downloads carry on with Try again."],
    ["قائمة التنزيل لا تقول «عندك» إلا للملف الموجود فعلًا، وتقول «قيد التنزيل» أو «متوقف مؤقتًا» أو «لم يكتمل» لغيره.",
      "The download list only says “Downloaded” for a file that's really there, and “Downloading”, “Paused” or “Not finished” otherwise."],
    ["شريط التقدّم يمتد تحت اسم الملف وأزراره معًا، على سطر واحد مع الأزرار.",
      "The progress bar runs under the file's name and its buttons, lined up with them."]
  ] },
  { v: "1.9.4", date: "2026-09-28", notes: [
    ["ويكيبيديا دون إنترنت في تطبيق Windows: نزّلها من الإعدادات (من Kiwix، وأنت تختار اللغة والحجم)، فيظهر قسم ويكيبيديا في بطاقة البحث دون اتصال.",
      "Wikipedia without internet in the Windows app: download it in Settings (from Kiwix, in the language and size you choose), and the lookup card's Wikipedia part works with no connection."],
    ["قارئ ويكيبيديا في لمحة: مقالات كاملة بتصميم لمحة، مع بحث في العناوين والمحتويات وحجم النص، وحدّد أي كلمة لتراها في بطاقة لمحة.",
      "A Wikipedia reader in Lamha: whole articles in Lamha's design, with title search, contents and text size. Select any word to look it up."],
    ["إضافة Firefox تتصل بتطبيق Windows (الإعدادات ← لمحة لـ Windows) لتستخدم ويكيبيديا المنزّلة فيه وتفتح المقالات في قارئه.",
      "The Firefox extension can link to the Windows app (Settings → Lamha for Windows) to use its downloaded Wikipedia and open articles in its reader."],
    ["أزرار التنزيل في الإعدادات أهدأ.", "Download buttons in Settings are quieter."]
  ] },
  { v: "1.9.3", date: "2026-09-28", notes: [
    ["نقرتان على مربع نص فارغ تُظهران زر «كتابة» لتكتب فيه نصًّا جديدًا، في صفحات الويب وفي أي برنامج على Windows.",
      "Double-click an empty text box to get the Write button and write something new into it: on web pages, and in any program on Windows."],
    ["الكلمات تُشرح بلغتها في كل اللغات: العربية بالعربية مع الجذر والجمع، والفرنسية بالفرنسية… ومفتاح البطاقة يتذكّر اختيارك لكل لغة. والكلمة بلغة الترجمة نفسها تُشرح بدل أن تعود كما هي.",
      "Words explained in their own language, for every language: Arabic in Arabic with its root and plural, French in French… The card's switch remembers your choice for each language, and a word in your translation language is explained instead of given back as it is."],
    ["قواميس للتنزيل من الإعدادات: فرنسي–فرنسي وألماني–ألماني وإسباني–إسباني وتركي–تركي، تعمل دون إنترنت.",
      "Dictionaries to download in Settings: French–French, German–German, Spanish–Spanish and Turkish–Turkish, working offline."],
    ["ما الجديد في كل إصدار يظهر في تطبيق Windows: الإعدادات ← التحديثات.",
      "What's new in each version shows in the Windows app: Settings → Updates."],
    ["إصلاح: كلمة بحروف لاتينية ليست إنجليزية (مثل maison) لم تعد تُعامَل كأنها إنجليزية.",
      "Fix: a Latin-letter word that isn't English (like maison) is no longer treated as English."],
    ["رابط «اقرأ المزيد في ويكيبيديا» يبدأ من اليسار في الواجهة الإنجليزية، كبقية البطاقة.",
      "“Read more on Wikipedia” starts on the left in the English interface, like the rest of the card."]
  ] },
  { v: "1.9.2", date: "2026-09-28", notes: [
    ["أداة «أطول» للنص المحدد، ونبرة «مفصّل» في «كتابة جديدة» و«اكتب ردًّا».", "A Longer tool for selected text, and a Longer tone in Write new and Reply."],
    ["شريط أقسام الإعدادات لم يعد ينزلق تحت الفأرة، والمسافات بين الصفوف أوضح.", "Settings' section bar no longer slides under the mouse, and rows are spaced more clearly."],
    ["قسم التحديثات صار آخر أقسام الإعدادات.", "Updates is now the last section in Settings."]
  ] },
  { v: "1.9.1", date: "2026-09-27", notes: [
    ["الأزرار بجانب مربعات النص في الإعدادات صارت على سطرها.", "Buttons next to text boxes in Settings line up with them."],
    ["أزرار مسح الحافظة تبدو كأزرار حذف.", "The clipboard's clear buttons look like what they do: delete."],
    ["شريط الأقسام في تطبيق Windows لم يعد يعرض أقسام Firefox.", "The Windows app's section bar no longer lists Firefox-only sections."]
  ] },
  { v: "1.9.0", date: "2026-09-27", notes: [
    ["هدف يومي وسلسلة أيام وكلمة اليوم في نافذة لمحة.", "A daily goal, a streak of days and a word of the day in Lamha's window."],
    ["مراجعة أكثر حيوية: شريط تقدّم وملخّص في آخر الجلسة.", "A livelier review: a progress bar, and a summary at the end."],
    ["ألوان وأيقونات موحّدة في البطاقة والنوافذ، وأشرطة تمرير بألوان السمة.", "One set of colours and icons for the card and the windows, and scrollbars in the theme's colours."],
    ["لمسات صغيرة: علامة ✓ بعد النسخ، وموجات صوت أثناء النطق.", "Small touches: a ✓ after copying, and sound waves while speaking."]
  ] },
  { v: "1.8.4", date: "2026-09-27", notes: [
    ["إعداد «السمة» يُطبَّق على كل لمحة، لا على البطاقة وحدها.", "The Theme setting applies to all of Lamha, not only the card."]
  ] },
  { v: "1.8.3", date: "2026-09-27", notes: [
    ["القاموس الإنجليزي يختار المعنى الذي يناسب الجملة أيضًا («I mentioned» فعل، و«a mention» اسم).", "The English view picks the sense that fits the sentence too (“I mentioned” is the verb, “a mention” the noun)."],
    ["شريط بطاقة أنظف، وأدوات نص جديدة في الحافظة (Alt+Shift+V ثم Tab).", "A tidier card bar, and new text tools in the clipboard (Alt+Shift+V, then Tab)."]
  ] },
  { v: "1.8.2", date: "2026-09-27", notes: [
    ["قاموس إنجليزي–إنجليزي: تعريفات وأمثلة ومرادفات بالإنجليزية بدل الترجمة، ودون إنترنت.", "An English–English dictionary: English definitions, examples and synonyms instead of a translation, offline too."]
  ] },
  { v: "1.8.1", date: "2026-09-27", notes: [
    ["خدمة الترجمة: Google أو الذكاء الاصطناعي أو تلقائي، والجمل دون إنترنت مع Ollama.", "A translation service choice: Google, the AI or Automatic, and sentences offline with Ollama."],
    ["«✨ ترجمة أدق» تحت ترجمات Google.", "“✨ Better translation” under Google's translations."],
    ["الفقرات تبقى في الترجمة، والنص الطويل يُترجم على أجزاء، وترجمة الصفحة تعيد المحاولة لما فشل.", "Translations keep their paragraphs, long text is translated in parts, and page translation retries what failed."]
  ] },
  { v: "1.8.0", date: "2026-09-26", notes: [
    ["حركات يمكنك ضبطها: كاملة أو خفيفة أو متوقفة.", "Animations you can set: full, subtle or off."]
  ] },
  { v: "1.7.0", date: "2026-09-26", notes: [
    ["أرقام لأدوات الكتابة، و«كتابة جديدة» دون تحديد نص.", "Numbers for the writing tools, and Write new with nothing selected."]
  ] },
  { v: "1.6.3", date: "2026-09-26", notes: [
    ["نافذة تطبيق Windows تعرض الأدوات من جديد.", "The Windows app's main window shows the tools again."]
  ] },
  { v: "1.6.2", date: "2026-09-26", notes: [
    ["فهم الكلمة من جملتها في البرامج الأخرى، وتحسينات في الواجهة.", "Words understood from their sentence in other programs, and interface fixes."]
  ] },
  { v: "1.6.1", date: "2026-09-26", notes: [
    ["إصلاحات في الأمان والخصوصية.", "Security and privacy fixes."]
  ] },
  { v: "1.6.0", date: "2026-09-26", notes: [
    ["واجهة بالإنجليزية، و«مسح غير المثبّت» في الحافظة.", "An English interface, and “Clear unpinned” in the clipboard."]
  ] },
  { v: "1.5.0", date: "2026-09-26", notes: [
    ["إضافة Firefox وتطبيق Windows.", "The Firefox extension and the Windows app."]
  ] }
];
