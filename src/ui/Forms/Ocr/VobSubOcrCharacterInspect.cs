﻿using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Ocr.Binary;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using MessageBox = Nikse.SubtitleEdit.Forms.SeMsgBox.MessageBox;

namespace Nikse.SubtitleEdit.Forms.Ocr
{
    public sealed partial class VobSubOcrCharacterInspect : Form
    {
        public XmlDocument ImageCompareDocument { get; private set; }
        public bool DeleteMultiMatch { get; private set; }
        public int LastIndex { get; private set; }
        private List<VobSubOcr.CompareMatch> _matches;
        private List<Bitmap> _imageSources;
        private List<ImageSplitterItem> _splitterItems;
        private string _directoryPath;
        private XmlNode _selectedCompareNode;
        private BinaryOcrBitmap _selectedCompareBinaryOcrBitmap;
        private VobSubOcr.CompareMatch _selectedMatch;
        private BinaryOcrDb _binOcrDb;

        public VobSubOcrCharacterInspect()
        {
            UiUtil.PreInitialize(this);
            InitializeComponent();
            VobSubEditCharacters.MakeToolStripLetters(contextMenuStripTextbox, InsertLanguageCharacter);
            UiUtil.FixFonts(this);

            LanguageStructure.VobSubEditCharacters langCharEdit = LanguageSettings.Current.VobSubEditCharacters;
            LanguageStructure.VobSubOcrCharacterInspect langCharInspect = LanguageSettings.Current.VobSubOcrCharacterInspect;

            labelCount.Text = string.Empty;
            labelExpandCount.Text = string.Empty;
            labelImageSize.Text = string.Empty;
            Text = langCharInspect.Title;
            groupBoxInspectItems.Text = langCharInspect.InspectItems;
            labelImageInfo.Text = string.Empty;
            groupBoxCurrentCompareImage.Text = langCharEdit.CurrentCompareImage;
            labelTextAssociatedWithImage.Text = langCharEdit.TextAssociatedWithImage;
            checkBoxItalic.Text = langCharEdit.IsItalic;
            buttonUpdate.Text = langCharEdit.Update;
            buttonDelete.Text = langCharEdit.Delete;
            buttonAddBetterMatch.Text = langCharInspect.AddBetterMatch;
            labelDoubleSize.Text = langCharEdit.ImageDoubleSize;
            buttonCancel.Text = LanguageSettings.Current.General.Cancel;
            buttonOK.Text = LanguageSettings.Current.General.Ok;
            UiUtil.FixLargeFonts(this, buttonOK);

            addBetterMultiMatchToolStripMenuItem.Text = langCharInspect.AddBetterMultiMatch;
            addOrUpdateMatchToolStripMenuItem.Text = langCharInspect.AddOrUpdateMatch;
            selectThePreviousMatchToolStripMenuItem.Text = langCharInspect.SelectPrevousMatch;
            selectTheNextMatchToolStripMenuItem.Text = langCharInspect.SelectNextMatch;
            jumpToPreviousMissingMatchToolStripMenuItem1.Text = langCharInspect.JumpPreviousMissingMatch;
            jumpToPreviousMissingMatchToolStripMenuItem.Text = langCharInspect.JumpPreviousMissingMatch;
            jumpToNextMissingMatchToolStripMenuItem1.Text = langCharInspect.JumpNextMissingMatch;
            jumpToNextMissingMatchToolStripMenuItem.Text = langCharInspect.JumpNextMissingMatch;
            focusTheTextInputToolStripMenuItem.Text = langCharInspect.FocusTextbox;

            addOrUpdateMatchToolStripMenuItem.ShortcutKeyDisplayString = Keys.Enter.ToString();
            selectThePreviousMatchToolStripMenuItem.ShortcutKeyDisplayString = Keys.Up.ToString();
            selectTheNextMatchToolStripMenuItem.ShortcutKeyDisplayString = Keys.Down.ToString();
            focusTheTextInputToolStripMenuItem.ShortcutKeyDisplayString = Keys.Enter.ToString();

            buttonDetectFont.Visible = Configuration.Settings.General.ShowBetaStuff;

            ToolStripItem[] extraTextboxItems = new ToolStripItem[contextMenuStripTextbox2.Items.Count];
            contextMenuStripTextbox2.Items.CopyTo(extraTextboxItems, 0);
            foreach (ToolStripItem item in extraTextboxItems)
            {
                contextMenuStripTextbox.Items.Add(item);
            }
        }

