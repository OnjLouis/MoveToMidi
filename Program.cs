using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("MoveToMidi")]
[assembly: System.Reflection.AssemblyDescription("Accessible Ableton Move and Note bundle to MIDI converter")]
[assembly: System.Reflection.AssemblyCompany("Andre Louis")]
[assembly: System.Reflection.AssemblyProduct("MoveToMidi")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) Andre Louis")]
[assembly: System.Reflection.AssemblyVersion("1.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.2.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.2")]

namespace MoveToMidi
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                Environment.ExitCode = CommandLineRunner.Run(args);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form
    {
        private const string AppName = "MoveToMidi";
        private const string Version = "1.2";
        private const string ProjectUrl = "https://github.com/OnjLouis/MoveToMidi";
        private readonly AppSettings settings = AppSettings.Load();
        private readonly ListView resultsList;
        private readonly Label statusLabel;
        private readonly List<string> resultLines = new List<string>();
        private readonly Timer updateCheckTimer;
        private bool automaticUpdateCheckStartedThisRun;

        private static readonly string HelpText =
@"MoveToMidi

Purpose
MoveToMidi converts Ableton Move and Ableton Note .ablbundle set files to standard MIDI files.

Keyboard
Ctrl+O: Open one or more .ablbundle files.
Ctrl+F: Open a folder and process every .ablbundle file in that folder.
Ctrl+Comma: Open Preferences.
Ctrl+F1: Open the project page on GitHub.
F1: Show this help.
F4: Review results.
Alt+F4: Close the program.

Updates
Help > Check for Updates checks GitHub Releases.
Help > Version History shows the latest GitHub release notes.
Help > Project on GitHub opens the project page.
Help > Donate opens onj.me/donate if you want to support development.
Preferences > Updates controls automatic checks and quiet update installs.

Output
MoveToMidi reads Song.abl directly inside each bundle. It does not extract the bundle contents.
Output is saved as MIDI type 1.
Source bundles are never overwritten. Existing output files are never overwritten; a number is added when needed.

Clip Slots
Move and Note bundles store clip slots and scenes, not a normal linear arrangement.
MoveToMidi exports slot 1, then slot 2, and so on as consecutive scenes. Empty slots are skipped.

What Gets Exported
Tempo and time signature.
Track names, using the device name when the track itself has no name.
Notes, durations, velocities, and release velocities.
Numeric clip envelope automation as MIDI CC messages when enabled in Preferences.";

        public MainForm()
        {
            Text = AppName;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 460);
            Size = new Size(860, 520);
            KeyPreview = true;
            AccessibleName = "MoveToMidi";
            AccessibleDescription = "Accessible Ableton Move and Note bundle to MIDI converter.";

            MainMenuStrip = BuildMenu();
            Controls.Add(MainMenuStrip);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12, 10, 12, 12) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            root.Controls.Add(buttons, 0, 0);

            var openFilesButton = CreateButton("&Open Bundle File(s)...", "Open bundle files", "Choose one or more Ableton .ablbundle files to convert.");
            openFilesButton.Click += delegate { OpenFiles(); };
            buttons.Controls.Add(openFilesButton);

            var openFolderButton = CreateButton("Open &Folder...", "Open folder", "Choose a folder and convert every Ableton bundle in it.");
            openFolderButton.Click += delegate { OpenFolder(); };
            buttons.Controls.Add(openFolderButton);

            var reviewButton = CreateButton("&Review Results", "Review results", "Review the selected result, or all results if none is selected.");
            reviewButton.Click += delegate { ReviewResults(); };
            buttons.Controls.Add(reviewButton);

            var helpButton = CreateButton("Help", "Help", "Open built-in MoveToMidi help.");
            helpButton.Click += delegate { ShowHelp(); };
            buttons.Controls.Add(helpButton);

            var preferencesButton = CreateButton("&Preferences...", "Preferences", "Choose output and automation defaults.");
            preferencesButton.Click += delegate { ShowPreferences(); };
            buttons.Controls.Add(preferencesButton);

            statusLabel = new Label
            {
                Text = "Choose Ableton bundle files or a folder to convert.",
                AutoSize = true,
                Margin = new Padding(0, 10, 0, 8),
                AccessibleRole = AccessibleRole.StaticText,
                AccessibleName = "Status"
            };
            root.Controls.Add(statusLabel, 0, 1);

            resultsList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = true,
                AccessibleRole = AccessibleRole.Table,
                AccessibleName = "Conversion results"
            };
            resultsList.Columns.Add("Source", 360);
            resultsList.Columns.Add("Result", 420);
            resultsList.DoubleClick += delegate { ReviewResults(); };
            root.Controls.Add(resultsList, 0, 2);

            KeyDown += MainForm_KeyDown;
            updateCheckTimer = new Timer();
            updateCheckTimer.Interval = 60 * 60 * 1000;
            updateCheckTimer.Tick += delegate { CheckAutomaticUpdateSchedule(); };
            StartAutomaticUpdateChecks();
        }

        private MenuStrip BuildMenu()
        {
            var menu = new MenuStrip { AccessibleName = "Menu bar" };
            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(new ToolStripMenuItem("&Open Bundle File(s)...", null, delegate { OpenFiles(); }, Keys.Control | Keys.O));
            file.DropDownItems.Add(new ToolStripMenuItem("Open &Folder...", null, delegate { OpenFolder(); }, Keys.Control | Keys.F));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, delegate { Close(); }));

            var options = new ToolStripMenuItem("&Options");
            options.DropDownItems.Add(new ToolStripMenuItem("&Preferences...", null, delegate { ShowPreferences(); }, Keys.Control | Keys.Oemcomma));

            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(new ToolStripMenuItem("&Check for Updates...", null, delegate { CheckForUpdates(true, true); }, Keys.Shift | Keys.F1));
            help.DropDownItems.Add(new ToolStripMenuItem("&Version History...", null, delegate { ShowVersionHistoryDialog(); }));
            help.DropDownItems.Add(new ToolStripMenuItem("&Project on GitHub", null, delegate { OpenProjectPage(); }, Keys.Control | Keys.F1));
            help.DropDownItems.Add(new ToolStripMenuItem("&Donate...", null, delegate { OpenDonatePage(); }));
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add(new ToolStripMenuItem("MoveToMidi &Help", null, delegate { ShowHelp(); }, Keys.F1));
            help.DropDownItems.Add(new ToolStripMenuItem("&About MoveToMidi", null, delegate { ShowAbout(); }));

            menu.Items.Add(file);
            menu.Items.Add(options);
            menu.Items.Add(help);
            return menu;
        }

        private static Button CreateButton(string text, string accessibleName, string description)
        {
            return new Button { Text = text, AutoSize = true, AccessibleRole = AccessibleRole.PushButton, AccessibleName = accessibleName, AccessibleDescription = description };
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F1) { ShowHelp(); e.Handled = true; }
            else if (e.KeyCode == Keys.F4 && !e.Alt) { ReviewResults(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.Oemcomma) { ShowPreferences(); e.Handled = true; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F1)) { OpenProjectPage(); return true; }
            if (keyData == (Keys.Control | Keys.Oemcomma)) { ShowPreferences(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OpenFiles()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Open Ableton bundle file or files";
                dialog.Filter = "Ableton bundles (*.ablbundle)|*.ablbundle|All files (*.*)|*.*";
                dialog.Multiselect = true;
                if (Directory.Exists(settings.LastInputFolder)) dialog.InitialDirectory = settings.LastInputFolder;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (dialog.FileNames.Length > 0) settings.LastInputFolder = Path.GetDirectoryName(dialog.FileNames[0]);
                ProcessPaths(new List<string>(dialog.FileNames));
            }
        }

        private void OpenFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose a folder containing Ableton .ablbundle files.";
                dialog.ShowNewFolderButton = false;
                if (Directory.Exists(settings.LastInputFolder)) dialog.SelectedPath = settings.LastInputFolder;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                settings.LastInputFolder = dialog.SelectedPath;
                var paths = Directory.GetFiles(dialog.SelectedPath, "*.ablbundle").OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (paths.Count == 0)
                {
                    MessageBox.Show(this, "No .ablbundle files were found in that folder.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                ProcessPaths(paths);
            }
        }

        private void ProcessPaths(List<string> paths)
        {
            var outputMode = settings.OutputMode;
            var outputFolder = CleanUserPath(settings.OutputFolder);
            var addConverted = settings.AddConvertedToFileNames;

            if (settings.AskForOutputLocationAfterInput)
            {
                using (var outputDialog = new OutputLocationForm(paths.Count, settings))
                {
                    if (outputDialog.ShowDialog(this) != DialogResult.OK)
                    {
                        statusLabel.Text = "Conversion cancelled before output location was chosen.";
                        return;
                    }
                    outputMode = outputDialog.Mode;
                    outputFolder = CleanUserPath(outputDialog.SelectedOutputFolder);
                    addConverted = outputDialog.AddConvertedToFileNames;
                    settings.OutputMode = outputMode;
                    settings.OutputFolder = outputFolder;
                    settings.AddConvertedToFileNames = addConverted;
                    SaveSettingsNonFatal();
                }
            }

            if (outputMode == OutputMode.SingleFolder && string.IsNullOrWhiteSpace(outputFolder))
            {
                MessageBox.Show(this, "Choose an output folder in Preferences, or turn on the option to ask where to save after choosing input.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                statusLabel.Text = "Conversion cancelled because no output folder is configured.";
                return;
            }

            resultsList.Items.Clear();
            resultLines.Clear();
            var successes = 0;
            var failures = 0;
            var stopwatch = Stopwatch.StartNew();

            foreach (var path in paths)
            {
                try
                {
                    var result = BundleConverter.ConvertFile(path, outputMode, outputFolder, addConverted, settings.CreateConvertOptions());
                    successes++;
                    AddResult(path, string.Format("Saved: {0}. Tracks: {1}; clips: {2}; notes: {3}; automation events: {4}.", result.OutputPath, result.TrackCount, result.ClipCount, result.NoteCount, result.AutomationEventCount), false);
                }
                catch (Exception ex)
                {
                    failures++;
                    AddResult(path, "Error: " + ex.Message, true);
                }
            }

            stopwatch.Stop();
            var summary = string.Format("Finished. {0} file(s) converted, {1} failed. Time taken: {2}.", successes, failures, FormatElapsed(stopwatch.Elapsed));
            resultLines.Insert(0, summary);
            statusLabel.Text = summary;
            string failureLogPath = null;
            MessageBox.Show(this, failures > 0 ? summary + " Review the results list for details." : summary, AppName, MessageBoxButtons.OK, failures > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            if (failures > 0)
            {
                failureLogPath = WriteFailureLogNonFatal(summary);
                if (!string.IsNullOrWhiteSpace(failureLogPath))
                {
                    statusLabel.Text = summary + " Failure log: " + failureLogPath;
                }
            }
        }

        private void AddResult(string source, string result, bool failed)
        {
            var item = new ListViewItem(source);
            item.SubItems.Add(result);
            if (failed)
            {
                item.Text = "FAILED: " + source;
                resultsList.Items.Insert(0, item);
                resultLines.Insert(0, "FAILED: " + source + Environment.NewLine + result);
            }
            else
            {
                resultsList.Items.Add(item);
                resultLines.Add(source + Environment.NewLine + result);
            }
        }

        private string WriteFailureLogNonFatal(string summary)
        {
            try
            {
                var path = Path.Combine(Application.StartupPath, "MoveToMidi failures.log");
                var lines = new List<string>();
                lines.Add("MoveToMidi conversion failures");
                lines.Add("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                lines.Add(summary);
                lines.Add(string.Empty);
                foreach (var line in resultLines)
                {
                    if (line.StartsWith("FAILED:", StringComparison.OrdinalIgnoreCase))
                    {
                        lines.Add(line);
                        lines.Add(string.Empty);
                    }
                }
                File.AppendAllText(path, string.Join(Environment.NewLine, lines.ToArray()) + Environment.NewLine);
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalSeconds < 1) return elapsed.TotalMilliseconds.ToString("0") + " ms";
            if (elapsed.TotalMinutes < 1) return elapsed.TotalSeconds.ToString("0.0") + " seconds";
            return string.Format("{0}:{1:00}.{2:0} minutes", (int)elapsed.TotalMinutes, elapsed.Seconds, elapsed.Milliseconds / 100);
        }

        private void ReviewResults()
        {
            if (resultsList.SelectedItems.Count == 1)
            {
                var item = resultsList.SelectedItems[0];
                ShowTextDialog("Review Result", item.Text + Environment.NewLine + item.SubItems[1].Text);
                return;
            }
            ShowTextDialog("Review Results", resultLines.Count == 0 ? "No results yet." : string.Join(Environment.NewLine + Environment.NewLine, resultLines.ToArray()));
        }

        private void ShowHelp() { ShowTextDialog("MoveToMidi Help", HelpText); }

        private void ShowAbout()
        {
            ShowTextDialog("About MoveToMidi", "MoveToMidi " + Version + Environment.NewLine + Environment.NewLine + "Accessible Ableton Move and Note bundle to MIDI converter." + Environment.NewLine + Environment.NewLine + "Project page:" + Environment.NewLine + ProjectUrl + Environment.NewLine + Environment.NewLine + "Created by Andre Louis with Codex.");
        }

        private void ShowPreferences()
        {
            using (var dialog = new PreferencesForm(settings))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    dialog.ApplyTo(settings);
                    SaveSettingsNonFatal();
                    StartAutomaticUpdateChecks();
                    statusLabel.Text = "Preferences saved.";
                }
            }
        }

        private void CheckForUpdates(bool showUpToDate, bool showErrors)
        {
            try
            {
                UseWaitCursor = true;
                var releases = UpdateService.FetchReleases(ProjectUrl, Version);
                var release = UpdateService.LatestVersionedRelease(releases) ?? UpdateService.FetchLatestRelease(ProjectUrl, Version);
                var latest = release == null ? string.Empty : (release.tag_name ?? string.Empty).Trim();
                System.Version current;
                System.Version remote;
                if (System.Version.TryParse(Version, out current) && System.Version.TryParse(latest.TrimStart('v', 'V'), out remote) && remote > current)
                {
                    if (settings.InstallUpdatesQuietly && TryStartUpdate(release, true)) return;
                    ShowUpdateAvailableDialog(release, latest, UpdateService.BuildReleaseNotes(releases, current, remote, Version));
                    return;
                }
                if (showUpToDate) MessageBox.Show(this, "MoveToMidi is up to date. Current version: " + Version + ".", "Check for Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (showErrors) MessageBox.Show(this, "Could not check for updates. GitHub releases may not exist yet, or the network request failed." + Environment.NewLine + Environment.NewLine + ex.Message, "Check for Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally { UseWaitCursor = false; }
        }

        private void StartAutomaticUpdateChecks()
        {
            updateCheckTimer.Stop();
            CheckAutomaticUpdateSchedule();
            if (UpdateService.AutomaticUpdateInterval(settings.UpdateCheckFrequency).HasValue) updateCheckTimer.Start();
        }

        private void CheckAutomaticUpdateSchedule()
        {
            var frequency = UpdateService.NormalizeUpdateCheckFrequency(settings.UpdateCheckFrequency);
            if (frequency == "Never") return;
            if (frequency == "Startup")
            {
                if (!automaticUpdateCheckStartedThisRun)
                {
                    automaticUpdateCheckStartedThisRun = true;
                    BeginSilentAutomaticUpdateCheck(false);
                }
                return;
            }
            var interval = UpdateService.AutomaticUpdateInterval(frequency);
            DateTime last;
            if (interval.HasValue && (!DateTime.TryParse(settings.LastAutomaticUpdateCheckUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out last) || DateTime.UtcNow - last >= interval.Value))
            {
                settings.LastAutomaticUpdateCheckUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                SaveSettingsNonFatal();
                BeginSilentAutomaticUpdateCheck(true);
            }
        }

        private void BeginSilentAutomaticUpdateCheck(bool recorded)
        {
            Task.Factory.StartNew(delegate
            {
                try
                {
                    if (!recorded) BeginInvoke((MethodInvoker)delegate { settings.LastAutomaticUpdateCheckUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture); SaveSettingsNonFatal(); });
                    var releases = UpdateService.FetchReleases(ProjectUrl, Version);
                    var release = UpdateService.LatestVersionedRelease(releases) ?? UpdateService.FetchLatestRelease(ProjectUrl, Version);
                    var latest = release == null ? string.Empty : (release.tag_name ?? string.Empty).Trim();
                    System.Version current;
                    System.Version remote;
                    if (!System.Version.TryParse(Version, out current) || !System.Version.TryParse(latest.TrimStart('v', 'V'), out remote) || remote <= current) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!IsDisposed)
                        {
                            if (settings.InstallUpdatesQuietly && TryStartUpdate(release, false)) return;
                            ShowUpdateAvailableDialog(release, latest, UpdateService.BuildReleaseNotes(releases, current, remote, Version));
                        }
                    });
                }
                catch { }
            });
        }

        private void ShowUpdateAvailableDialog(GitHubReleaseInfo release, string latest, string releaseNotes)
        {
            var releaseUrl = release == null || string.IsNullOrWhiteSpace(release.html_url) ? ProjectUrl + "/releases" : release.html_url;
            var zipAsset = UpdateService.FindPortableZipAsset(release);
            using (var dialog = new Form())
            {
                dialog.Text = "Update available";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.Width = 720;
                dialog.Height = 520;
                dialog.AccessibleName = "Update available";
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Top, Text = "MoveToMidi " + latest + " is available.", Padding = new Padding(0, 0, 0, 8) }, 0, 0);
                layout.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = releaseNotes, AccessibleName = "Release notes" }, 0, 1);
                var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 8, 0, 0) };
                if (zipAsset != null)
                {
                    var install = new Button { Text = "&Download and install", AutoSize = true, AccessibleName = "Download and install update" };
                    install.Click += delegate { dialog.DialogResult = DialogResult.OK; dialog.Close(); StartUpdate(zipAsset.browser_download_url); };
                    buttons.Controls.Add(install);
                    dialog.AcceptButton = install;
                }
                var releaseButton = new Button { Text = "Open &release page", AutoSize = true };
                releaseButton.Click += delegate { Process.Start(new ProcessStartInfo { FileName = releaseUrl, UseShellExecute = true }); };
                var later = new Button { Text = "&Later", DialogResult = DialogResult.Cancel, AutoSize = true };
                buttons.Controls.Add(releaseButton);
                buttons.Controls.Add(later);
                dialog.CancelButton = later;
                layout.Controls.Add(buttons, 0, 2);
                dialog.Controls.Add(layout);
                dialog.ShowDialog(this);
            }
        }

        private void ShowVersionHistoryDialog()
        {
            try
            {
                UseWaitCursor = true;
                var releases = UpdateService.FetchReleases(ProjectUrl, Version);
                var release = UpdateService.LatestVersionedRelease(releases) ?? UpdateService.FetchLatestRelease(ProjectUrl, Version);
                var version = release == null ? Version : (release.tag_name ?? Version).Trim().TrimStart('v', 'V');
                var notes = UpdateService.FormatReleaseNotesForDialog(release == null ? string.Empty : release.body, "No release notes were provided for this update.");
                ShowTextDialog("Version History - " + version, "Latest release: " + version + Environment.NewLine + Environment.NewLine + notes);
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not check updates. GitHub releases may not exist yet, or the network request failed." + Environment.NewLine + Environment.NewLine + ex.Message, "Version History", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            finally { UseWaitCursor = false; }
        }

        private void OpenDonatePage()
        {
            OpenExternalPage("https://onj.me/donate", "Could not open the donation page.");
        }

        private void OpenProjectPage()
        {
            OpenExternalPage(ProjectUrl, "Could not open the project page.");
        }

        private void OpenExternalPage(string url, string errorTitle)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, errorTitle + Environment.NewLine + Environment.NewLine + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private bool TryStartUpdate(GitHubReleaseInfo release, bool showErrors)
        {
            var zipAsset = UpdateService.FindPortableZipAsset(release);
            if (zipAsset == null || string.IsNullOrWhiteSpace(zipAsset.browser_download_url))
            {
                if (showErrors) MessageBox.Show(this, "This GitHub release does not include a downloadable ZIP package. Please open the release page instead.", "Check for Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            StartUpdate(zipAsset.browser_download_url);
            return true;
        }

        private void StartUpdate(string zipUrl)
        {
            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var exePath = Application.ExecutablePath;
                var updaterTempDir = UpdateService.GetUpdaterTempDirectory(appDir);
                var scriptPath = Path.Combine(updaterTempDir, "MoveToMidiUpdater-" + Guid.NewGuid().ToString("N") + ".ps1");
                File.WriteAllText(scriptPath, UpdateService.BuildUpdaterScript(zipUrl, appDir, exePath, updaterTempDir, Process.GetCurrentProcess().Id, Version));
                Process.Start(new ProcessStartInfo { FileName = "powershell.exe", Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"", UseShellExecute = false, CreateNoWindow = true });
                Close();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not start updater", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void SaveSettingsNonFatal()
        {
            try { settings.Save(); }
            catch (Exception ex) { statusLabel.Text = "Settings could not be saved. " + ex.Message; }
        }

        internal static string CleanUserPath(string path)
        {
            path = (path ?? string.Empty).Trim();
            if (path.Length >= 2 && ((path[0] == '"' && path[path.Length - 1] == '"') || (path[0] == '\'' && path[path.Length - 1] == '\'')))
            {
                path = path.Substring(1, path.Length - 2).Trim();
            }
            return path;
        }

        private void ShowTextDialog(string title, string text)
        {
            using (var dialog = new TextReviewForm(title, text)) dialog.ShowDialog(this);
        }
    }

    internal enum OutputMode { AlongsideSourceFiles, SingleFolder }

    internal sealed class AppSettings
    {
        public OutputMode OutputMode = OutputMode.AlongsideSourceFiles;
        public string OutputFolder = string.Empty;
        public string LastInputFolder = string.Empty;
        public bool AskForOutputLocationAfterInput = true;
        public bool AddConvertedToFileNames = true;
        public bool ExportAutomation = true;
        public int AutomationBaseController = 20;
        public string UpdateCheckFrequency = "Startup";
        public bool InstallUpdatesQuietly = false;
        public string LastAutomaticUpdateCheckUtc = string.Empty;

        private static string SettingsPath { get { return Path.Combine(Application.StartupPath, "MoveToMidi.ini"); } }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            if (!File.Exists(SettingsPath)) return settings;
            foreach (var rawLine in File.ReadAllLines(SettingsPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("[")) continue;
                var split = line.IndexOf('=');
                if (split <= 0) continue;
                var key = line.Substring(0, split).Trim();
                var value = line.Substring(split + 1).Trim();
                if (key.Equals("OutputMode", StringComparison.OrdinalIgnoreCase))
                {
                    OutputMode mode;
                    if (Enum.TryParse(value, true, out mode)) settings.OutputMode = mode;
                }
                else if (key.Equals("OutputFolder", StringComparison.OrdinalIgnoreCase)) settings.OutputFolder = MainForm.CleanUserPath(value);
                else if (key.Equals("LastInputFolder", StringComparison.OrdinalIgnoreCase)) settings.LastInputFolder = MainForm.CleanUserPath(value);
                else if (key.Equals("AskForOutputLocationAfterInput", StringComparison.OrdinalIgnoreCase)) settings.AskForOutputLocationAfterInput = ParseBool(value, settings.AskForOutputLocationAfterInput);
                else if (key.Equals("AddConvertedToFileNames", StringComparison.OrdinalIgnoreCase)) settings.AddConvertedToFileNames = ParseBool(value, settings.AddConvertedToFileNames);
                else if (key.Equals("ExportAutomation", StringComparison.OrdinalIgnoreCase)) settings.ExportAutomation = ParseBool(value, settings.ExportAutomation);
                else if (key.Equals("AutomationBaseController", StringComparison.OrdinalIgnoreCase))
                {
                    int cc;
                    if (int.TryParse(value, out cc) && cc >= 0 && cc <= 119) settings.AutomationBaseController = cc;
                }
                else if (key.Equals("UpdateCheckFrequency", StringComparison.OrdinalIgnoreCase)) settings.UpdateCheckFrequency = UpdateService.NormalizeUpdateCheckFrequency(value);
                else if (key.Equals("InstallUpdatesQuietly", StringComparison.OrdinalIgnoreCase)) settings.InstallUpdatesQuietly = ParseBool(value, settings.InstallUpdatesQuietly);
                else if (key.Equals("LastAutomaticUpdateCheckUtc", StringComparison.OrdinalIgnoreCase)) settings.LastAutomaticUpdateCheckUtc = value;
            }
            return settings;
        }

        private static bool ParseBool(string value, bool defaultValue)
        {
            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : defaultValue;
        }

        public ConvertOptions CreateConvertOptions()
        {
            return new ConvertOptions { ExportAutomation = ExportAutomation, AutomationBaseController = AutomationBaseController };
        }

        public void Save()
        {
            File.WriteAllLines(SettingsPath, new[]
            {
                "[Settings]",
                "OutputMode=" + OutputMode,
                "OutputFolder=" + OutputFolder,
                "LastInputFolder=" + LastInputFolder,
                "AskForOutputLocationAfterInput=" + AskForOutputLocationAfterInput,
                "AddConvertedToFileNames=" + AddConvertedToFileNames,
                "ExportAutomation=" + ExportAutomation,
                "AutomationBaseController=" + AutomationBaseController,
                "UpdateCheckFrequency=" + UpdateService.NormalizeUpdateCheckFrequency(UpdateCheckFrequency),
                "InstallUpdatesQuietly=" + InstallUpdatesQuietly,
                "LastAutomaticUpdateCheckUtc=" + LastAutomaticUpdateCheckUtc
            });
        }
    }

    internal sealed class ConvertOptions
    {
        public bool ExportAutomation = true;
        public int AutomationBaseController = 20;
    }

    internal sealed class ConvertResult
    {
        public string OutputPath;
        public int TrackCount;
        public int ClipCount;
        public int NoteCount;
        public int AutomationEventCount;
    }

    internal static class CommandLineRunner
    {
        public static int Run(string[] args)
        {
            var settings = AppSettings.Load();
            var inputs = new List<string>();
            string outputFolder = null;
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if ((arg.Equals("--output-folder", StringComparison.OrdinalIgnoreCase) || arg.Equals("-o", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
                {
                    outputFolder = args[++i];
                    settings.OutputMode = OutputMode.SingleFolder;
                }
                else if (arg.Equals("--no-automation", StringComparison.OrdinalIgnoreCase))
                {
                    settings.ExportAutomation = false;
                }
                else if (!arg.StartsWith("-", StringComparison.Ordinal))
                {
                    inputs.Add(arg);
                }
            }
            if (!string.IsNullOrWhiteSpace(outputFolder)) settings.OutputFolder = MainForm.CleanUserPath(outputFolder);
            var files = ExpandInputs(inputs);
            if (files.Count == 0) return 2;
            var failures = 0;
            var failureLines = new List<string>();
            foreach (var file in files)
            {
                try { BundleConverter.ConvertFile(file, settings.OutputMode, settings.OutputFolder, settings.AddConvertedToFileNames, settings.CreateConvertOptions()); }
                catch (Exception ex)
                {
                    failures++;
                    failureLines.Add("FAILED: " + file);
                    failureLines.Add("Error: " + ex.Message);
                    failureLines.Add(string.Empty);
                }
            }
            if (failures > 0)
            {
                WriteFailureLog(failureLines);
            }
            return failures == 0 ? 0 : 1;
        }

        private static void WriteFailureLog(List<string> failureLines)
        {
            try
            {
                var path = Path.Combine(Application.StartupPath, "MoveToMidi failures.log");
                var lines = new List<string>();
                lines.Add("MoveToMidi conversion failures");
                lines.Add("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                lines.AddRange(failureLines);
                File.AppendAllText(path, string.Join(Environment.NewLine, lines.ToArray()) + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static List<string> ExpandInputs(List<string> inputs)
        {
            var files = new List<string>();
            foreach (var input in inputs)
            {
                if (File.Exists(input) && input.EndsWith(".ablbundle", StringComparison.OrdinalIgnoreCase)) files.Add(input);
                else if (Directory.Exists(input)) files.AddRange(Directory.GetFiles(input, "*.ablbundle"));
            }
            files.Sort(StringComparer.CurrentCultureIgnoreCase);
            return files;
        }
    }

    internal sealed class OutputLocationForm : Form
    {
        private readonly RadioButton alongsideRadio;
        private readonly RadioButton singleFolderRadio;
        private readonly TextBox folderTextBox;
        private readonly Button browseButton;
        private readonly CheckBox addConvertedCheckBox;
        public OutputMode Mode { get { return singleFolderRadio.Checked ? OutputMode.SingleFolder : OutputMode.AlongsideSourceFiles; } }
        public string SelectedOutputFolder { get { return MainForm.CleanUserPath(folderTextBox.Text); } }
        public bool AddConvertedToFileNames { get { return addConvertedCheckBox.Checked; } }

        public OutputLocationForm(int fileCount, AppSettings settings)
        {
            Text = "Choose Output Location";
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimizeBox = false;
            MaximizeBox = false;
            AccessibleName = "Choose Output Location";
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Padding = new Padding(12) };
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "Choose where to save " + fileCount + " converted MIDI file(s).", AutoSize = true, MaximumSize = new Size(520, 0), AccessibleRole = AccessibleRole.StaticText });
            alongsideRadio = new RadioButton { Text = "Create &Output folders alongside the source files", AutoSize = true, Checked = settings.OutputMode != OutputMode.SingleFolder, AccessibleName = "Create Output folders alongside the source files" };
            layout.Controls.Add(alongsideRadio);
            singleFolderRadio = new RadioButton { Text = "Put all converted files in &one folder", AutoSize = true, Checked = settings.OutputMode == OutputMode.SingleFolder, AccessibleName = "Put all converted files in one folder" };
            singleFolderRadio.CheckedChanged += delegate { UpdateFolderControls(); };
            layout.Controls.Add(singleFolderRadio);
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(22, 6, 0, 6) };
            row.Controls.Add(new Label { Text = "Output folder:", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
            folderTextBox = new TextBox { Text = settings.OutputFolder, Width = 360, AccessibleName = "Output folder path" };
            row.Controls.Add(folderTextBox);
            browseButton = new Button { Text = "Browse...", AutoSize = true, AccessibleName = "Browse for output folder" };
            browseButton.Click += delegate { BrowseForFolder(); };
            row.Controls.Add(browseButton);
            layout.Controls.Add(row);
            addConvertedCheckBox = new CheckBox { Text = "Add \"converted\" to output file &names", Checked = settings.AddConvertedToFileNames, AutoSize = true, AccessibleName = "Add converted to output file names" };
            layout.Controls.Add(addConvertedCheckBox);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = cancel;
            UpdateFolderControls();
        }

        private void BrowseForFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose where converted MIDI files should be saved.";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(folderTextBox.Text)) dialog.SelectedPath = folderTextBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    folderTextBox.Text = dialog.SelectedPath;
                    singleFolderRadio.Checked = true;
                }
            }
        }

        private void UpdateFolderControls()
        {
            folderTextBox.Enabled = singleFolderRadio.Checked;
            browseButton.Enabled = singleFolderRadio.Checked;
        }
    }

    internal sealed class PreferencesForm : Form
    {
        private readonly RadioButton outputAlongsideRadio;
        private readonly RadioButton outputSingleFolderRadio;
        private readonly TextBox outputFolderTextBox;
        private readonly Button outputBrowseButton;
        private readonly CheckBox askOutputCheckBox;
        private readonly CheckBox addConvertedCheckBox;
        private readonly CheckBox exportAutomationCheckBox;
        private readonly NumericUpDown automationBaseControllerBox;
        private readonly ComboBox updateCheckFrequencyBox;
        private readonly CheckBox installUpdatesQuietlyCheckBox;

        public PreferencesForm(AppSettings settings)
        {
            Text = "Preferences";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(640, 430);
            MinimumSize = new Size(560, 360);
            AccessibleName = "Preferences";
            var tabs = new TabControl { Dock = DockStyle.Fill, AccessibleName = "Preference tabs" };
            Controls.Add(tabs);
            var outputTab = new TabPage("Output Defaults") { AccessibleName = "Output Defaults" };
            tabs.TabPages.Add(outputTab);
            var outputPanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, Padding = new Padding(12) };
            outputTab.Controls.Add(outputPanel);
            outputAlongsideRadio = new RadioButton { Text = "Create &Output folders alongside the source files", Checked = settings.OutputMode != OutputMode.SingleFolder, AutoSize = true };
            outputPanel.Controls.Add(outputAlongsideRadio);
            outputSingleFolderRadio = new RadioButton { Text = "Put all converted files in &one folder", Checked = settings.OutputMode == OutputMode.SingleFolder, AutoSize = true };
            outputSingleFolderRadio.CheckedChanged += delegate { UpdateOutputFolderControls(); };
            outputPanel.Controls.Add(outputSingleFolderRadio);
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(22, 8, 0, 8) };
            row.Controls.Add(new Label { Text = "Output folder:", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
            outputFolderTextBox = new TextBox { Text = settings.OutputFolder, Width = 360, AccessibleName = "Output folder path" };
            row.Controls.Add(outputFolderTextBox);
            outputBrowseButton = new Button { Text = "Browse...", AutoSize = true, AccessibleName = "Browse for output folder" };
            outputBrowseButton.Click += delegate { BrowseForOutputFolder(); };
            row.Controls.Add(outputBrowseButton);
            outputPanel.Controls.Add(row);
            askOutputCheckBox = CreateCheckBox("&Ask where to save after choosing input", settings.AskForOutputLocationAfterInput, "When checked, MoveToMidi asks for output choices after you choose bundles. When unchecked, saved output defaults are used immediately.");
            outputPanel.Controls.Add(askOutputCheckBox);
            addConvertedCheckBox = CreateCheckBox("Add \"converted\" to output file &names", settings.AddConvertedToFileNames, "When checked, output files include the word converted before the file extension.");
            outputPanel.Controls.Add(addConvertedCheckBox);

            var automationTab = new TabPage("Automation") { AccessibleName = "Automation" };
            tabs.TabPages.Add(automationTab);
            var automationPanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, Padding = new Padding(12) };
            automationTab.Controls.Add(automationPanel);
            exportAutomationCheckBox = CreateCheckBox("&Export numeric clip envelopes as MIDI CC", settings.ExportAutomation, "When checked, numeric clip envelope automation is exported as MIDI controller messages.");
            exportAutomationCheckBox.CheckedChanged += delegate { automationBaseControllerBox.Enabled = exportAutomationCheckBox.Checked; };
            automationPanel.Controls.Add(exportAutomationCheckBox);
            automationPanel.Controls.Add(new Label { Text = "First automation controller number:", AutoSize = true });
            automationBaseControllerBox = new NumericUpDown { Minimum = 0, Maximum = 119, Value = settings.AutomationBaseController, AccessibleName = "First automation controller number" };
            automationPanel.Controls.Add(automationBaseControllerBox);

            var updatesTab = new TabPage("Updates") { AccessibleName = "Updates" };
            tabs.TabPages.Add(updatesTab);
            var updatesPanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, Padding = new Padding(12) };
            updatesTab.Controls.Add(updatesPanel);
            updatesPanel.Controls.Add(new Label { Text = "Check GitHub Releases for updates:", AutoSize = true, AccessibleRole = AccessibleRole.StaticText });
            updateCheckFrequencyBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, AccessibleRole = AccessibleRole.ComboBox, AccessibleName = "Check GitHub Releases for updates" };
            updateCheckFrequencyBox.Items.AddRange(UpdateFrequencyLabels());
            updateCheckFrequencyBox.SelectedIndex = UpdateFrequencyIndex(settings.UpdateCheckFrequency);
            updatesPanel.Controls.Add(updateCheckFrequencyBox);
            installUpdatesQuietlyCheckBox = CreateCheckBox("Download and install updates &quietly when available", settings.InstallUpdatesQuietly, "When checked, MoveToMidi downloads and installs a release ZIP without first showing release notes.");
            updatesPanel.Controls.Add(installUpdatesQuietlyCheckBox);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(10) };
            Controls.Add(buttons);
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = cancel;
            UpdateOutputFolderControls();
            automationBaseControllerBox.Enabled = exportAutomationCheckBox.Checked;
        }

        public void ApplyTo(AppSettings settings)
        {
            settings.OutputMode = outputSingleFolderRadio.Checked ? OutputMode.SingleFolder : OutputMode.AlongsideSourceFiles;
            settings.OutputFolder = MainForm.CleanUserPath(outputFolderTextBox.Text);
            settings.AskForOutputLocationAfterInput = askOutputCheckBox.Checked;
            settings.AddConvertedToFileNames = addConvertedCheckBox.Checked;
            settings.ExportAutomation = exportAutomationCheckBox.Checked;
            settings.AutomationBaseController = (int)automationBaseControllerBox.Value;
            settings.UpdateCheckFrequency = UpdateFrequencyFromIndex(updateCheckFrequencyBox.SelectedIndex);
            settings.InstallUpdatesQuietly = installUpdatesQuietlyCheckBox.Checked;
        }

        private static CheckBox CreateCheckBox(string text, bool isChecked, string description)
        {
            return new CheckBox { Text = text, Checked = isChecked, AutoSize = true, AccessibleRole = AccessibleRole.CheckButton, AccessibleName = text.Replace("&", string.Empty), AccessibleDescription = description, Margin = new Padding(3, 3, 3, 6) };
        }

        private void BrowseForOutputFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose where converted MIDI files should be saved by default.";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(outputFolderTextBox.Text)) dialog.SelectedPath = outputFolderTextBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    outputFolderTextBox.Text = dialog.SelectedPath;
                    outputSingleFolderRadio.Checked = true;
                }
            }
        }

        private void UpdateOutputFolderControls()
        {
            outputFolderTextBox.Enabled = outputSingleFolderRadio.Checked;
            outputBrowseButton.Enabled = outputSingleFolderRadio.Checked;
        }

        private static object[] UpdateFrequencyLabels()
        {
            return new object[] { "At startup", "Every hour", "Every 6 hours", "Every 12 hours", "Daily", "Weekly", "Never" };
        }

        private static int UpdateFrequencyIndex(string value)
        {
            switch (UpdateService.NormalizeUpdateCheckFrequency(value))
            {
                case "Hourly": return 1;
                case "6Hours": return 2;
                case "12Hours": return 3;
                case "Daily": return 4;
                case "Weekly": return 5;
                case "Never": return 6;
                default: return 0;
            }
        }

        private static string UpdateFrequencyFromIndex(int index)
        {
            switch (index)
            {
                case 1: return "Hourly";
                case 2: return "6Hours";
                case 3: return "12Hours";
                case 4: return "Daily";
                case 5: return "Weekly";
                case 6: return "Never";
                default: return "Startup";
            }
        }
    }

    internal sealed class TextReviewForm : Form
    {
        public TextReviewForm(string title, string text)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(720, 520);
            MinimizeBox = false;
            AccessibleName = title;
            var textBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Dock = DockStyle.Fill, Text = NormalizeLineEndings(text), AccessibleName = title + " text" };
            Controls.Add(textBox);
            var close = new Button { Text = "Close", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 36, AccessibleName = "Close" };
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
        }

        private static string NormalizeLineEndings(string text) { return (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine); }
    }

    internal sealed class GitHubReleaseInfo
    {
        public string tag_name { get; set; }
        public string html_url { get; set; }
        public string body { get; set; }
        public List<GitHubReleaseAsset> assets { get; set; }
    }

    internal sealed class GitHubReleaseAsset
    {
        public string name { get; set; }
        public string browser_download_url { get; set; }
    }

    internal static class UpdateService
    {
        public static GitHubReleaseInfo FetchLatestRelease(string projectUrl, string version)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var client = CreateGitHubClient(version))
            {
                return new JavaScriptSerializer().Deserialize<GitHubReleaseInfo>(client.DownloadString(ApiUrl(projectUrl) + "/releases/latest"));
            }
        }

        public static List<GitHubReleaseInfo> FetchReleases(string projectUrl, string version)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var client = CreateGitHubClient(version))
            {
                return new JavaScriptSerializer().Deserialize<List<GitHubReleaseInfo>>(client.DownloadString(ApiUrl(projectUrl) + "/releases?per_page=100")) ?? new List<GitHubReleaseInfo>();
            }
        }

        public static GitHubReleaseInfo LatestVersionedRelease(IEnumerable<GitHubReleaseInfo> releases)
        {
            return (releases ?? new List<GitHubReleaseInfo>()).Select(r => new { Release = r, Version = ReleaseVersion(r) }).Where(i => i.Version != null).OrderByDescending(i => i.Version).Select(i => i.Release).FirstOrDefault();
        }

        public static GitHubReleaseAsset FindPortableZipAsset(GitHubReleaseInfo release)
        {
            if (release == null) return null;
            return (release.assets ?? new List<GitHubReleaseAsset>()).Where(a => a != null && !string.IsNullOrWhiteSpace(a.browser_download_url) && !string.IsNullOrWhiteSpace(a.name))
                .Where(a => a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.name.IndexOf("portable", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenByDescending(a => a.name.IndexOf("move", StringComparison.OrdinalIgnoreCase) >= 0)
                .FirstOrDefault();
        }

        public static string BuildReleaseNotes(IEnumerable<GitHubReleaseInfo> releases, System.Version current, System.Version latest, string currentVersion)
        {
            var newer = (releases ?? new List<GitHubReleaseInfo>()).Select(r => new { Release = r, Version = ReleaseVersion(r) }).Where(i => i.Version != null && i.Version > current && i.Version <= latest).OrderByDescending(i => i.Version).ToList();
            var builder = new StringBuilder();
            builder.AppendLine("Your version: " + currentVersion);
            builder.AppendLine("New version: " + latest);
            builder.AppendLine();
            builder.AppendLine("Changes between " + currentVersion + " and " + latest);
            builder.AppendLine();
            if (newer.Count == 0) builder.AppendLine("No release notes were provided for this update.");
            foreach (var item in newer)
            {
                builder.AppendLine(item.Release.tag_name);
                builder.AppendLine(FormatReleaseNotesForDialog(item.Release.body, "No release notes were provided for this update."));
                builder.AppendLine();
            }
            return builder.ToString();
        }

        public static string FormatReleaseNotesForDialog(string text, string emptyText)
        {
            if (string.IsNullOrWhiteSpace(text)) return emptyText;
            return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine).Trim();
        }

        public static string NormalizeUpdateCheckFrequency(string value)
        {
            if (string.Equals(value, "Hour", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Hourly", StringComparison.OrdinalIgnoreCase)) return "Hourly";
            if (string.Equals(value, "6Hours", StringComparison.OrdinalIgnoreCase)) return "6Hours";
            if (string.Equals(value, "12Hours", StringComparison.OrdinalIgnoreCase)) return "12Hours";
            if (string.Equals(value, "Daily", StringComparison.OrdinalIgnoreCase)) return "Daily";
            if (string.Equals(value, "Weekly", StringComparison.OrdinalIgnoreCase)) return "Weekly";
            if (string.Equals(value, "Never", StringComparison.OrdinalIgnoreCase)) return "Never";
            return "Startup";
        }

        public static TimeSpan? AutomaticUpdateInterval(string frequency)
        {
            switch (NormalizeUpdateCheckFrequency(frequency))
            {
                case "Hourly": return TimeSpan.FromHours(1);
                case "6Hours": return TimeSpan.FromHours(6);
                case "12Hours": return TimeSpan.FromHours(12);
                case "Daily": return TimeSpan.FromDays(1);
                case "Weekly": return TimeSpan.FromDays(7);
                default: return null;
            }
        }

        public static string GetUpdaterTempDirectory(string appDir)
        {
            var candidates = new List<string>();
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData)) candidates.Add(Path.Combine(localAppData, "Temp"));
            candidates.Add(Path.GetTempPath());
            candidates.Add(Path.Combine(appDir, "Update Temp"));
            foreach (var candidate in candidates)
            {
                try
                {
                    var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate));
                    Directory.CreateDirectory(fullPath);
                    return fullPath;
                }
                catch { }
            }
            throw new InvalidOperationException("Could not create a temporary folder for the updater.");
        }

        public static string BuildUpdaterScript(string zipUrl, string targetDir, string exePath, string tempDir, int processId, string version)
        {
            return
                "$ErrorActionPreference = 'Stop'\r\n" +
                "Add-Type -AssemblyName System.Windows.Forms\r\n" +
                "$zipUrl = " + PowerShellQuote(zipUrl) + "\r\n" +
                "$userAgent = " + PowerShellQuote("MoveToMidi " + version) + "\r\n" +
                "$target = " + PowerShellQuote(targetDir) + "\r\n" +
                "$exe = " + PowerShellQuote(exePath) + "\r\n" +
                "$tempBase = " + PowerShellQuote(tempDir) + "\r\n" +
                "$pidToWait = " + processId.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                "try {\r\n" +
                "  [System.IO.Directory]::CreateDirectory($tempBase) | Out-Null\r\n" +
                "  $root = Join-Path $tempBase ('MoveToMidiUpdate_' + [guid]::NewGuid().ToString('N'))\r\n" +
                "  $zip = Join-Path $root 'update.zip'\r\n" +
                "  $stage = Join-Path $root 'stage'\r\n" +
                "  [System.IO.Directory]::CreateDirectory($root) | Out-Null\r\n" +
                "  [System.IO.Directory]::CreateDirectory($stage) | Out-Null\r\n" +
                "  Invoke-WebRequest -Uri $zipUrl -OutFile $zip -UseBasicParsing -UserAgent $userAgent\r\n" +
                "  Expand-Archive -LiteralPath $zip -DestinationPath $stage -Force\r\n" +
                "  $source = $stage\r\n" +
                "  if (-not (Test-Path -LiteralPath (Join-Path $source 'MoveToMidi.exe'))) {\r\n" +
                "    $candidate = Get-ChildItem -LiteralPath $stage -Recurse -Filter 'MoveToMidi.exe' -File | Select-Object -First 1\r\n" +
                "    if ($candidate) { $source = $candidate.DirectoryName }\r\n" +
                "  }\r\n" +
                "  if (-not (Test-Path -LiteralPath (Join-Path $source 'MoveToMidi.exe'))) { throw 'The downloaded ZIP does not contain MoveToMidi.exe.' }\r\n" +
                "  Get-Process -Id $pidToWait -ErrorAction SilentlyContinue | Wait-Process\r\n" +
                "  Get-ChildItem -LiteralPath $source -Force | ForEach-Object {\r\n" +
                "    if ($_.name -ieq 'MoveToMidi.ini' -or $_.name -ieq 'MoveToMidi failures.log') { return }\r\n" +
                "    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $target $_.name) -Recurse -Force\r\n" +
                "  }\r\n" +
                "  Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue\r\n" +
                "  Start-Process -FilePath $exe\r\n" +
                "} catch {\r\n" +
                "  [System.Windows.Forms.MessageBox]::Show('MoveToMidi update failed:' + [Environment]::NewLine + [Environment]::NewLine + $_.Exception.Message, 'MoveToMidi updater', 'OK', 'Error') | Out-Null\r\n" +
                "}\r\n" +
                "Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue\r\n";
        }

        private static WebClient CreateGitHubClient(string version)
        {
            var client = new WebClient();
            client.Headers.Add("User-Agent", "MoveToMidi " + version);
            return client;
        }

        private static string ApiUrl(string projectUrl) { return projectUrl.Replace("https://github.com/", "https://api.github.com/repos/"); }

        private static System.Version ReleaseVersion(GitHubReleaseInfo release)
        {
            if (release == null || string.IsNullOrWhiteSpace(release.tag_name)) return null;
            System.Version version;
            return System.Version.TryParse(release.tag_name.Trim().TrimStart('v', 'V'), out version) ? version : null;
        }

        private static string PowerShellQuote(string value) { return "'" + (value ?? string.Empty).Replace("'", "''") + "'"; }
    }

    internal static class BundleConverter
    {
        private const int TicksPerQuarter = 480;

        public static ConvertResult ConvertFile(string inputPath, OutputMode mode, string selectedOutputFolder, bool addConvertedToFileName, ConvertOptions options)
        {
            var song = ReadSong(inputPath);
            var tracks = GetList(song, "tracks");
            if (tracks.Count == 0) throw new InvalidDataException("Song.abl contains no tracks.");
            var tempo = GetDouble(song, "tempo", 120.0);
            var timeSignature = GetDictionary(song, "timeSignature");
            var numerator = (int)GetDouble(timeSignature, "upper", GetDouble(timeSignature, "numerator", 4));
            var denominator = (int)GetDouble(timeSignature, "lower", GetDouble(timeSignature, "denominator", 4));
            var midiTracks = new List<byte[]>();
            var noteCount = 0;
            var clipCount = 0;
            var automationCount = 0;
            var midiSourceTrackCount = 0;

            midiTracks.Add(BuildTempoTrack(tempo, numerator, denominator));
            for (var trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
            {
                var track = tracks[trackIndex] as Dictionary<string, object>;
                if (track == null) continue;
                if (!GetString(track, "kind").Equals("midi", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                midiSourceTrackCount++;
                int notes;
                int clips;
                int automation;
                var midiTrack = BuildMidiTrack(track, trackIndex, options, out notes, out clips, out automation);
                if (notes > 0 || automation > 0)
                {
                    midiTracks.Add(midiTrack);
                    noteCount += notes;
                    clipCount += clips;
                    automationCount += automation;
                }
            }

            if (midiSourceTrackCount == 0)
            {
                throw new InvalidDataException("This set contains audio tracks only. There are no MIDI tracks to export.");
            }
            if (midiTracks.Count <= 1) throw new InvalidDataException("No MIDI notes or automation were found.");
            var outputPath = GetOutputPath(inputPath, mode, selectedOutputFolder, addConvertedToFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllBytes(outputPath, BuildMidi(midiTracks));
            return new ConvertResult { OutputPath = outputPath, TrackCount = midiTracks.Count - 1, ClipCount = clipCount, NoteCount = noteCount, AutomationEventCount = automationCount };
        }

        private static Dictionary<string, object> ReadSong(string inputPath)
        {
            using (var zip = ZipFile.OpenRead(inputPath))
            {
                var entry = zip.GetEntry("Song.abl") ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/Song.abl", StringComparison.OrdinalIgnoreCase));
                if (entry == null) throw new InvalidDataException("The bundle does not contain Song.abl.");
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                {
                    return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 200 }.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                }
            }
        }

        private static byte[] BuildTempoTrack(double tempo, int numerator, int denominator)
        {
            var events = new List<MidiEvent>();
            events.Add(new MidiEvent(0, MetaEvent(0x03, Encoding.ASCII.GetBytes("Tempo"))));
            var microseconds = Math.Max(1, (int)Math.Round(60000000.0 / Math.Max(1.0, tempo)));
            events.Add(new MidiEvent(0, new byte[] { 0xFF, 0x51, 0x03, (byte)((microseconds >> 16) & 0xFF), (byte)((microseconds >> 8) & 0xFF), (byte)(microseconds & 0xFF) }));
            var denomPower = 0;
            var denom = Math.Max(1, denominator);
            while (denom > 1) { denom >>= 1; denomPower++; }
            events.Add(new MidiEvent(0, new byte[] { 0xFF, 0x58, 0x04, (byte)Math.Max(1, numerator), (byte)denomPower, 24, 8 }));
            return BuildTrack(events);
        }

        private static byte[] BuildMidiTrack(Dictionary<string, object> track, int trackIndex, ConvertOptions options, out int noteCount, out int clipCount, out int automationCount)
        {
            noteCount = 0;
            clipCount = 0;
            automationCount = 0;
            var events = new List<MidiEvent>();
            events.Add(new MidiEvent(0, MetaEvent(0x03, Encoding.UTF8.GetBytes(GetTrackName(track, trackIndex)))));
            var clipSlots = GetList(track, "clipSlots");
            var sceneStarts = ComputeSceneStarts(clipSlots);
            var channel = (byte)(trackIndex % 16);
            for (var slotIndex = 0; slotIndex < clipSlots.Count; slotIndex++)
            {
                var slot = clipSlots[slotIndex] as Dictionary<string, object>;
                var clip = slot == null ? null : GetDictionary(slot, "clip");
                if (clip == null) continue;
                var sceneStart = sceneStarts.ContainsKey(slotIndex) ? sceneStarts[slotIndex] : 0.0;
                var notes = GetList(clip, "notes");
                if (notes.Count > 0) clipCount++;
                foreach (var noteObj in notes)
                {
                    var note = noteObj as Dictionary<string, object>;
                    if (note == null) continue;
                    var noteNumber = ClampToByte(GetDouble(note, "noteNumber", 60));
                    var startTick = BeatToTick(sceneStart + GetDouble(note, "startTime", 0.0));
                    var endTick = BeatToTick(sceneStart + GetDouble(note, "startTime", 0.0) + Math.Max(0.01, GetDouble(note, "duration", 0.25)));
                    var velocity = ClampToByte(GetDouble(note, "velocity", 100.0));
                    var offVelocity = ClampToByte(GetDouble(note, "offVelocity", 0.0));
                    events.Add(new MidiEvent(startTick, new byte[] { (byte)(0x90 | channel), noteNumber, velocity }));
                    events.Add(new MidiEvent(Math.Max(startTick + 1, endTick), new byte[] { (byte)(0x80 | channel), noteNumber, offVelocity }));
                    noteCount++;
                }
                if (options.ExportAutomation)
                {
                    foreach (var envelopeObj in GetList(clip, "envelopes"))
                    {
                        var envelope = envelopeObj as Dictionary<string, object>;
                        if (envelope == null) continue;
                        var parameterId = (int)GetDouble(envelope, "parameterId", 0);
                        var controller = options.AutomationBaseController + parameterId;
                        if (controller < 0 || controller > 119) continue;
                        foreach (var pointObj in GetList(envelope, "breakpoints"))
                        {
                            var point = pointObj as Dictionary<string, object>;
                            if (point == null) continue;
                            double value;
                            if (!TryGetDouble(point, "value", out value)) continue;
                            var tick = BeatToTick(sceneStart + GetDouble(point, "time", 0.0));
                            events.Add(new MidiEvent(tick, new byte[] { (byte)(0xB0 | channel), (byte)controller, ClampToByte(value <= 1.0 ? value * 127.0 : value) }));
                            automationCount++;
                        }
                    }
                }
            }
            return BuildTrack(events);
        }

        private static Dictionary<int, double> ComputeSceneStarts(List<object> clipSlots)
        {
            var starts = new Dictionary<int, double>();
            var current = 0.0;
            for (var i = 0; i < clipSlots.Count; i++)
            {
                var slot = clipSlots[i] as Dictionary<string, object>;
                var clip = slot == null ? null : GetDictionary(slot, "clip");
                if (clip == null) continue;
                starts[i] = current;
                var region = GetDictionary(clip, "region");
                var length = Math.Max(0.0, GetDouble(region, "end", 0.0) - GetDouble(region, "start", 0.0));
                if (length <= 0.0) length = MaxNoteEnd(clip);
                current += Math.Max(1.0, length);
            }
            return starts;
        }

        private static double MaxNoteEnd(Dictionary<string, object> clip)
        {
            var max = 0.0;
            foreach (var noteObj in GetList(clip, "notes"))
            {
                var note = noteObj as Dictionary<string, object>;
                if (note != null) max = Math.Max(max, GetDouble(note, "startTime", 0.0) + GetDouble(note, "duration", 0.0));
            }
            return max;
        }

        private static string GetTrackName(Dictionary<string, object> track, int trackIndex)
        {
            var name = GetString(track, "name");
            if (!string.IsNullOrWhiteSpace(name)) return name;
            foreach (var deviceObj in GetList(track, "devices"))
            {
                var device = deviceObj as Dictionary<string, object>;
                if (device == null) continue;
                name = GetString(device, "name");
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            return "Track " + (trackIndex + 1);
        }

        private static byte[] BuildTrack(List<MidiEvent> events)
        {
            events.Sort(delegate(MidiEvent a, MidiEvent b)
            {
                var tickCompare = a.Tick.CompareTo(b.Tick);
                return tickCompare != 0 ? tickCompare : a.Data[0].CompareTo(b.Data[0]);
            });
            var output = new List<byte>();
            var lastTick = 0;
            foreach (var midiEvent in events)
            {
                WriteVariableLength(output, Math.Max(0, midiEvent.Tick - lastTick));
                output.AddRange(midiEvent.Data);
                lastTick = midiEvent.Tick;
            }
            WriteVariableLength(output, 0);
            output.Add(0xFF);
            output.Add(0x2F);
            output.Add(0x00);
            return output.ToArray();
        }

        private static byte[] BuildMidi(List<byte[]> tracks)
        {
            using (var stream = new MemoryStream())
            {
                WriteAscii(stream, "MThd");
                WriteUInt32(stream, 6);
                WriteUInt16(stream, 1);
                WriteUInt16(stream, tracks.Count);
                WriteUInt16(stream, TicksPerQuarter);
                foreach (var track in tracks)
                {
                    WriteAscii(stream, "MTrk");
                    WriteUInt32(stream, track.Length);
                    stream.Write(track, 0, track.Length);
                }
                return stream.ToArray();
            }
        }

        private static byte[] MetaEvent(byte type, byte[] data)
        {
            var result = new List<byte>();
            result.Add(0xFF);
            result.Add(type);
            WriteVariableLength(result, data.Length);
            result.AddRange(data);
            return result.ToArray();
        }

        private static string GetOutputPath(string inputPath, OutputMode mode, string selectedOutputFolder, bool addConvertedToFileName)
        {
            var inputDir = Path.GetDirectoryName(inputPath);
            var outputDir = mode == OutputMode.SingleFolder ? selectedOutputFolder : Path.Combine(inputDir, "Output");
            var stem = Path.GetFileNameWithoutExtension(inputPath);
            var baseName = addConvertedToFileName ? stem + " converted" : stem;
            var candidate = Path.Combine(outputDir, baseName + ".mid");
            var counter = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(outputDir, baseName + " " + counter + ".mid");
                counter++;
            }
            return candidate;
        }

        private static int BeatToTick(double beat) { return Math.Max(0, (int)Math.Round(beat * TicksPerQuarter)); }
        private static byte ClampToByte(double value) { return (byte)Math.Max(0, Math.Min(127, (int)Math.Round(value))); }

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> dictionary, string key)
        {
            object value;
            return dictionary != null && dictionary.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static List<object> GetList(Dictionary<string, object> dictionary, string key)
        {
            object value;
            if (dictionary == null || !dictionary.TryGetValue(key, out value) || value == null) return new List<object>();
            var list = value as object[];
            if (list != null) return list.ToList();
            var arrayList = value as System.Collections.ArrayList;
            if (arrayList != null) return arrayList.Cast<object>().ToList();
            return new List<object>();
        }

        private static string GetString(Dictionary<string, object> dictionary, string key)
        {
            object value;
            return dictionary != null && dictionary.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : string.Empty;
        }

        private static double GetDouble(Dictionary<string, object> dictionary, string key, double defaultValue)
        {
            double value;
            return TryGetDouble(dictionary, key, out value) ? value : defaultValue;
        }

        private static bool TryGetDouble(Dictionary<string, object> dictionary, string key, out double value)
        {
            value = 0.0;
            object raw;
            if (dictionary == null || !dictionary.TryGetValue(key, out raw) || raw == null) return false;
            if (raw is int) { value = (int)raw; return true; }
            if (raw is long) { value = (long)raw; return true; }
            if (raw is double) { value = (double)raw; return true; }
            if (raw is decimal) { value = (double)(decimal)raw; return true; }
            return double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static void WriteVariableLength(List<byte> output, int value)
        {
            var buffer = value & 0x7F;
            value >>= 7;
            while (value > 0)
            {
                buffer <<= 8;
                buffer |= (value & 0x7F) | 0x80;
                value >>= 7;
            }
            while (true)
            {
                output.Add((byte)(buffer & 0xFF));
                if ((buffer & 0x80) != 0) buffer >>= 8;
                else break;
            }
        }

        private static void WriteUInt16(Stream stream, int value)
        {
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)(value & 0xFF));
        }

        private static void WriteUInt32(Stream stream, int value)
        {
            stream.WriteByte((byte)((value >> 24) & 0xFF));
            stream.WriteByte((byte)((value >> 16) & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)(value & 0xFF));
        }

        private static void WriteAscii(Stream stream, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        private sealed class MidiEvent
        {
            public readonly int Tick;
            public readonly byte[] Data;
            public MidiEvent(int tick, byte[] data) { Tick = tick; Data = data; }
        }
    }
}

