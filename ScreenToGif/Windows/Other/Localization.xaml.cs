﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.Win32;
using ScreenToGif.Controls;
using ScreenToGif.Util;

namespace ScreenToGif.Windows.Other
{
    public partial class Localization : Window
    {
        private IEnumerable<CultureInfo> _cultureList;

        public Localization()
        {
            InitializeComponent();
        }

        #region Events
        
        private async void Localization_OnLoaded(object sender, RoutedEventArgs e)
        {
            AddButton.IsEnabled = false;

            foreach (var resourceDictionary in Application.Current.Resources.MergedDictionaries)
            {
                var imageItem = new ImageListBoxItem
                {
                    Tag = resourceDictionary.Source?.OriginalString ?? "Settings",
                    Content = resourceDictionary.Source?.OriginalString ?? "Settings"
                };

                if (resourceDictionary.Source == null)
                {
                    imageItem.IsEnabled = false;
                    imageItem.Image = FindResource("Vector.No") as Canvas;
                    imageItem.Author = "This is a settings dictionary.";
                }
                else if (resourceDictionary.Source.OriginalString.Contains("StringResources"))
                {
                    imageItem.Image = FindResource("Vector.Translate") as Canvas;

                    #region Name

                    var subs = resourceDictionary.Source.OriginalString.Substring(resourceDictionary.Source.OriginalString.IndexOf("StringResources"));
                    var pieces = subs.Split(new[] {'.'}, StringSplitOptions.RemoveEmptyEntries);

                    if (pieces.Length == 3)
                    {
                        imageItem.Author = "Recognized as " + pieces[1];
                    }
                    else
                    {
                        imageItem.Author = "Not recognized";
                    }

                    #endregion   
                }
                else
                {
                    imageItem.IsEnabled = false;
                    imageItem.Image = FindResource("Vector.No") as Canvas;
                    imageItem.Author = "This is a style dictionary.";
                }

                ResourceListBox.Items.Add(imageItem);
            }

            ResourceListBox.SelectedIndex = ResourceListBox.Items.Count - 1;
            ResourceListBox.ScrollIntoView(ResourceListBox.SelectedItem);

            _cultureList = await GetProperCulturesAsync();

            AddButton.IsEnabled = true;

            CommandManager.InvalidateRequerySuggested();
        }

        private void MoveUp_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ResourceListBox.SelectedIndex > 0;
        }

