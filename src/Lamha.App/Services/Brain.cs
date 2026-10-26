using System.Reflection;
using System.Runtime.InteropServices;
using Lamha.Core.Ai;
using Lamha.Core.Clipboard;
using Lamha.Core.Diagnostics;
using Lamha.Core.Money;
using Lamha.Core.Dictionary;
using Lamha.Core.Net;
using Lamha.Core.Review;
using Lamha.Core.Settings;
using Lamha.Core.Translation;
using Lamha.Core.Wikipedia;
using Lamha.Core.Writing;
using Lamha.Data.Database;
using Lamha.Data.Dictionary;
using Lamha.Windows.Security;
using Lamha.Windows.Shell;
using CoreLocalization = Lamha.Core.Settings.Localization;

namespace Lamha.App.Services;

/// <summary>
/// Lamha's brain (Lamha.Core) on its data (Lamha.Data) and Windows (Lamha.Windows): the composition root, as Core's
/// tests build it in <c>TestLamha</c>. The database, the settings and clipboard history open at start (the tray needs
/// them). Everything a lookup needs (HTTP, the dictionary file, the AI) is built at the first lookup and kept.
/// </summary>
public sealed partial class Brain : IDisposable
{
    private readonly Lazy<Lookups> _lookups;

    /// <param name="dataFolder">Where lamha.db lives: %LOCALAPPDATA%\Lamha Data, or a <c>--profile</c> folder.</param>
    /// <param name="keyPrefix">Credential Manager names start with it ("Lamha/"; another for a <c>--profile</c>).</param>
    public Brain(string dataFolder, string keyPrefix)
    {
        DataFolder = dataFolder;
        Database = LamhaDatabase.Open(Path.Combine(dataFolder, "lamha.db"));
        State = new SqliteStateStore(Database);
        Settings = new SettingsService(State);
        Secrets = new CredentialStore(keyPrefix);
        Localization = new CoreLocalization(Settings);
        ClipboardHistory = new ClipboardHistory(new SqliteClipStore(Database), new Dpapi(), State, Settings, TimeProvider.System);
        Reminder = new ReviewReminder(new SqliteDeckStore(Database), State, Settings, TimeProvider.System);
        _lookups = new Lazy<Lookups>(() => new Lookups(this));
    }

    /// <summary>"3.0.0-alpha.0+abc123": the build's version.</summary>
    public static string Version { get; } =
        typeof(Brain).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "3.0.0";

    public string DataFolder { get; }

    public LamhaDatabase Database { get; }

    public SqliteStateStore State { get; }

    public SettingsService Settings { get; }

    public CredentialStore Secrets { get; }

    /// <summary>The interface language as the brain sees it (the setting, or Windows').</summary>
    public CoreLocalization Localization { get; }

    public ClipboardHistory ClipboardHistory { get; }

    /// <summary>The tray's count of words waiting for review, and when to remind: read from the deck, without a lookup's parts.</summary>
    public ReviewReminder Reminder { get; }

    /// <summary>The review deck changed (once a lookup has built it), on the thread that changed it.</summary>
    public event Action? ReviewChanged;

    /// <summary>The lookup: dictionary, Google, the AI, Wikipedia (built at first use).</summary>
    public LookupService Lookup => _lookups.Value.Lookup;

    public WritingService Writing => _lookups.Value.Writing;

    public ReviewService Review => _lookups.Value.Review;

    /// <summary>Today: the day's goal, the streak and the word of the day.</summary>
    public TodayService Today => _lookups.Value.Today;

    /// <summary>The words looked up, newest first.</summary>
    public HistoryService History => _lookups.Value.History;

    public DiagnosticsReport Diagnostics => _lookups.Value.Diagnostics;

    /// <summary>Settings → A copy of my data: save a copy, and restore one (Lamha 2's format).</summary>
    public BackupService Backup => _lookups.Value.Backup;

    /// <summary>Lamha's tools on a copy in clipboard history (translate, write it in English, proofread, summarize, add to review).</summary>
    public ClipActions ClipActions => _lookups.Value.ClipActions;

    /// <summary>The AI: whether the writing tools and the translator are set up, and which.</summary>
    public AiService Ai => _lookups.Value.Ai;

    /// <summary>Translation on its own: the offline dictionary's definitions, translated after the card shows them.</summary>
    public TranslationService Translation => _lookups.Value.Translation;

    public WikipediaService Wikipedia => _lookups.Value.Wikipedia;

    /// <summary>Windows' network state (online, metered…).</summary>
    public INetworkStatus Network => _lookups.Value.Network;

