﻿using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.Http;
using Nikse.SubtitleEdit.Logic;
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using MessageBox = Nikse.SubtitleEdit.Forms.SeMsgBox.MessageBox;

namespace Nikse.SubtitleEdit.Forms
{
    public sealed partial class DownloadYouTubeDl : Form
    {
        public const string Url = "https://github.com/yt-dlp/yt-dlp/releases/download/2024.07.02/yt-dlp.exe";
        public const string Sha512Hash = "8aa32a8580cecce2fb733052ada22cbbd8727192b77e2f63000ccfa47aeee893a975f65eed121a85e40e894bcc671adaee36ea2f10748427d6e4109c02295703";
        public bool AutoClose { get; internal set; }
        private readonly CancellationTokenSource _cancellationTokenSource;

        public DownloadYouTubeDl()
        {
            UiUtil.PreInitialize(this);
            InitializeComponent();
            UiUtil.FixFonts(this);
            Text = string.Format(LanguageSettings.Current.Settings.DownloadX, "youtube-dl");
            buttonOK.Text = LanguageSettings.Current.General.Ok;
            buttonCancel.Text = LanguageSettings.Current.General.Cancel;
            UiUtil.FixLargeFonts(this, buttonOK);
            _cancellationTokenSource = new CancellationTokenSource();
        }

        private void DownloadFfmpeg_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
            }
        }

        private void buttonOK_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            _cancellationTokenSource.Cancel();
            DialogResult = DialogResult.Cancel;
        }

        private void DownloadFfmpeg_Shown(object sender, EventArgs e)
        {
            try
            {
                labelPleaseWait.Text = LanguageSettings.Current.General.PleaseWait;
                buttonOK.Enabled = false;
                Refresh();
                Cursor = Cursors.WaitCursor;
                using (var httpClient = HttpClientFactory.CreateHttpClientDownloader())
                using (var downloadStream = new MemoryStream())
                {
                    var downloadTask = httpClient.DownloadAsync(Url, downloadStream, new Progress<float>((progress) =>
                    {
                        var pct = (int)Math.Round(progress * 100.0, MidpointRounding.AwayFromZero);
                        labelPleaseWait.Text = LanguageSettings.Current.General.PleaseWait + "  " + pct + "%";
                    }), _cancellationTokenSource.Token);

                    while (!downloadTask.IsCompleted && !downloadTask.IsCanceled)
                    {
                        Application.DoEvents();
                    }

                    if (downloadTask.IsCanceled)
                    {
                        DialogResult = DialogResult.Cancel;
                        labelPleaseWait.Refresh();
                        return;
                    }

                    CompleteDownload(downloadStream);
                }
            }
            catch (Exception exception)
            {
                labelPleaseWait.Text = string.Empty;
                buttonOK.Enabled = true;
                Cursor = Cursors.Default;
                MessageBox.Show($"Unable to download {Url}!" + Environment.NewLine + Environment.NewLine +
                                exception.Message + Environment.NewLine + Environment.NewLine + exception.StackTrace);
                DialogResult = DialogResult.Cancel;
            }
        }

        private void CompleteDownload(MemoryStream downloadStream)
        {
            if (downloadStream.Length == 0)
            {
                throw new Exception("No content downloaded - missing file or no internet connection!");
            }

            var folder = Path.Combine(Configuration.DataDirectory);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            downloadStream.Position = 0;
            var bytes = downloadStream.ToArray();
            var hash = Utilities.GetSha512Hash(bytes);
            if (hash != Sha512Hash)
            {
                MessageBox.Show("yt-dlp.exe SHA-512 hash does not match!");
                return;
            }

            File.WriteAllBytes(Path.Combine(folder, "yt-dlp.exe"), bytes);

            Cursor = Cursors.Default;
            labelPleaseWait.Text = string.Empty;

            if (AutoClose)
            {
                DialogResult = DialogResult.OK;
                return;
            }

            buttonOK.Enabled = true;
            labelPleaseWait.Text = string.Format(LanguageSettings.Current.SettingsFfmpeg.XDownloadOk, "yt-dlp.exe");
        }
    }
}