        internal void Initialize(string databaseFolderName, List<VobSubOcr.CompareMatch> matches, List<Bitmap> imageSources, BinaryOcrDb binOcrDb, List<ImageSplitterItem> splitterItems)
        {
            _binOcrDb = binOcrDb;
            _matches = matches;
            _imageSources = imageSources;
            _splitterItems = splitterItems;
            DeleteMultiMatch = false;

            listBoxInspectItems.Items.Clear();
            if (_binOcrDb == null)
            {
                ImageCompareDocument = new XmlDocument();
                _directoryPath = Configuration.VobSubCompareDirectory + databaseFolderName + Path.DirectorySeparatorChar;
                if (!File.Exists(_directoryPath + "Images.xml"))
                {
                    ImageCompareDocument.LoadXml("<OcrBitmaps></OcrBitmaps>");
                }
                else
                {
                    ImageCompareDocument.Load(_directoryPath + "Images.xml");
                }
            }

            SyncListBoxToMatches();

            if (LastIndex > listBoxInspectItems.Items.Count)
            {
                LastIndex = listBoxInspectItems.Items.Count - 1;
            }

            if (listBoxInspectItems.Items.Count > 0)
            {
                listBoxInspectItems.SelectedIndex = LastIndex;
            }

            ShowCount();
            listBoxInspectItems.Select();
        }