    /// <summary>Prices in the user's currency, with the day's rates kept for offline.</summary>
    public CurrencyService Money => _lookups.Value.Money;

    /// <summary>Whether the offline dictionary file shipped with Lamha could be opened (after the first lookup).</summary>
    public bool HasDictionary => _lookups.IsValueCreated && _lookups.Value.DictionaryFile is not null;

    public void Dispose()
    {
        if (_lookups.IsValueCreated)
        {
            _lookups.Value.Dispose();
        }

        Database.Dispose();
    }

    /// <summary>What a lookup needs, made together at the first lookup.</summary>
    private sealed partial class Lookups : IDisposable
    {
        private readonly HttpClient _client;

        public Lookups(Brain brain)
        {
            var clock = TimeProvider.System;
            var settings = brain.Settings;
            var db = brain.Database;
            var history = new SqliteHistoryStore(db);
            var deck = new SqliteDeckStore(db);
            var mistakes = new SqliteMistakeStore(db);
            var activity = new SqliteActivityStore(db);
            var network = Network = new NetworkStatus();

            // Lamha's own timeouts apply per request (Core's Http); HttpClient's own would cut a slow AI answer short.
            _client = new HttpClient(new SocketsHttpHandler { PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1) })
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("Lamha/" + Version.Split('+')[0]);
            var http = new Http(_client, clock);

            DictionaryFile = OpenDictionary();
            var dictionary = DictionaryFile is null ? null : new LocalDictionary(DictionaryFile);
            var errorLog = new ErrorLog(new SqliteErrorLogStore(db), clock);
            var ai = Ai = new AiService(settings, brain.Secrets, http);
            var packs = new NoLanguagePacks(); // phase 5
            var translation = Translation = new TranslationService(settings, ai, new GoogleTranslate(http), new AiTranslator(ai), network);
            var today = Today = new TodayService(activity, brain.State, deck, history, settings, clock, dictionary);
            Review = new ReviewService(deck, brain.State, history, settings, today, clock);
            Review.Changed += () => brain.ReviewChanged?.Invoke();
            Writing = new WritingService(ai, settings, mistakes, brain.Localization, clock, errorLog);
            Lookup = new LookupService(
                settings, brain.Localization, translation, new WordExplainer(ai, packs), packs, History = new HistoryService(history, clock),
                Review, today, Wikipedia = new WikipediaService(http, network, Version), network, clock, dictionary, errorLog);
            ClipActions = new ClipActions(brain.ClipboardHistory, Lookup, Writing, Review, settings);
            Money = new CurrencyService(http, brain.State, network, clock);
            Diagnostics = new DiagnosticsReport(
                settings, brain.Localization, brain.Secrets, deck, history, mistakes, packs, errorLog, new HostInfo(settings), Version);
            Backup = new BackupService(Review, deck, history, mistakes, activity, brain.State, today, clock, Version);
        }

        public BackupService Backup { get; }

        public DictionaryFile? DictionaryFile { get; }

        public AiService Ai { get; }

        public TranslationService Translation { get; }

        public WikipediaService Wikipedia { get; }

        public INetworkStatus Network { get; }

        public CurrencyService Money { get; }

        public LookupService Lookup { get; }

        public ClipActions ClipActions { get; }

        public WritingService Writing { get; }

        public ReviewService Review { get; }

        public TodayService Today { get; }

        public HistoryService History { get; }

        public DiagnosticsReport Diagnostics { get; }

        public void Dispose()
        {
            _client.Dispose();
            DictionaryFile?.Dispose();
        }

        /// <summary>dict\lamha-dict.db next to Lamha.exe (built from Lamha 2's shards with the app); null when missing.</summary>
        private static DictionaryFile? OpenDictionary()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "dict", "lamha-dict.db");
            try
            {
                return File.Exists(path) ? new DictionaryFile(path) : null;
            }
            catch (InvalidDataException ex)
            {
                AppLog.Error("dictionary file", ex);
                return null;
            }
        }
    }

    /// <summary>The app's lines in Report a problem.</summary>
    private sealed class HostInfo(SettingsService settings) : IHostInfo
    {
        public string Host => $"Windows app (Windows 11 {Environment.OSVersion.Version.Build}, {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()})";

        public IReadOnlyList<string> Lines =>
        [
            $"Memory {Environment.WorkingSet / 1024 / 1024} MB",
            $"Clipboard history {(settings.Current.ClipboardEnabled ? "on" : "off")}",
        ];
    }
}
