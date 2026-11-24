using System.Runtime.InteropServices;

namespace HddSpindown;

/// <summary>List item: optional bold caption (or command in monospace) and description.</summary>
/// <remarks>Mono = command (field with copy button), Swatch = state color (badge like in the main view).</remarks>
public sealed record HelpItem(string? Label, string Text, bool Mono = false, string? Swatch = null)
{
    public bool HasLabel => Label is not null;
    public bool IsCommand => Mono;
    public bool IsSwatch => Swatch is not null;
    public bool IsBullet => !Mono && Swatch is null;
    public bool ShowBulletLabel => IsBullet && HasLabel;
}

/// <summary>Expandable section: intro, list, closing (each optional).</summary>
public sealed record HelpTopic(string Glyph, string Title, string Summary, string? Intro, IReadOnlyList<HelpItem> Items, string? Outro)
{
    public bool HasIntro => Intro is not null;
    public bool HasItems => Items.Count > 0;
    public bool HasOutro => Outro is not null;
}

public sealed record LicenseEntry(string Glyph, string Name, string Version, string License, string Copyright, string? Note, string? Text)
{
    public bool HasText => Text is not null;
    public bool HasNote => Note is not null;
    public string Meta => (char.IsDigit(Version[0]) ? $"{Loc.VersionLabel} {Version}" : Version) + $"  ·  {License}";
}

/// <summary>Content of the settings pages (bilingual). Deliberately written without dashes.</summary>
public static class SettingsContent
{
    private static HelpItem I(string? labelDe, string? labelEn, string de, string en, bool mono = false) =>
        new(labelDe is null ? null : Loc.T(labelDe, labelEn!), Loc.T(de, en), mono);

    private static HelpItem C(string command, string de, string en) => new(command, Loc.T(de, en), Mono: true);

    private static HelpItem S(string de, string en, string color, string textDe, string textEn) =>
        new(Loc.T(de, en), Loc.T(textDe, textEn), Swatch: color);