        private void listBoxInspectItems_SelectedIndexChanged(object sender, EventArgs e)
        {
            labelImageInfo.Text = string.Empty;
            labelExpandCount.Text = string.Empty;
            labelImageSize.Text = string.Empty;

            if (listBoxInspectItems.SelectedIndex < 0)
            {
                return;
            }

            _selectedCompareNode = null;
            _selectedCompareBinaryOcrBitmap = null;

            var img = _imageSources[listBoxInspectItems.SelectedIndex];
            pictureBoxInspectItem.Image = img;
            if (img != null)
            {
                pictureBoxInspectItem.Width = img.Width + 2;
                pictureBoxInspectItem.Height = img.Height + 2;
            }

            pictureBoxCompareBitmap.Image = null;
            pictureBoxCompareBitmapDouble.Image = null;

            int index = listBoxInspectItems.SelectedIndex;
            var match = _matches[index];
            _selectedMatch = match;
            if (!string.IsNullOrEmpty(match.Name))
            {
                if (_binOcrDb != null)
                {
                    bool bobFound = false;
                    foreach (BinaryOcrBitmap bob in _binOcrDb.CompareImages)
                    {
                        if (match.Name == bob.Key)
                        {
                            textBoxText.Text = bob.Text;
                            checkBoxItalic.Checked = bob.Italic;
                            _selectedCompareBinaryOcrBitmap = bob;
                            SetDbItemView(bob.ToOldBitmap());
                            var matchBob = new BinaryOcrBitmap(new NikseBitmap(_imageSources[listBoxInspectItems.SelectedIndex]));
                            if (matchBob.Hash == bob.Hash && matchBob.Width == bob.Width && matchBob.Height == bob.Height && matchBob.NumberOfColoredPixels == bob.NumberOfColoredPixels)
                            {
                                buttonAddBetterMatch.Enabled = false; // exact match
                            }
                            else
                            {
                                buttonAddBetterMatch.Enabled = true;
                            }
                            bobFound = true;
                            break;
                        }
                    }
                    if (!bobFound)
                    {
                        foreach (BinaryOcrBitmap bob in _binOcrDb.CompareImagesExpanded)
                        {
                            if (match.Name == bob.Key)
                            {
                                textBoxText.Text = bob.Text;
                                checkBoxItalic.Checked = bob.Italic;
                                _selectedCompareBinaryOcrBitmap = bob;

                                var oldBitmap = bob.ToOldBitmap();
                                SetDbItemView(oldBitmap);

                                int dif = 1;
                                if (oldBitmap.Width == match.ImageSplitterItem.NikseBitmap.Width && oldBitmap.Height == match.ImageSplitterItem.NikseBitmap.Height)
                                {
                                    dif = NikseBitmapImageSplitter.IsBitmapsAlike(match.ImageSplitterItem.NikseBitmap, oldBitmap);
                                }
                                buttonAddBetterMatch.Enabled = dif > 0; // if exact match then don't allow "Add better match"
                                labelExpandCount.Text = $"Expand count: {bob.ExpandCount}";
                                break;
                            }
                        }
                    }
                }
                else
                {
                    foreach (XmlNode node in ImageCompareDocument.DocumentElement.ChildNodes)
                    {
                        if (node.Attributes["Text"] != null && node.InnerText == match.Name)
                        {
                            string text = node.Attributes["Text"].InnerText;
                            textBoxText.Text = text;
                            checkBoxItalic.Checked = node.Attributes["Italic"] != null;
                            string databaseName = Path.Combine(_directoryPath, "Images.db");
                            using (var f = new FileStream(databaseName, FileMode.Open))
                            {
                                try
                                {
                                    string name = node.InnerText;
                                    int pos = Convert.ToInt32(name);
                                    f.Position = pos;
                                    var mbmp = new ManagedBitmap(f);
                                    var bitmap = mbmp.ToOldBitmap();
                                    SetDbItemView(mbmp.ToOldBitmap());
                                    labelImageInfo.Text = string.Format(LanguageSettings.Current.VobSubEditCharacters.Image + " - {0}x{1}", bitmap.Width, bitmap.Height);
                                }
                                catch (Exception exception)
                                {
                                    labelImageInfo.Text = LanguageSettings.Current.VobSubEditCharacters.Image;
                                    MessageBox.Show(exception.Message);
                                }
                            }

                            _selectedCompareNode = node;
                            break;
                        }
                    }
                }
            }

            buttonAddBetterMatch.Text = LanguageSettings.Current.VobSubOcrCharacterInspect.AddBetterMatch;
            if (_selectedMatch.Text == LanguageSettings.Current.VobSubOcr.NoMatch)
            {
                buttonUpdate.Enabled = false;
                buttonDelete.Enabled = false;
                buttonAddBetterMatch.Enabled = true;
                buttonAddBetterMatch.Text = LanguageSettings.Current.VobSubOcrCharacterInspect.Add;
                textBoxText.Enabled = true;
                textBoxText.Text = string.Empty;
                checkBoxItalic.Enabled = true;
                pictureBoxCompareBitmap.Visible = true;
                pictureBoxCompareBitmapDouble.Visible = true;
                labelDoubleSize.Visible = true;
            }
            else if (_selectedCompareNode == null && _selectedCompareBinaryOcrBitmap == null)
            {
                buttonUpdate.Enabled = false;
                buttonDelete.Enabled = false;
                buttonAddBetterMatch.Enabled = true;
                textBoxText.Enabled = true;
                textBoxText.Text = string.Empty;
                checkBoxItalic.Enabled = false;
                pictureBoxCompareBitmap.Visible = false;
                pictureBoxCompareBitmapDouble.Visible = false;
                labelDoubleSize.Visible = false;
                if (img == null)
                {
                    buttonAddBetterMatch.Enabled = false;
                }
            }
            else
            {
                buttonUpdate.Enabled = true;
                buttonDelete.Enabled = true;
                if (_selectedCompareNode != null)
                {
                    buttonAddBetterMatch.Enabled = true;
                }

                textBoxText.Enabled = true;
                checkBoxItalic.Enabled = true;
                pictureBoxCompareBitmap.Visible = true;
                pictureBoxCompareBitmapDouble.Visible = true;
                labelDoubleSize.Visible = true;
            }
        }

        private void SetDbItemView(Bitmap bitmap)
        {
            pictureBoxCompareBitmap.Image = bitmap;
            pictureBoxCompareBitmap.Width = bitmap.Width;
            pictureBoxCompareBitmap.Height = bitmap.Height;
            labelImageSize.Top = pictureBoxCompareBitmap.Top + bitmap.Height + 17;
            labelImageSize.Text = bitmap.Width + "x" + bitmap.Height;
            pictureBoxCompareBitmapDouble.Width = bitmap.Width * 2;
            pictureBoxCompareBitmapDouble.Height = bitmap.Height * 2;
            pictureBoxCompareBitmapDouble.Image = bitmap;
        }

