using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using DS4WinWPF.Translations;

namespace DS4WinWPF.DS4Forms
{
    /// <summary>
    /// Translates plain English text written directly in XAML (Text, Content, Header,
    /// ToolTip, Title, and inline Runs) when the selected language is not English.
    /// XAML keeps its English literals, so markup stays readable and tests that inspect
    /// it keep working; the English value in Translations/Strings.resx is the lookup key.
    /// Bound and styled values are left alone, and text already localized through
    /// {lex:Loc} no longer matches an English entry.
    /// </summary>
    internal static class RuntimeXamlTranslator
    {
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
        private static readonly ConditionalWeakTable<FrameworkElement, object> visited = new();

        private static Dictionary<string, List<string>> exactKeys;
        private static Dictionary<string, List<string>> looseKeys;
        private static Dictionary<string, string> englishValues;
        private static CultureInfo culture;
        private static bool registered;

        internal static void Initialize(CultureInfo selectedCulture)
        {
            culture = selectedCulture;
            if (culture.TwoLetterISOLanguageName == "en" || culture.Equals(CultureInfo.InvariantCulture))
            {
                exactKeys = null;
                looseKeys = null;
                return;
            }

            BuildLookup();
            if (!registered)
            {
                // Loaded is only broadcast to elements with instance handlers, so it would
                // skip most controls. Every element gets SizeChanged on its first layout,
                // before it is rendered, including template and late-shown content.
                EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.SizeChangedEvent,
                    new SizeChangedEventHandler(OnElementSized), true);
                registered = true;
            }
        }

        private static void BuildLookup()
        {
            exactKeys = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            looseKeys = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            englishValues = new Dictionary<string, string>(StringComparer.Ordinal);
            ResourceSet english = Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, true);
            if (english == null)
            {
                return;
            }

            foreach (DictionaryEntry entry in english.Cast<DictionaryEntry>().OrderBy(entry => (string)entry.Key, StringComparer.Ordinal))
            {
                if (entry.Value is not string value || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                string key = (string)entry.Key;
                englishValues[key] = value;
                AddKey(exactKeys, value, key);
                AddKey(looseKeys, Loose(value), key);
            }
        }

        private static void AddKey(Dictionary<string, List<string>> lookup, string text, string key)
        {
            if (!lookup.TryGetValue(text, out List<string> keys))
            {
                lookup[text] = keys = new List<string>();
            }

            keys.Add(key);
        }

        private static string Loose(string text) =>
            Whitespace.Replace(text, string.Empty).ToLowerInvariant();

        internal static string Translate(string english)
        {
            if (exactKeys == null || string.IsNullOrWhiteSpace(english))
            {
                return null;
            }

            // The same English text can live under several keys; use the first one the
            // selected language actually translates.
            string normalized = Whitespace.Replace(english, " ").Trim();
            IEnumerable<string> candidates = Candidates(exactKeys, english)
                .Concat(Candidates(exactKeys, normalized))
                .Concat(Candidates(looseKeys, Loose(english)));
            foreach (string key in candidates)
            {
                string translated = Strings.ResourceManager.GetString(key, culture);
                if (!string.IsNullOrEmpty(translated) && translated != englishValues[key])
                {
                    return translated;
                }
            }

            return null;
        }

        private static IEnumerable<string> Candidates(Dictionary<string, List<string>> lookup, string text) =>
            lookup.TryGetValue(text, out List<string> keys) ? keys : Enumerable.Empty<string>();

        private static void OnElementSized(object sender, SizeChangedEventArgs e)
        {
            if (exactKeys == null || sender is not FrameworkElement element ||
                visited.TryGetValue(element, out _))
            {
                return;
            }

            visited.Add(element, null);

            TranslateProperty(element, FrameworkElement.ToolTipProperty);
            switch (element)
            {
                case Window:
                    TranslateProperty(element, Window.TitleProperty);
                    break;
                case TextBlock textBlock:
                    TranslateTextBlock(textBlock);
                    break;
                case HeaderedContentControl:
                    TranslateProperty(element, HeaderedContentControl.HeaderProperty);
                    break;
                case HeaderedItemsControl:
                    TranslateProperty(element, HeaderedItemsControl.HeaderProperty);
                    break;
                case ListView { View: GridView gridView }:
                    foreach (GridViewColumn column in gridView.Columns)
                    {
                        TranslateProperty(column, GridViewColumn.HeaderProperty);
                    }
                    break;
            }

            if (element is ContentControl)
            {
                TranslateProperty(element, ContentControl.ContentProperty);
            }
        }

        private static void TranslateTextBlock(TextBlock textBlock)
        {
            // A set Text (literal or bound) is mirrored into an internal Run, so the
            // inlines are only examined when the text came from element content.
            if (textBlock.ReadLocalValue(TextBlock.TextProperty) != DependencyProperty.UnsetValue)
            {
                TranslateProperty(textBlock, TextBlock.TextProperty);
                return;
            }

            // Literal element content becomes Runs; translate the whole block when it is
            // plain text, otherwise each Run (e.g. inside a Hyperlink) on its own.
            if (textBlock.Inlines.All(inline => inline is Run or LineBreak) &&
                textBlock.Inlines.OfType<Run>().All(IsLocalLiteral))
            {
                string combined = string.Concat(textBlock.Inlines.Select(inline => inline is Run run ? run.Text : "\n"));
                string translated = Translate(combined);
                if (translated != null)
                {
                    textBlock.Inlines.Clear();
                    textBlock.Inlines.Add(new Run(translated));
                    return;
                }
            }

            TranslateInlines(textBlock.Inlines);
        }

        private static void TranslateInlines(InlineCollection inlines)
        {
            foreach (Inline inline in inlines.ToList())
            {
                switch (inline)
                {
                    case Run run:
                        TranslateProperty(run, Run.TextProperty);
                        break;
                    case Span span:
                        TranslateInlines(span.Inlines);
                        break;
                }
            }
        }

        private static bool IsLocalLiteral(DependencyObject target) =>
            IsLocalLiteral(target, Run.TextProperty);

        // A binding or markup extension is stored as an expression, not a string, so
        // only text that was written as a literal is ever replaced.
        private static bool IsLocalLiteral(DependencyObject target, DependencyProperty property) =>
            target.ReadLocalValue(property) is string;

        private static bool TranslateProperty(DependencyObject target, DependencyProperty property)
        {
            if (target.GetValue(property) is not string english || !IsLocalLiteral(target, property))
            {
                return false;
            }

            string translated = Translate(english);
            if (translated == null)
            {
                return false;
            }

            target.SetValue(property, translated);
            return true;
        }
    }
}
