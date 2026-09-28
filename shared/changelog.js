/* Lamha — what changed in each version, in both interface languages. Shown in the Windows app's Settings → Updates
 * ("What's new"), and turned into the GitHub release notes by tools/release-notes.mjs in the release workflow, which
 * stops when the version being released has no entry here. Newest first; each note is [arabic, english]. */
// eslint-disable-next-line no-unused-vars
var LamhaChangelog = [
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