        private void buttonUpdate_Click(object sender, EventArgs e) => UpdateMatch();

        private void UpdateMatch()
        {
            if (_selectedCompareNode == null && _selectedCompareBinaryOcrBitmap == null)
            {
                return;
            }

            string newText = textBoxText.Text;

            if (_selectedCompareBinaryOcrBitmap != null)
            {
                foreach (var match in _matches)
                {
                    if (match.Name == _selectedCompareBinaryOcrBitmap.Key)
                    {
                        _selectedCompareBinaryOcrBitmap.Text = newText;
                        _selectedCompareBinaryOcrBitmap.Italic = checkBoxItalic.Checked;
                        match.Text = newText;
                        match.Italic = checkBoxItalic.Checked;
                        match.Name = _selectedCompareBinaryOcrBitmap.Key;
                        break;
                    }
                }

                _selectedCompareBinaryOcrBitmap.Text = newText;
                _selectedCompareBinaryOcrBitmap.Italic = checkBoxItalic.Checked;
                listBoxInspectItems.SelectedIndexChanged -= listBoxInspectItems_SelectedIndexChanged;
                if (checkBoxItalic.Checked)
                {
                    listBoxInspectItems.Items[listBoxInspectItems.SelectedIndex] = newText + " (italic)";
                }
                else
                {
                    listBoxInspectItems.Items[listBoxInspectItems.SelectedIndex] = newText;
                }

                listBoxInspectItems.SelectedIndexChanged += listBoxInspectItems_SelectedIndexChanged;
            }
            else
            {
                XmlNode node = _selectedCompareNode;
                listBoxInspectItems.SelectedIndexChanged -= listBoxInspectItems_SelectedIndexChanged;
                listBoxInspectItems.Items[listBoxInspectItems.SelectedIndex] = newText;
                listBoxInspectItems.SelectedIndexChanged += listBoxInspectItems_SelectedIndexChanged;
                node.Attributes["Text"].InnerText = newText;
                SetItalic(node);
            }

            listBoxInspectItems_SelectedIndexChanged(null, null);
        }

        private void SetItalic(XmlNode node)
        {
            if (node?.Attributes == null || node.OwnerDocument == null)
            {
                return;
            }

            if (checkBoxItalic.Checked)
            {
                if (node.Attributes["Italic"] == null)
                {
                    var italic = node.OwnerDocument.CreateAttribute("Italic");
                    italic.InnerText = "true";
                    node.Attributes.Append(italic);
                }
            }
            else
            {
                if (node.Attributes["Italic"] != null)
                {
                    node.Attributes.RemoveNamedItem("Italic");
                }
            }
        }

        private void buttonDelete_Click(object sender, EventArgs e)
        {
            if (_selectedCompareNode == null && _selectedCompareBinaryOcrBitmap == null)
            {
                return;
            }

            listBoxInspectItems.Items[listBoxInspectItems.SelectedIndex] = LanguageSettings.Current.VobSubOcr.NoMatch;
            if (_selectedCompareBinaryOcrBitmap != null)
            {
                if (_selectedCompareBinaryOcrBitmap.ExpandCount > 0)
                {
                    _binOcrDb.CompareImagesExpanded.Remove(_selectedCompareBinaryOcrBitmap);
                }
                else
                {
                    _binOcrDb.CompareImages.Remove(_selectedCompareBinaryOcrBitmap);
                }

                DeleteMultiMatch = _selectedCompareBinaryOcrBitmap.ExpandCount > 0;
                _selectedCompareBinaryOcrBitmap = null;
                _binOcrDb.Save();
                if (DeleteMultiMatch)
                {
                    LastIndex = listBoxInspectItems.SelectedIndex;
                    DialogResult = DialogResult.OK;
                    return;
                }
            }
            else
            {
                ImageCompareDocument.DocumentElement.RemoveChild(_selectedCompareNode);
                _selectedCompareNode = null;
            }
            listBoxInspectItems_SelectedIndexChanged(null, null);
        }

        private void buttonAddBetterMatch_Click(object sender, EventArgs e) => AddBetterMatch();

