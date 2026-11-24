using System.Globalization;

namespace HddSpindown;

/// <summary>
/// Simple bilingual support (German/English). Defaults to the Windows display language:
/// German for "de-*", otherwise English. Can be overridden via "Language" in settings.json.
/// </summary>
public static class Loc
{
    public static bool German { get; private set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";

    public static void Init(string? language)
    {
        German = language?.ToLowerInvariant() switch
        {
            "de" => true,
            "en" => false,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de",
        };
    }

    public static string T(string de, string en) => German ? de : en;

    public const string AppName = "HDD-Spindown";

    // ---------- Texts for XAML ({x:Static local:Loc.*}) ----------
    public static string HeaderTitle => T("Festplatten", "Hard drives");
    public static string HeaderSubtitle => T(
        "Ausgewählte Festplatten sperren, offline schalten und schlafen legen, damit Windows sie nicht ständig aufweckt.",
        "Lock, take offline and spin down selected hard drives so Windows doesn't keep waking them up.");
    public static string Refresh => T("Aktualisieren", "Refresh");
    public static string Refreshing => T("Datenträger werden eingelesen …", "Scanning drives …");
    public static string RefreshTip => T("Datenträger neu einlesen", "Rescan drives");
    public static string SleepSelected => T("Ausgewählte schlafen", "Spin down selected");
    public static string SleepSelectedTip => T("Legt alle Festplatten mit „Beim Start schlafen“ schlafen",
                                               "Spins down all drives marked “Spin down at startup”");
    public static string WakeAll => T("Alle aufwecken", "Wake all");
    public static string WakeAllTip => T("Hebt alle Sperren dieser App auf und weckt die Festplatten",
                                         "Releases all locks held by this app and wakes the drives");
    public static string ModeTip => T("Was beim Schlafen zusätzlich passiert", "What else happens when spinning down");
    public static string AutoSleep => T("Beim Start schlafen", "Spin down at startup");
    public static string AutoSleepTip => T("Beim Programmstart und bei „Ausgewählte schlafen“ automatisch schlafen legen",
                                           "Spin down automatically at startup and with “Spin down selected”");
    public static string Wake => T("Aufwecken", "Wake up");
    public static string WakeTip => T("Sperren aufheben und aufwecken", "Release locks and wake up");
    public static string LockOnly => T("Nur sperren", "Lock only");
    public static string LockOnlyTip => T("Offline schalten bzw. deaktivieren, ohne schlafen zu legen",
                                          "Take offline or disable without spinning down");
    public static string NoDisks => T("Keine Festplatten gefunden.", "No drives found.");
    public static string Log => T("Protokoll", "Log");

    // ---------- Settings ----------
    public const string Author = "Ronny Severin";
    public static string Settings => T("Einstellungen", "Settings");
    public static string Features => T("Funktionen", "Features");
    public static string Help => T("Hilfe", "Help");
    public static string Licenses => T("Lizenzen", "Licenses");
    public static string Privacy => T("Datenschutz", "Privacy");
    public static string LicensesNav => T("Lizenzen & Datenschutz", "Licenses & privacy");
    public static string Folders => T("Ordner", "Folders");
    public static string FeaturesSubtitle => T("Programminfo, Ordner und Optionen", "App info, folders and options");
    public static string HelpSubtitle => T("So holst du das Beste aus HDD-Spindown heraus", "How to get the most out of HDD-Spindown");
    public static string LicensesSubtitle => T("Verwendete Komponenten und der Umgang mit deinen Daten", "Components used and how your data is handled");
    public static string LicenseText => T("Lizenztext", "License text");
    public static string Support => T("Unterstützung", "Support");
    public static string ContentFollows => T("Inhalt folgt.", "Content coming soon.");
    public static string VersionLabel => T("Version", "Version");
    public static string AuthorLine => T($"{Author} • Open Source unter MIT-Lizenz", $"{Author} • Open source under the MIT License");
    public static string SettingsFolderLabel => T("Einstellungsordner", "Settings folder");
    public static string ProgramFolderLabel => T("Programmordner", "Program folder");
    public static string OpenFolder => T("Öffnen", "Open");
    public static string CopyPath => T("Pfad kopieren", "Copy path");
    public static string CopyCommand => T("Befehl kopieren", "Copy command");

    // ---------- Support ----------
    public static string SupportSubtitle => T("Wenn dir HDD-Spindown gefällt", "If you like HDD-Spindown");
    public static string SupportHeadline => T("Kostenlos, und das bleibt so.", "Free, and it stays that way.");
    public static string SupportText => T(
        "HDD-Spindown ist kostenlos und Open Source, ohne Werbung, ohne Abo und ohne Datensammlung. " +
        "Wenn dir die App etwas Ruhe am Schreibtisch und ein paar Watt auf der Stromrechnung spart, freue ich mich über einen Kaffee. " +
        "Jede Unterstützung hilft, HDD-Spindown weiterzuentwickeln.",
        "HDD-Spindown is free and open source, with no ads, no subscription and no data collection. " +
        "If the app brings a little quiet to your desk and saves a few watts on your power bill, I'd be happy about a coffee. " +
        "Every bit of support helps keep HDD-Spindown going.");
    public const string BuyCoffee = "Buy me a coffee";
    public static string SupportVoluntary => T("Freiwillig: Alle Funktionen stehen allen kostenlos zur Verfügung.",
                                               "Voluntary: every feature is free for everyone.");
    public static string MoreWays => T("Auch so hilfst du weiter", "Other ways to help");
    public static string ShareTitle => T("Weiterempfehlen", "Spread the word");
    public static string ShareText => T("Erzähl Freunden und Kollegen mit lauten Festplatten von HDD-Spindown.",
                                        "Tell friends and colleagues with noisy hard drives about HDD-Spindown.");
    public static string StarTitle => T("Stern auf GitHub", "Star on GitHub");
    public static string StarText => T("Ein Stern für HDD-Spindown auf GitHub hilft anderen, das Projekt zu finden.",
                                       "A star for HDD-Spindown on GitHub helps others find the project.");
    public static string StarLink => T("HDD-Spindown auf GitHub", "HDD-Spindown on GitHub");
    public static string FeedbackTitle => T("Feedback geben", "Give feedback");
    public static string FeedbackText => T("Ideen und Fehlermeldungen, gern als Issue auf GitHub, machen HDD-Spindown besser.",
                                           "Ideas and bug reports, ideally as a GitHub issue, make HDD-Spindown better.");
    public static string FeedbackLink => T("Issue erstellen", "Open an issue");
    public static string LinkFollows => T("Link folgt in Kürze", "Link coming soon");
    public static string Back => T("Zurück", "Back");
    public static string Options => T("Optionen", "Options");

    public static string OptTooltipTitle => T("Gerätepfad beim Darüberfahren", "Device path on hover");
    public static string OptTooltipDesc => T("Zeigt den technischen Gerätepfad als Tooltip über dem Laufwerksnamen.",
                                             "Shows the technical device path as a tooltip over the drive name.");
    public static string OptAutostartTitle => T("Mit Windows starten", "Start with Windows");
    public static string OptAutostartDesc => T("Startet HDD-Spindown beim Anmelden im Infobereich (über die Aufgabenplanung, ohne UAC-Abfrage).",
                                               "Starts HDD-Spindown in the notification area when you sign in (via Task Scheduler, without a UAC prompt).");
    public static string TagButton => T("Etikett", "Label");
    public static string TagAdd => T("Etikett hinzufügen", "Add label");
    public static string TagAddShort => T("Hinzufügen", "Add");
    public static string TagRemove => T("Etikett entfernen", "Remove label");
    public static string TagsTitle => T("Etikett für diese Platte", "Label for this drive");
    public static string TagChangeColor => T("Klicken, um die Farbe zu ändern", "Click to change the color");
    public static string TagColorFor => T("Farbe für", "Color for");
    public static string TagColorHint => T("Gilt für dieses Etikett auf allen Platten.", "Applies to this label on all drives.");
    public static string TagColorLabel => T("Farbe", "Color");
    public static string TagSuggestionsLabel => T("Vorschläge", "Suggestions");
    public static string TagLimit => T($"Bis zu {DiskItem.MaxTags} Etiketten je Platte, je höchstens {DiskItem.MaxTagLength} Zeichen.",
                                       $"Up to {DiskItem.MaxTags} labels per drive, {DiskItem.MaxTagLength} characters each.");
    public static string OptLanguageTitle => T("Sprache", "Language");
    public static string OptLanguageDesc => T("Standard ist die Anzeigesprache von Windows. Andere Sprachen fallen auf Englisch zurück.",
                                              "Defaults to the Windows display language. Other languages fall back to English.");
    public static string LangAuto => T("Automatisch (Windows)", "Automatic (Windows)");
    public static string RestartNeeded => T("Wird nach einem Neustart der App übernommen.", "Takes effect after restarting the app.");
    public static string RestartNow => T("Jetzt neu starten", "Restart now");
    public static string OptNotifyTitle => T("Benachrichtigungen", "Notifications");
    public static string OptNotifyDesc => T("Kurze Meldung im Infobereich, wenn eine Festplatte schlafen gelegt oder aufgeweckt wurde. Fehler werden immer gemeldet.",
                                            "Short notification when a drive was spun down or woken up. Errors are always reported.");
}