        private void MoveDown_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ResourceListBox.SelectedIndex < ResourceListBox.Items.Count - 1;
        }

        private void Save_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ResourceListBox.SelectedIndex != -1;
        }

        private void Remove_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ResourceListBox.SelectedIndex != -1;
        }

        private void Add_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }


        private void MoveUp_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (LocalizationHelper.Move(ResourceListBox.SelectedIndex))
            {
                var selectedIndex = ResourceListBox.SelectedIndex;

                var selected = ResourceListBox.Items[selectedIndex];

                ResourceListBox.Items.RemoveAt(selectedIndex);
                ResourceListBox.Items.Insert(selectedIndex - 1, selected);
                ResourceListBox.SelectedItem = selected;
            }

            CommandManager.InvalidateRequerySuggested();
        }

        private void MoveDown_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (LocalizationHelper.Move(ResourceListBox.SelectedIndex, false))
            {
                var selectedIndex = ResourceListBox.SelectedIndex;

                var selected = ResourceListBox.Items[selectedIndex];

                ResourceListBox.Items.RemoveAt(selectedIndex);
                ResourceListBox.Items.Insert(selectedIndex + 1, selected);
                ResourceListBox.SelectedItem = selected;
            }

            CommandManager.InvalidateRequerySuggested();
        }

        private void Save_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                AddExtension = true,
                Filter = "Resource Dictionary (*.xaml)|*.xaml",
                Title = "Save Resource Dictionary"
            };

            var source = ((ImageListBoxItem)ResourceListBox.SelectedItem).Content.ToString();
            var subs = source.Substring(source.IndexOf("StringResources"));

            sfd.FileName = subs;

            var result = sfd.ShowDialog();

            if (result.HasValue && result.Value)
                LocalizationHelper.SaveSelected(ResourceListBox.SelectedIndex, sfd.FileName);

            CommandManager.InvalidateRequerySuggested();
        }

        private void Remove_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (LocalizationHelper.Remove(ResourceListBox.SelectedIndex))
                ResourceListBox.Items.RemoveAt(ResourceListBox.SelectedIndex);

            CommandManager.InvalidateRequerySuggested();
        }

        private async void Add_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                AddExtension = true,
                CheckFileExists = true,
                Title = "Open a Resource Dictionary",
                Filter = "Resource Dictionay (*.xaml)|*.xaml;"
            };

            var result = ofd.ShowDialog();

            if (!result.HasValue || !result.Value) return;

            #region Validation

            if (!ofd.FileName.Contains("StringResources"))
            {
                Dialog.Ok("Action Denied", "The name of file does not follow a valid pattern.",
                    "Try renaming like (without the []): StringResources.[Language Code].xaml");

                return;
            }

            var subs = ofd.FileName.Substring(ofd.FileName.IndexOf("StringResources"));

            if (Application.Current.Resources.MergedDictionaries.Any(x => x.Source != null && x.Source.OriginalString.Contains(subs)))
            {
                Dialog.Ok("Action Denied", "You can't add a resource with the same name.",
                    "Try renaming like: StringResources.[Language Code].xaml");

                return;
            }

            var pieces = subs.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);

            if (pieces.Length != 3)
            {
                Dialog.Ok("Action Denied", "Filename with wrong format.",
                    "Try renaming like: StringResources.[Language Code].xaml");

                return;
            }
            var cultureName = pieces[1];

            CultureInfo properCulture;
            try
            {
                properCulture = await Task.Factory.StartNew(() => CheckSupportedCulture(CultureInfo.GetCultureInfo(cultureName)));
            }
            catch (CultureNotFoundException)
            {
                Dialog.Ok("Action Denied", "Unknown Language.",
                    $"The \"{cultureName}\" and its family were not recognized as a valid language codes.");

                return;
            }
            catch (Exception ex)
            {
                Dialog.Ok("Action Denied", "Error checking culture.", ex.Message);

                return;
            }

            if (properCulture.Name != cultureName)
            {
                Dialog.Ok("Action Denied", "Redundant Language Code.", 
                    $"The \"{cultureName}\" code is redundant. Try using \'{properCulture.Name}\" instead");

                return;
            }

            #endregion

            try
            {
                await Task.Factory.StartNew(() => LocalizationHelper.ImportStringResource(ofd.FileName));
            }
            catch(Exception ex)
            {
                Dialog.Ok("Localization", "Localization - Importing Xaml Resource", ex.Message);

                GC.Collect();
                return;
            }

            var resourceDictionary = Application.Current.Resources.MergedDictionaries.LastOrDefault();

            var imageItem = new ImageListBoxItem
            {
                Tag = resourceDictionary?.Source.OriginalString ?? "Unknown",
                Content = resourceDictionary?.Source.OriginalString ?? "Unknown",
                Image = FindResource("Vector.Translate") as Canvas,
                Author = "Recognized as " + pieces[1]
            };

            ResourceListBox.Items.Add(imageItem);
            ResourceListBox.ScrollIntoView(imageItem);

            CommandManager.InvalidateRequerySuggested();
        }

        #endregion

        #region Methods 

        private CultureInfo CheckSupportedCulture(CultureInfo culture)
        {
            if (_cultureList.Contains(culture))
                return culture;

            CultureInfo t = culture;

            while (t != CultureInfo.InvariantCulture)
            {
                if (_cultureList.Contains(t))
                    return t;

                t = t.Parent;
            }

            return null;
        }

        private async Task<IEnumerable<CultureInfo>> GetProperCulturesAsync()
        {
            IEnumerable<CultureInfo> allCodes = await Task.Factory.StartNew(() => CultureInfo.GetCultures(CultureTypes.AllCultures).Where(x => !string.IsNullOrEmpty(x.Name)));

            try
            {
                IEnumerable<string> properCodes = await GetLanguageCodesAsync();
                IEnumerable<CultureInfo> properCultures = allCodes.Where(x => properCodes.Contains(x.Name));
                if (properCultures == null)
                    return allCodes;

                return properCultures;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Dialog.Ok("Translator", "Translator - Getting Language Codes", ex.Message +
                    Environment.NewLine + "Loading all local language codes."));
            }

            GC.Collect();
            return allCodes;
        }

        private async Task<IEnumerable<string>> GetLanguageCodesAsync()
        {
            var path = await GetLanguageCodesPathAsync();

            if (string.IsNullOrEmpty(path))
                throw new WebException("Can't get language codes. Path to language codes is null");

            var request = (HttpWebRequest)WebRequest.Create(path);
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/51.0.2704.79 Safari/537.36 Edge/14.14393";

            var response = (HttpWebResponse)await request.GetResponseAsync();

            using (var resultStream = response.GetResponseStream())
            {
                if (resultStream == null)
                    throw new WebException("Empty response from server when getting language codes");

                using (var reader = new StreamReader(resultStream))
                {
                    var result = await reader.ReadToEndAsync();

                    var jsonReader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(result),
                        new System.Xml.XmlDictionaryReaderQuotas());

                    var json = await Task<XElement>.Factory.StartNew(() => XElement.Load(jsonReader));
                    var languages = json.Elements();

                    return await Task.Factory.StartNew(() => languages.Where(x =>
                    x.XPathSelectElement("defs").Value != "0"
                    ).Select(x => x.XPathSelectElement("lang").Value));
                }
            }
        }

        private async Task<string> GetLanguageCodesPathAsync()
        {
            var request = (HttpWebRequest)WebRequest.Create("https://datahub.io/core/language-codes/datapackage.json");
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/51.0.2704.79 Safari/537.36 Edge/14.14393";

            var response = (HttpWebResponse)await request.GetResponseAsync();

            using (var resultStream = response.GetResponseStream())
            {
                if (resultStream == null)
                    throw new WebException("Empty response from server when getting language codes path");

                using (var reader = new StreamReader(resultStream))
                {
                    var result = await reader.ReadToEndAsync();

                    var jsonReader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(result),
                        new System.Xml.XmlDictionaryReaderQuotas());

                    var json = await Task<XElement>.Factory.StartNew(() => XElement.Load(jsonReader));

                    return await Task.Factory.StartNew(() => json.XPathSelectElement("resources").Elements().Where(x =>
                    x.XPathSelectElement("name").Value == "ietf-language-tags_json"
                    ).First().XPathSelectElement("path").Value);
                }
            }
        }

        #endregion
    }
}