        private void AddBetterMatch()
        {
            if (listBoxInspectItems.SelectedIndex < 0)
            {
                return;
            }

            if (listBoxInspectItems.Items[listBoxInspectItems.SelectedIndex].ToString() == textBoxText.Text + (checkBoxItalic.Checked ? " (italic)" : string.Empty))
            {
                // text (or italic) not changed
                textBoxText.SelectAll();
                textBoxText.Focus();
                return;
            }

            if (_selectedCompareBinaryOcrBitmap != null || (_binOcrDb != null && (_selectedMatch.Text == LanguageSettings.Current.VobSubOcr.NoMatch || _selectedMatch.NOcrCharacter == null)))
            {
                BinaryOcrBitmap bob;
                int expandCount = 0;
                if (_selectedCompareBinaryOcrBitmap != null && _selectedCompareBinaryOcrBitmap.ExpandCount > 0 && _selectedMatch.Extra != null && _selectedMatch.Extra.Count > 1)
                {
                    expandCount = _selectedMatch.Extra.Count;
                    var first = _selectedMatch.Extra[0];
                    bob = new BinaryOcrBitmap(first.NikseBitmap, checkBoxItalic.Checked, _selectedMatch.Extra.Count, textBoxText.Text, first.X, first.Top) { ExpandedList = new List<BinaryOcrBitmap>() };
                    for (int i = 1; i < _selectedMatch.Extra.Count; i++)
                    {
                        var element = _selectedMatch.Extra[i];
                        bob.ExpandedList.Add(new BinaryOcrBitmap(element.NikseBitmap, checkBoxItalic.Checked, 0, null, element.X, element.Top));
                    }
                }
                else
                {
                    int x = 0;
                    int y = 0;
                    var nbmp = new NikseBitmap(pictureBoxInspectItem.Image as Bitmap);
                    if (_selectedMatch?.ImageSplitterItem != null)
                    {
                        if (_selectedMatch.ImageSplitterItem != null)
                        {
                            x = _selectedMatch.ImageSplitterItem.X;
                            y = _selectedMatch.ImageSplitterItem.Top;
                        }
                        else
                        {
                            x = _selectedMatch.X;
                            y = _selectedMatch.Y;
                        }
                    }
                    bob = new BinaryOcrBitmap(nbmp, checkBoxItalic.Checked, 0, textBoxText.Text, x, y);
                }

                _binOcrDb.Add(bob);

                int index = listBoxInspectItems.SelectedIndex;
                _matches[index].Name = bob.Key;
                _matches[index].ExpandCount = expandCount;
                _matches[index].Italic = checkBoxItalic.Checked;
                _matches[index].Text = textBoxText.Text;
                SyncListBoxToMatches();

                listBoxInspectItems_SelectedIndexChanged(null, null);
                ShowCount();

                // update other letters that are exact matches
                for (int i = 0; i < _matches.Count; i++)
                {
                    if (i != index && i < _imageSources.Count && _matches[i].ExpandCount == 0)
                    {
                        var newMatch = _binOcrDb.FindExactMatch(new BinaryOcrBitmap(new NikseBitmap(_imageSources[i])));
                        if (newMatch >= 0 && _binOcrDb.CompareImages[newMatch].Hash == bob.Hash)
                        {
                            _matches[i].Name = bob.Key;
                            _matches[i].ExpandCount = 0;
                            _matches[i].Italic = checkBoxItalic.Checked;
                            _matches[i].Text = textBoxText.Text;
                            listBoxInspectItems.Items[i] = textBoxText.Text;
                        }
                    }
                }

                return;
            }

            if (_selectedCompareNode != null)
            {
                XmlNode newNode = ImageCompareDocument.CreateElement("Item");
                var text = newNode.OwnerDocument.CreateAttribute("Text");
                text.InnerText = textBoxText.Text;
                newNode.Attributes.Append(text);

                string databaseName = Path.Combine(_directoryPath, "Images.db");
                FileStream f;
                long pos;
                if (!File.Exists(databaseName))
                {
                    using (f = new FileStream(databaseName, FileMode.Create))
                    {
                        pos = f.Position;
                        new ManagedBitmap(pictureBoxInspectItem.Image as Bitmap).AppendToStream(f);
                    }
                }
                else
                {
                    using (f = new FileStream(databaseName, FileMode.Append))
                    {
                        pos = f.Position;
                        new ManagedBitmap(pictureBoxInspectItem.Image as Bitmap).AppendToStream(f);
                    }
                }
                string name = pos.ToString(CultureInfo.InvariantCulture);
                newNode.InnerText = name;

                SetItalic(newNode);
                ImageCompareDocument.DocumentElement.AppendChild(newNode);

                int index = listBoxInspectItems.SelectedIndex;
                _matches[index].Name = name;
                _matches[index].ExpandCount = 0;
                _matches[index].Italic = checkBoxItalic.Checked;
                _matches[index].Text = textBoxText.Text;
                SyncListBoxToMatches();

                ShowCount();
                listBoxInspectItems_SelectedIndexChanged(null, null);
            }
        }

