using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.Dictionaries;
using Nikse.SubtitleEdit.Logic;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace Nikse.SubtitleEdit.Forms
{
    public sealed partial class ChangeCasingNames : Form
    {
        private Subtitle _subtitle;
        private NameList _nameList;
        private List<string> _nameListInclMulti;
        private string _language;

        public ChangeCasingNames()
        {
            UiUtil.PreInitialize(this);
            InitializeComponent();
            UiUtil.FixFonts(this);
            labelXLinesSelected.Text = string.Empty;
            Text = LanguageSettings.Current.ChangeCasingNames.Title;
            groupBoxNames.Text = string.Empty;
            listViewNames.Columns[0].Text = LanguageSettings.Current.ChangeCasingNames.Enabled;
            listViewNames.Columns[1].Text = LanguageSettings.Current.ChangeCasingNames.Name;
            groupBoxLinesFound.Text = string.Empty;
            listViewFixes.Columns[0].Text = LanguageSettings.Current.General.Apply;
            listViewFixes.Columns[1].Text = LanguageSettings.Current.General.LineNumber;
            listViewFixes.Columns[2].Text = LanguageSettings.Current.General.Before;
            listViewFixes.Columns[3].Text = LanguageSettings.Current.General.After;

            buttonSelectAll.Text = LanguageSettings.Current.FixCommonErrors.SelectAll;
            buttonInverseSelection.Text = LanguageSettings.Current.FixCommonErrors.InverseSelection;
            toolStripMenuItem1SelectAll.Text = LanguageSettings.Current.FixCommonErrors.SelectAll;
            toolStripMenuItem2InverseSelection.Text = LanguageSettings.Current.FixCommonErrors.InverseSelection;
            labelExtraNames.Text = LanguageSettings.Current.ChangeCasingNames.ExtraNames;
            buttonAddCustomNames.Text = LanguageSettings.Current.DvdSubRip.Add;
            toolStripMenuItemInverseSelection.Text = LanguageSettings.Current.Main.Menu.Edit.InverseSelection;
            toolStripMenuItemSelectAll.Text = LanguageSettings.Current.Main.Menu.ContextMenu.SelectAll;
            buttonOK.Text = LanguageSettings.Current.General.Ok;
            buttonCancel.Text = LanguageSettings.Current.General.Cancel;
            listViewFixes.Resize += delegate
            {
                var width = (listViewFixes.Width - (listViewFixes.Columns[0].Width + listViewFixes.Columns[1].Width)) / 2;
                listViewFixes.Columns[2].Width = width;
                listViewFixes.Columns[3].Width = width;
            };
            UiUtil.FixLargeFonts(this, buttonOK);
        }

        public int LinesChanged { get; private set; }

        private void ChangeCasingNames_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
            }
        }
        
        public void Initialize(Subtitle subtitle)
        {
            _subtitle = subtitle;

            _language = LanguageAutoDetect.AutoDetectGoogleLanguage(_subtitle);
            if (string.IsNullOrEmpty(_language))
            {
                _language = "en_US";
            }

            _nameList = new NameList(Configuration.DictionariesDirectory, _language, Configuration.Settings.WordLists.UseOnlineNames, Configuration.Settings.WordLists.NamesUrl);
            _nameListInclMulti = _nameList.GetAllNames(); // Will contains both one word names and multi names

            FindAllNames();
            if (_language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                foreach (ListViewItem item in listViewNames.Items)
                {
                    var name = item.SubItems[1].Text;
                    if (name == "US")
                    {
                        item.Checked = false;
                    }
                }
            }

            GeneratePreview();
        }

        private void GeneratePreview()
        {
            Cursor = Cursors.WaitCursor;
            listViewFixes.BeginUpdate();
            listViewFixes.Items.Clear();
            foreach (var p in _subtitle.Paragraphs)
            {
                string text = p.Text;
                foreach (ListViewItem item in listViewNames.Items)
                {
                    string name = item.SubItems[1].Text;

                    string textNoTags = HtmlUtil.RemoveHtmlTags(text, true);
                    if (textNoTags != textNoTags.ToUpperInvariant())
                    {
                        if (item.Checked && text != null && text.Contains(name, StringComparison.OrdinalIgnoreCase) && name.Length > 1 && name != name.ToLowerInvariant())
                        {
                            var st = new StrippableText(text);
                            st.FixCasing(new List<string> { name }, true, false, false, string.Empty);
                            text = st.MergedString;
                        }
                    }
                }
                if (text != p.Text)
                {
                    AddToPreviewListView(p, text);
                }
            }
            listViewFixes.EndUpdate();
            groupBoxLinesFound.Text = string.Format(LanguageSettings.Current.ChangeCasingNames.LinesFoundX, listViewFixes.Items.Count);
            Cursor = Cursors.Default;
        }

        private void AddToPreviewListView(Paragraph p, string newText)
        {
            var item = new ListViewItem(string.Empty) { Tag = p, Checked = true };
            item.SubItems.Add(p.Number.ToString(CultureInfo.InvariantCulture));
            item.SubItems.Add(UiUtil.GetListViewTextFromString(p.Text));
            item.SubItems.Add(UiUtil.GetListViewTextFromString(newText));
            listViewFixes.Items.Add(item);
        }

        private void AddCustomNames()
        {
            foreach (var s in textBoxExtraNames.Text.Split(','))
            {
                var name = s.Trim();
                if (name.Length > 1 && !_nameListInclMulti.Contains(name))
                {
                    _nameListInclMulti.Add(name);
                }
            }
        }

        private void FindAllNames()
        {
            var text = HtmlUtil.RemoveHtmlTags(_subtitle.GetAllTexts());

            listViewNames.BeginUpdate();
            var foundNameLookup = new HashSet<string>();

            foreach (var name in _nameListInclMulti)
            {
                // To short, number only or all lowercase
                if (name.Length == 1 || name == name.ToLowerInvariant())
                {
                    continue;
                }

                var startIndex = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);

                while (startIndex >= 0)
                {
                    var isNotEmbeddedNotFoundNotSameCasing = !IsEmbeddedWord(text, name, startIndex) &&
                                                             !foundNameLookup.Contains(name) && 
                                                             text.Substring(startIndex, name.Length) != name;
                    
                    if (isNotEmbeddedNotFoundNotSameCasing)
                    {
                        // culture specific filter
                        // todo: this type of word should be handled in names.xml blacklist
                        // if (_language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                        // {
                        //     text.Substring(startIndex).StartsWith("don't", StringComparison.InvariantCultureIgnoreCase);
                        // }

                        foundNameLookup.Add(name);

                        // add to found name listview
                        listViewNames.Items.Add(new ListViewItem(string.Empty)
                        {
                            Checked = true,
                            SubItems = { name }
                        });
                        break; // break while
                    }

                    startIndex = text.IndexOf(name, startIndex + name.Length, StringComparison.OrdinalIgnoreCase);
                }
            }

            listViewNames.EndUpdate();
            groupBoxNames.Text = string.Format(LanguageSettings.Current.ChangeCasingNames.NamesFoundInSubtitleX, listViewNames.Items.Count);
        }

        private static bool IsEmbeddedWord(string text, string name, int nameStartIndexInText)
        {
            if (nameStartIndexInText > 0 && char.IsLetterOrDigit(text[nameStartIndexInText - 1]))
            {
                return true;
            }
            var nameEndIndexInText = nameStartIndexInText + name.Length;
            return nameEndIndexInText < text.Length && char.IsLetterOrDigit(text[nameEndIndexInText]);
        }

        private void ListViewNamesSelectedIndexChanged(object sender, EventArgs e)
        {
            labelXLinesSelected.Text = string.Empty;
            if (listViewNames.SelectedItems.Count != 1)
            {
                return;
            }

            var name = listViewNames.SelectedItems[0].SubItems[1].Text;

            listViewFixes.BeginUpdate();

            foreach (ListViewItem item in listViewFixes.Items)
            {
                var text = UiUtil.GetStringFromListViewText(item.SubItems[2].Text);
                item.Selected = ShouldSelect(text, name);
            }

            listViewFixes.EndUpdate();

            if (listViewFixes.SelectedItems.Count > 0)
            {
                listViewFixes.EnsureVisible(listViewFixes.SelectedItems[0].Index);
            }
        }

        private static bool ShouldSelect(string text, string name)
        {
            var start = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            while (start >= 0)
            {
                if (!IsEmbeddedWord(text, name, start))
                {
                    return true;
                }

                start = text.IndexOf(name, start + name.Length, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private void ListViewNamesItemChecked(object sender, ItemCheckedEventArgs e)
        {
            GeneratePreview();
        }

        private void ChangeCasingNames_ResizeEnd(object sender, EventArgs e)
        {
            listViewFixes.AutoSizeLastColumn();
            listViewNames.AutoSizeLastColumn();
        }

        private void ChangeCasingNames_Shown(object sender, EventArgs e)
        {
            ChangeCasingNames_ResizeEnd(sender, e);
            listViewNames.ItemChecked += ListViewNamesItemChecked;
        }

        internal void FixCasing()
        {
            foreach (ListViewItem item in listViewFixes.Items)
            {
                if (item.Checked)
                {
                    LinesChanged++;
                    if (item.Tag is Paragraph p)
                    {
                        p.Text = UiUtil.GetStringFromListViewText(item.SubItems[3].Text);
                    }
                }
            }
        }

        private void ButtonOkClick(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
        }

        private void listViewFixes_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listViewFixes.SelectedItems.Count > 1)
            {
                labelXLinesSelected.Text = string.Format(LanguageSettings.Current.Main.XLinesSelected, listViewFixes.SelectedItems.Count);
            }
            else
            {
                labelXLinesSelected.Text = string.Empty;
            }
        }

        private void buttonSelectAll_Click(object sender, EventArgs e)
        {
            DoSelection(true);
        }

        private void buttonInverseSelection_Click(object sender, EventArgs e)
        {
            DoSelection(false);
        }

        private void DoSelection(bool selectAll)
        {
            listViewNames.ItemChecked -= ListViewNamesItemChecked;
            listViewNames.BeginUpdate();
            foreach (ListViewItem item in listViewNames.Items)
            {
                if (selectAll)
                {
                    item.Checked = true;
                }
                else
                {
                    item.Checked = !item.Checked;
                }
            }
            listViewNames.EndUpdate();
            listViewNames.ItemChecked += ListViewNamesItemChecked;
            GeneratePreview();
        }

        private void buttonAddCustomNames_Click(object sender, EventArgs e)
        {
            AddCustomNames();
            textBoxExtraNames.Text = string.Empty;
            FindAllNames();
        }

        private void toolStripMenuItemSelectAll_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in listViewFixes.Items)
            {
                item.Checked = true;
            }
        }

        private void toolStripMenuItemInverseSelection_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in listViewFixes.Items)
            {
                item.Checked = !item.Checked;
            }
        }

        private void toolStripMenuItem1SelectAll_Click(object sender, EventArgs e)
        {
            DoSelection(true);
        }

        private void toolStripMenuItem2InverseSelection_Click(object sender, EventArgs e)
        {
            DoSelection(false);
        }
    }
}
