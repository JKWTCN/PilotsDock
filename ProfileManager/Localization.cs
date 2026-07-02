using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ProfileManager
{
    public static class Localization
    {
        private static Dictionary<string, string> Translation { get; set; } = [];
        private static bool IsLoaded { get; set; } = false;

        public static string Translate(string text)
        {
            EnsureLoaded();

            if (string.IsNullOrWhiteSpace(text) || Translation.Count == 0)
                return text;

            string leading = text[..(text.Length - text.TrimStart().Length)];
            string trailing = text[(text.TrimEnd().Length)..];
            string key = NormalizeKey(text);
            string suffix = "";

            if (!Translation.ContainsKey(key) && key.EndsWith(':'))
            {
                key = key[..^1].TrimEnd();
                suffix = ":";
            }

            if (Translation.TryGetValue(key, out string translated) && !string.IsNullOrWhiteSpace(translated))
                return leading + translated + suffix + trailing;

            return text;
        }

        public static string Translate(string text, params object[] args)
        {
            string template = Translate(text);
            if (args == null || args.Length == 0)
                return template;

            try
            {
                return string.Format(CultureInfo.CurrentCulture, template, args);
            }
            catch
            {
                return template;
            }
        }

        public static void Apply(DependencyObject root)
        {
            if (root == null)
                return;

            Apply(root, []);
        }

        private static void Apply(DependencyObject root, HashSet<DependencyObject> visited)
        {
            if (root == null || !visited.Add(root))
                return;

            TranslateObject(root);

            int visualCount = 0;
            try { visualCount = VisualTreeHelper.GetChildrenCount(root); } catch { }
            for (int i = 0; i < visualCount; i++)
                Apply(VisualTreeHelper.GetChild(root, i), visited);

            foreach (object child in LogicalTreeHelper.GetChildren(root))
                if (child is DependencyObject dependencyObject)
                    Apply(dependencyObject, visited);
        }

        private static void TranslateObject(DependencyObject obj)
        {
            if (obj is Window window)
                window.Title = Translate(window.Title);

            if (obj is HeaderedContentControl headered && headered.Header is string header)
                headered.Header = Translate(header);

            if (obj is TreeViewItem treeItem && treeItem.Header is string treeHeader)
                treeItem.Header = Translate(treeHeader);

            if (obj is Label label && label.Content is string labelText)
                label.Content = Translate(labelText);
            else if (obj is Button button && button.Content is string buttonText)
                button.Content = Translate(buttonText);
            else if (obj is CheckBox checkBox && checkBox.Content is string checkBoxText)
                checkBox.Content = Translate(checkBoxText);
            else if (obj is TextBlock textBlock)
                textBlock.Text = Translate(textBlock.Text);

            if (obj is FrameworkElement element && element.ToolTip is string toolTip)
                element.ToolTip = Translate(toolTip);
        }

        private static void EnsureLoaded()
        {
            if (IsLoaded)
                return;

            IsLoaded = true;
            foreach (string language in GetLanguageCandidates())
                foreach (string directory in GetSearchDirectories())
                    if (TryLoad(Path.Combine(directory, $"{language}.json")))
                        return;
        }

        private static bool TryLoad(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return false;

                JsonNode root = JsonNode.Parse(File.ReadAllText(path));
                JsonObject localization = root?["Localization"]?.AsObject();
                if (localization == null)
                    return false;

                Translation.Clear();
                foreach (var item in localization)
                    if (item.Value != null)
                        Translation[NormalizeKey(item.Key)] = item.Value.GetValue<string>();

                return Translation.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> GetLanguageCandidates()
        {
            string language = GetApplicationLanguage();
            language = string.IsNullOrWhiteSpace(language) ? CultureInfo.CurrentUICulture.Name : language;
            language = language.Replace('-', '_');

            List<string> candidates = [];
            AddCandidate(candidates, language);

            int separator = language.IndexOf('_');
            if (separator > 0)
                AddCandidate(candidates, language[..separator]);

            if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                AddCandidate(candidates, "zh_CN");

            AddCandidate(candidates, "en");
            return candidates;
        }

        private static string GetApplicationLanguage()
        {
            try
            {
                if (App.CommandLineArgs.TryGetValue("language", out string language) && !string.IsNullOrWhiteSpace(language))
                    return language;

                if (App.CommandLineArgs.TryGetValue("info", out string info) && !string.IsNullOrWhiteSpace(info))
                    return JsonNode.Parse(info)?["application"]?["language"]?.GetValue<string>();
            }
            catch { }

            return "";
        }

        private static IEnumerable<string> GetSearchDirectories()
        {
            List<string> directories = [];
            AddDirectory(directories, AppDomain.CurrentDomain.BaseDirectory);
            AddDirectory(directories, Directory.GetCurrentDirectory());
            AddDirectory(directories, Parameters.PLUGIN_PATH);
            return directories;
        }

        private static void AddCandidate(List<string> candidates, string language)
        {
            if (!string.IsNullOrWhiteSpace(language) && !candidates.Contains(language))
                candidates.Add(language);
        }

        private static void AddDirectory(List<string> directories, string directory)
        {
            if (!string.IsNullOrWhiteSpace(directory) && !directories.Contains(directory))
                directories.Add(directory);
        }

        private static string NormalizeKey(string text)
        {
            return string.Join(" ", text.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