        private void ShowCount()
        {
            labelCount.Text = listBoxInspectItems.Items.Count > 1 ? listBoxInspectItems.Items.Count.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }

        private void SyncListBoxToMatches()
        {
            int index = listBoxInspectItems.SelectedIndex;
            listBoxInspectItems.Items.Clear();
            for (int i = 0; i < _matches.Count; i++)
            {
                listBoxInspectItems.Items.Add(_matches[i]);
            }
            listBoxInspectItems.SelectedIndex = index;
        }

        private void SelectCharacterItem(bool previous, bool missingMatch)
        {
            if (previous)
            {
                if (missingMatch)
                {
                    for (int i = listBoxInspectItems.SelectedIndex - 1; i >= 0; i--)
                    {
                        if (_matches[i].Text == LanguageSettings.Current.VobSubOcr.NoMatch)
                        {
                            listBoxInspectItems.SelectedIndex = i;
                            break;
                        }
                    }
                }
                else if (listBoxInspectItems.SelectedIndex > 0)
                {
                    listBoxInspectItems.SelectedIndex--;
                }
            }
            else
            {
                if (missingMatch)
                {
                    for (int i = listBoxInspectItems.SelectedIndex + 1; i < _matches.Count; i++)
                    {
                        if (_matches[i].Text == LanguageSettings.Current.VobSubOcr.NoMatch)
                        {
                            listBoxInspectItems.SelectedIndex = i;
                            break;
                        }
                    }
                }
                else if (listBoxInspectItems.SelectedIndex < listBoxInspectItems.Items.Count - 1)
                {
                    listBoxInspectItems.SelectedIndex++;
                }
            }
        }