    public static IReadOnlyList<HelpTopic> HelpTopics() =>
    [
        new("",
            Loc.T("Worum es geht", "What it's for"),
            Loc.T("Festplatten gezielt schlafen legen und schlafen lassen", "Spin drives down and keep them asleep"),
            Loc.T("Windows weckt Festplatten gern wieder auf, auch wenn gerade niemand sie braucht. HDD-Spindown sperrt die Laufwerke einer Platte oder schaltet sie offline und schickt sie erst dann in den Ruhezustand. So bleibt sie wirklich still.",
                  "Windows likes to wake hard drives up even when nobody needs them. HDD-Spindown locks a drive's volumes or takes it offline first and only then sends it to standby. That way it really stays quiet."),
            [
                I("Leiser", "Quieter", "Keine Platte dreht, die gerade niemand braucht.", "No drive spins that nobody is using."),
                I("Sparsamer", "More efficient", "Ruhende Platten brauchen kaum Strom.", "Idle drives use hardly any power."),
                I("Kühler", "Cooler", "Weniger Wärme im Gehäuse, besonders bei mehreren Platten.", "Less heat in the case, especially with several drives."),
            ],
            null),

        new("",
            Loc.T("Die drei Modi", "The three modes"),
            Loc.T("Wie gründlich eine Platte abgeschirmt wird", "How thoroughly a drive is shielded"),
            null,
            [
                I("Sperren + schlafen", "Lock + spin down",
                  "Die Laufwerke werden gesperrt und ausgehängt, danach schläft die Platte. Die Sperre hält, solange die App läuft.",
                  "The volumes are locked and dismounted, then the drive spins down. The lock holds as long as the app is running."),
                I("Offline + schlafen", "Offline + spin down",
                  "Zusätzlich wird die Platte offline geschaltet. Das ist die zuverlässigste Variante. Nach einem Neustart ist sie automatisch wieder da.",
                  "Additionally takes the drive offline. This is the most reliable option. After a restart the drive is back automatically."),
                I("Offline + schlafen + deaktivieren", "Offline + spin down + disable",
                  "Zusätzlich wird das Gerät im Geräte-Manager deaktiviert. Das bleibt auch über einen Neustart hinweg so, bis du die Platte aufweckst.",
                  "Additionally disables the device in Device Manager. This persists across restarts until you wake the drive."),
            ],
            Loc.T("Tipp: In den meisten Fällen ist „Offline + schlafen“ die beste Wahl.",
                  "Tip: in most cases “Offline + spin down” is the best choice.")),

        new("",
            Loc.T("Schaltflächen", "Buttons"),
            Loc.T("Was die Buttons auf den Karten tun", "What the buttons on the cards do"),
            null,
            [
                I("Schlafen (Mond)", "Spin down (moon)", "Sperrt die Platte gemäß Modus und legt sie schlafen.", "Locks the drive according to its mode and spins it down."),
                I("Nur sperren (Schloss)", "Lock only (padlock)", "Schaltet offline oder deaktiviert, ohne den Schlafbefehl zu senden.", "Takes it offline or disables it without sending the spin-down command."),
                I("Aufwecken (Sonne)", "Wake up (sun)", "Hebt alle Sperren auf und lässt die Platte anlaufen.", "Releases all locks and spins the drive up."),
                I("Ausgewählte schlafen", "Spin down selected", "Legt alle Platten mit „Beim Start schlafen“ schlafen.", "Spins down all drives marked “Spin down at startup”."),
                I("Alle aufwecken", "Wake all", "Hebt alle Sperren dieser App auf einmal auf.", "Releases all locks held by this app at once."),
            ],
            Loc.T("Im schmalen Fenster zeigen die Buttons nur ihr Symbol. Fährst du mit der Maus darüber, erscheint die Beschriftung.",
                  "In a narrow window the buttons only show their icon. Hover over them to see the label.")),

        new("",
            Loc.T("Beim Start schlafen", "Spin down at startup"),
            Loc.T("Platten automatisch nach dem Start schlafen legen", "Spin drives down automatically after startup"),
            Loc.T("Markierte Platten schlafen ein, sobald die App startet. Zusammen mit „Mit Windows starten“ passiert das direkt nach der Anmeldung, ganz ohne Klick.",
                  "Marked drives spin down as soon as the app starts. Combined with “Start with Windows” this happens right after you sign in, without a single click."),
            [],
            Loc.T("Im schmalen Fenster ersetzt das Ein/Aus-Symbol den Text neben dem Kästchen.",
                  "In a narrow window the power symbol replaces the text next to the checkbox.")),

        new("",
            Loc.T("Etiketten", "Labels"),
            Loc.T("Platten mit eigenen Namen wie „Filme“ oder „Backup“ versehen", "Tag drives with your own names like “Movies” or “Backup”"),
            Loc.T("Fahre mit der Maus über eine Karte, dann erscheint neben dem Namen „+ Etikett“. Dort wählst du einen Vorschlag oder tippst ein eigenes Etikett ein und bestätigst mit Enter.",
                  "Hover over a card and “+ Label” appears next to the name. Pick a suggestion or type your own label and press Enter."),
            [
                I("Farbe", "Color", "Beim Hinzufügen eine Farbe wählen oder später auf das Etikett klicken. Die Farbe gilt für dieses Etikett auf allen Platten.", "Pick a color when adding, or click the label later. The color applies to that label on all drives."),
                I("Entfernen", "Removing", "Fahre über ein Etikett und klicke auf das kleine x.", "Hover over a label and click the small x."),
                I("Im Infobereich", "In the notification area", "Etiketten erscheinen auch im Menü des Tray-Symbols, damit du Platten dort schneller findest.", "Labels also show in the tray icon's menu so you can find drives faster."),
            ],
            Loc.T($"Bis zu {DiskItem.MaxTags} Etiketten je Platte sind möglich.", $"Up to {DiskItem.MaxTags} labels per drive are possible.")),

        new("",
            Loc.T("Zustandsanzeige", "Status display"),
            Loc.T("Farben und Zeitangaben auf einen Blick", "Colors and times at a glance"),
            null,
            [
                S("Aktiv", "Active", "#6CCB5F", "Die Platte läuft oder ist bereit. Bei Platten ohne Abfrage steht hier „Online“.", "The drive is spinning or ready. Drives that can't be queried show “Online”."),
                S("Schläft", "Spun down", "#60CDFF", "Im Ruhezustand. Mit dem Zusatz „gesperrt“ oder „offline“, wenn die App das veranlasst hat.", "In standby. With “locked” or “offline” added if the app did this."),
                S("Gesperrt", "Locked", "#FCE100", "Von der App gesperrt, die Platte dreht aber noch.", "Locked by the app, but the drive is still spinning."),
                S("Offline", "Offline", "#9A9A9A", "Offline, deaktiviert oder nicht verfügbar.", "Offline, disabled or unavailable."),
            ],
            Loc.T("Die Detailzeile verrät, seit wann eine Platte schläft oder wann sie aufgewacht ist. Das klappt auch, wenn Windows sie selbst schlafen gelegt hat. SATA-Platten werden dafür alle 15 Sekunden abgefragt, ohne sie zu wecken.",
                  "The detail line tells you since when a drive has been asleep or when it woke up. This also works if Windows spun it down by itself. SATA drives are checked every 15 seconds for this without waking them.")),

        new("",
            Loc.T("Geschützte Laufwerke", "Protected drives"),
            Loc.T("NVMe-SSDs und das Systemlaufwerk bleiben unangetastet", "NVMe SSDs and the system drive are never touched"),
            Loc.T("Diese Laufwerke zeigt die App nur an. Sie sind ausgegraut, haben keine Buttons und reagieren auch nicht auf Befehle über die Kommandozeile. So kann nichts Wichtiges versehentlich abgeschaltet werden.",
                  "The app only displays these drives. They are greyed out, have no buttons and ignore command-line commands as well. That way nothing important can be switched off by accident."),
            [],
            null),

        new("",
            Loc.T("Infobereich", "Notification area"),
            Loc.T("Die App arbeitet im Hintergrund weiter", "The app keeps working in the background"),
            null,
            [
                I("Fenster schließen", "Closing the window", "Die App läuft im Infobereich weiter, damit die Sperren aktiv bleiben.", "The app keeps running in the notification area so the locks stay active."),
                I("Rechtsklick auf das Symbol", "Right-clicking the icon", "Öffnet ein Menü, um einzelne Platten schlafen zu legen oder aufzuwecken.", "Opens a menu to spin down or wake individual drives."),
                I("Beenden", "Exiting", "Geht ebenfalls über dieses Menü. Gesperrte Platten kannst du dabei vorher aufwecken lassen.", "Also done from this menu. You can have locked drives woken up first."),
            ],
            null),

        new("",
            Loc.T("Wenn sich eine Platte nicht sperren lässt", "If a drive can't be locked"),
            Loc.T("Typische Ursachen und was hilft", "Common causes and what helps"),
            Loc.T("Hat ein Programm noch Dateien auf der Platte geöffnet, erzwingt die App nichts. Die Aktion bricht ab, alles wird zurückgesetzt und ein Dialog zeigt, welche Programme die Platte festhalten. Nach dem Schließen genügt ein Klick auf „Erneut versuchen“. Häufige Kandidaten sind:",
                  "If a program still has files open on the drive, the app doesn't force anything. The action is cancelled, everything is rolled back and a dialog shows which programs are holding the drive. After closing them, just click “Try again”. Common culprits are:"),
            [
                I(null, null, "Explorer-Fenster, die einen Ordner der Platte zeigen", "Explorer windows showing a folder on the drive"),
                I(null, null, "Medienplayer, Bildbetrachter oder geöffnete Dokumente", "Media players, image viewers or open documents"),
                I(null, null, "Virenscanner und die Windows-Suche", "Virus scanners and Windows Search"),
                I(null, null, "Backup- und Synchronisierungsprogramme", "Backup and sync tools"),
            ],
            Loc.T("Schließe sie und versuche es erneut. Für Sonderfälle führt die App beforeSleep.bat und afterSleep.bat aus dem Programmordner aus. Und noch ein Rat: Häufiges Anlaufen belastet die Mechanik, lege Platten also besser nicht im Minutentakt schlafen.",
                  "Close them and try again. For special cases the app runs beforeSleep.bat and afterSleep.bat from the program folder. One more piece of advice: frequent spin-ups stress the mechanics, so better not spin drives down every few minutes.")),

        new("",
            Loc.T("Kommandozeile", "Command line"),
            Loc.T("Steuerung per Skript oder Verknüpfung", "Control via script or shortcut"),
            null,
            [
                C("HddSpindown -s J:", "Platte J: schlafen legen. Optional mit -m OFFLINE, DEACTIVATE oder SLEEP.", "Spin down drive J:. Optionally with -m OFFLINE, DEACTIVATE or SLEEP."),
                C("HddSpindown -l J: -m OFFLINE", "Nur sperren, ohne schlafen zu legen.", "Lock only, without spinning down."),
                C("HddSpindown -w J:", "Aufwecken. Ohne Laufwerk werden alle aufgeweckt.", "Wake up. Without a drive, all drives are woken."),
                C("HddSpindown -t", "Nur im Infobereich starten.", "Start in the notification area only."),
            ],
            Loc.T("Läuft die App bereits, wird der Befehl an sie weitergegeben.", "If the app is already running, the command is passed on to it.")),

        new("",
            Loc.T("Warum warnt Windows beim Start?", "Why does Windows warn at startup?"),
            Loc.T("Kostenlos und deshalb nicht digital signiert", "Free and therefore not code-signed"),
            Loc.T("Eine digitale Signatur kostet laufend Geld. HDD-Spindown verzichtet darauf, damit die App kostenlos bleibt. Windows kennt die Datei deshalb nicht und reagiert vorsichtig. Der komplette Quellcode ist offen einsehbar.",
                  "A code signature costs money every month. HDD-Spindown does without one so the app can stay free. Windows therefore doesn't know the file and is cautious. The complete source code is openly available."),
            [
                I("„Der Computer wurde durch Windows geschützt“", "“Windows protected your PC”",
                  "Auf „Weitere Informationen“ klicken, dann auf „Trotzdem ausführen“.",
                  "Click “More info”, then “Run anyway”."),
                I("„Eine Anwendungssteuerungsrichtlinie hat diese Datei blockiert“", "“An Application Control policy has blocked this file”",
                  "Das ist Smart App Control. Es lässt nur signierte Programme zu. HDD-Spindown läuft dann nur, wenn Smart App Control unter Windows-Sicherheit, App- und Browsersteuerung ausgeschaltet ist. Einmal ausgeschaltet, lässt es sich nur durch Zurücksetzen von Windows wieder einschalten.",
                  "That's Smart App Control. It only allows signed programs. HDD-Spindown then only runs with Smart App Control turned off under Windows Security, App & browser control. Once turned off, it can only be turned back on by resetting Windows."),
                I("Abfrage der Benutzerkontensteuerung", "User Account Control prompt",
                  "Ganz normal: Die App braucht Adminrechte, um Datenträger zu steuern.",
                  "Perfectly normal: the app needs administrator rights to control drives."),
            ],
            null),
    ];