        private void VobSubOcrCharacterInspect_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
            }
        }

        private void contextMenuStripListBox_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (listBoxInspectItems.SelectedIndex < 0 ||
                listBoxInspectItems.SelectedIndex == listBoxInspectItems.Items.Count - 1 ||
                _binOcrDb == null ||
                _selectedCompareBinaryOcrBitmap != null && _selectedCompareBinaryOcrBitmap.ExpandCount > 1)
            {
                addBetterMultiMatchToolStripMenuItem.Enabled = false;
                return;
            }

            if (_matches[listBoxInspectItems.SelectedIndex].ImageSplitterItem == null)
            {
                addBetterMultiMatchToolStripMenuItem.Enabled = false;
                return;
            }

            var next = _matches[listBoxInspectItems.SelectedIndex + 1];
            if (next.ExpandCount > 0 || next.Extra?.Count > 0)
            {
                addBetterMultiMatchToolStripMenuItem.Enabled = false;
                return;
            }

            if (next.Text == LanguageSettings.Current.VobSubOcr.NoMatch)
            {
                return;
            }

            if (next.ImageSplitterItem?.NikseBitmap == null || !string.IsNullOrWhiteSpace(next.ImageSplitterItem.SpecialCharacter))
            {
                addBetterMultiMatchToolStripMenuItem.Enabled = false;
            }
        }

        private void addBetterMultiMatchToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var form = new AddBetterMultiMatch())
            {
                form.Initialize(listBoxInspectItems.SelectedIndex, _matches, _splitterItems);
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    _binOcrDb.Add(form.ExpandedMatch);
                    DialogResult = DialogResult.OK;
                }
            }
        }

        private void InsertLanguageCharacter(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem toolStripMenuItem)
            {
                var start = textBoxText.SelectionStart;
                textBoxText.SelectedText = toolStripMenuItem.Text;
                textBoxText.SelectionLength = 0;
                textBoxText.SelectionStart = start + toolStripMenuItem.Text.Length;
            }
        }

        private void buttonDetectFont_Click(object sender, EventArgs e)
        {
            using (var form = new BinaryOcrTrain())
            {
                form.InitializeDetectFont(_selectedCompareBinaryOcrBitmap, textBoxText.Text);
                form.ShowDialog(this);

                if (form.AutoDetectedFonts.Count > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var font in form.AutoDetectedFonts)
                    {
                        sb.AppendLine(font);
                    }
                    MessageBox.Show(sb.ToString().Trim());
                }
                else
                {
                    MessageBox.Show("Font not found!");
                }
            }
        }

        private void checkBoxItalic_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxItalic.Checked)
            {
                labelTextAssociatedWithImage.Font = new Font(labelTextAssociatedWithImage.Font.FontFamily, labelTextAssociatedWithImage.Font.Size, FontStyle.Italic);
                textBoxText.Font = new Font(textBoxText.Font.FontFamily, textBoxText.Font.Size, FontStyle.Italic | FontStyle.Bold);
            }
            else
            {
                labelTextAssociatedWithImage.Font = new Font(labelTextAssociatedWithImage.Font.FontFamily, labelTextAssociatedWithImage.Font.Size);
                textBoxText.Font = new Font(textBoxText.Font.FontFamily, textBoxText.Font.Size, FontStyle.Bold);
            }
        }

        private void TextBoxText_KeyDown(object sender, KeyEventArgs e)
        {
            bool executedAddOrUpdate = false;
            bool changedSelection = false;
            if (e.KeyCode == Keys.Enter)
            {
                //Instantly executes the least destructive action
                if (buttonAddBetterMatch.Enabled)
                {
                    AddBetterMatch();
                    executedAddOrUpdate = true;
                }
                else if (buttonUpdate.Enabled)
                {
                    UpdateMatch();
                    executedAddOrUpdate = true;
                }
            }
            else if (e.KeyCode == Keys.Up)
            {
                //Allow navigating the list box while in the text field. Put the cursor at the end too, in case there already is a character
                if (e.Modifiers == Keys.Control)
                {
                    SelectCharacterItem(true, true);
                }
                else
                {
                    SelectCharacterItem(true, false);
                }
                changedSelection = true;
            }
            else if (e.KeyCode == Keys.Down)
            {
                if (e.Modifiers == Keys.Control)
                {
                    SelectCharacterItem(false, true);
                }
                else
                {
                    SelectCharacterItem(false, false);
                }
                changedSelection = true;
            }

            if (changedSelection)
            {
                textBoxText.SelectionStart = 0;
                textBoxText.SelectionLength = textBoxText.Text.Length;
            }

            if (executedAddOrUpdate || changedSelection)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void ListBoxInspectItems_KeyDown(object sender, KeyEventArgs e)
        {
            //Allows instantly entering back into the textbox, editing the next recognized character.
            bool handled = false;
            if (e.KeyCode == Keys.Enter)
            {
                handled = true;
                textBoxText.Focus();
            }
            else if (e.Modifiers == Keys.Control)
            {
                if (e.KeyCode == Keys.Up)
                {
                    handled = true;
                    SelectCharacterItem(true, true);
                }
                else if (e.KeyCode == Keys.Down)
                {
                    handled = true;
                    SelectCharacterItem(false, true);
                }
            }

            if (handled)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void AddOrUpdateMatchToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (buttonAddBetterMatch.Enabled)
            {
                AddBetterMatch();
            }
            else if (buttonUpdate.Enabled)
            {
                UpdateMatch();
            }
        }

        private void JumpToPreviousMissingMatchToolStripMenuItem_Click(object sender, EventArgs e) => SelectCharacterItem(true, true);

        private void JumpToNextMissingMatchToolStripMenuItem_Click(object sender, EventArgs e) => SelectCharacterItem(false, true);

        private void FocusTheTextInputToolStripMenuItem_Click(object sender, EventArgs e) => textBoxText.Focus();

        private void SelectThePreviousMatchToolStripMenuItem_Click(object sender, EventArgs e) => SelectCharacterItem(true, false);

        private void SelectTheNextMatchToolStripMenuItem_Click(object sender, EventArgs e) => SelectCharacterItem(false, false);
    }
}