    public static HelpTopic Privacy() => new("",
        Loc.T("Deine Daten", "Your data"),
        Loc.T("Alles bleibt auf diesem Computer", "Everything stays on this computer"),
        Loc.T("HDD-Spindown arbeitet vollständig lokal.", "HDD-Spindown works entirely locally."),
        [
            I("Keine Datenübertragung", "No data transfer", "Die App baut keine Internetverbindung auf und gibt nichts an Dritte weiter.", "The app makes no internet connections and shares nothing with third parties."),
            I("Keine Telemetrie", "No telemetry", "Keine Nutzungsstatistik, keine Werbung, keine Konten.", "No usage statistics, no ads, no accounts."),
            I("Lokale Einstellungen", "Local settings", "Gespeichert werden nur Modus und „Beim Start schlafen“ je Laufwerk sowie deine Optionen, in der Datei settings.json im Einstellungsordner.", "Only the mode and “Spin down at startup” per drive plus your options are stored, in settings.json in the settings folder."),
            I("Autostart", "Autostart", "„Mit Windows starten“ legt die Aufgabe „HDD-Spindown“ in der Aufgabenplanung an. Ausschalten entfernt sie wieder.", "“Start with Windows” creates the task “HDD-Spindown” in Task Scheduler. Turning it off removes it again."),
        ],
        Loc.T("Zum vollständigen Entfernen genügt es, das Programm und den Einstellungsordner zu löschen.",
              "To remove everything, simply delete the program and the settings folder."));

    public static IReadOnlyList<LicenseEntry> Licenses() =>
    [
        new("", Loc.AppName, AppInfo.Version, "MIT", $"Copyright © 2026 {Loc.Author}", null, Mit($"Copyright (c) 2026 {Loc.Author}")),
        new("", ".NET Runtime (WPF, Windows Forms)", RuntimeInformation.FrameworkDescription.Replace(".NET ", ""), "MIT",
            "Copyright © .NET Foundation and Contributors",
            Loc.T("Laufzeitumgebung der App. Enthält alle verwendeten Bibliotheken: WPF und Windows Forms für die Oberfläche, System.Text.Json für die Einstellungen und System.Drawing für Symbole. Weitere Pakete von Drittanbietern verwendet HDD-Spindown nicht.",
                  "The app's runtime. It contains every library used: WPF and Windows Forms for the UI, System.Text.Json for settings and System.Drawing for icons. HDD-Spindown uses no other third-party packages."),
            Mit("Copyright (c) .NET Foundation and Contributors")),
        new("", "Segoe Fluent Icons", Loc.T("Bestandteil von Windows", "Part of Windows"), Loc.T("Microsoft-Schriftlizenz", "Microsoft font license"),
            "Copyright © Microsoft Corporation",
            Loc.T("Symbolschrift von Windows 11. Sie wird vom System verwendet und nicht mit der App ausgeliefert.",
                  "Windows 11 icon font. It is used from the system and not shipped with the app."),
            null),
    ];

    private static string Mit(string copyright) =>
        "MIT License\n\n" + copyright + "\n\n" +
        "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated " +
        "documentation files (the \"Software\"), to deal in the Software without restriction, including without limitation " +
        "the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to " +
        "permit persons to whom the Software is furnished to do so, subject to the following conditions:\n\n" +
        "The above copyright notice and this permission notice shall be included in all copies or substantial portions of " +
        "the Software.\n\n" +
        "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO " +
        "THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE " +
        "AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, " +
        "TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.";
}
