﻿using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreamTimer.Backend;
using StreamTimer.Wrappers;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics.PerformanceData;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace streamdocktools.Actions
{
    [PluginActionId("com.ashurtech.streamdocktools.TriggerScanAction")]
    public class TriggerScanAction : PluginBase
    {
        private class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                PluginSettings instance = new PluginSettings
                {
                    APIKEY = String.Empty,
                    APIURL = String.Empty,
                };

                return instance;
            }
            [JsonProperty(PropertyName = "APIKEY")]
            public string APIKEY { get; set; }

            [JsonProperty(PropertyName = "APIURL")]
            public string APIURL { get; set; }
        }

        #region Private members
        private readonly PluginSettings settings;

        #endregion

        #region PluginBase Methods

        public TriggerScanAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                this.settings = PluginSettings.CreateDefaultSettings();
                Connection.SetSettingsAsync(JObject.FromObject(settings));
            }
            else
            {
                this.settings = payload.Settings.ToObject<PluginSettings>();
            }
            Connection.OnPropertyInspectorDidAppear += Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin += Connection_OnSendToPlugin;
            InitializeSettings();
        }

        public override void Dispose()
        {
            Connection.OnPropertyInspectorDidAppear -= Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            tmrAlert.Elapsed -= TmrAlert_Elapsed;
            tmrAlert.Stop();
            Logger.Instance.LogMessage(TracingLevel.INFO, "Destructor called");
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            hadInputError = false;
            bool playSound = settings.PlaySoundOnEnd;
            string fileName = settings.TimerFileName;
            // New in StreamDeck-Tools v2.0:
            Tools.AutoPopulateSettings(settings, payload.Settings);

            // if (fileName != settings.TimerFileName && !String.IsNullOrEmpty(settings.TimerFileName))
            // {
            //     HelperUtils.WriteToFile(settings.TimerFileName, String.Empty);
            // }

            // if (playSound != settings.PlaySoundOnEnd && settings.PlaySoundOnEnad)
            // {
            //     PropagatePlaybackDevices();
            // }

            InitializeSettings();
            SaveSettings();
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }

        public async override void KeyPressed(KeyPayload payload)
        {
            // if (settings.HourglassMode)
            // {
            //     displayCurrentStatus = true;
            // }
            // Logger.Instance.LogMessage(TracingLevel.INFO, "Key Pressed");
            using (HttpClient client = new HttpClient())
            {
            Logger.Instance.LogMessage(TracingLevel.INFO, "scanstash_Response: DoStashScan triggered"); 
            ///var theapikey = settings?.APIKEY ?? string.Empty;
            ///var theapiurl = settings?.APIURL ?? string.Empty;
            var key = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1aWQiOiJ4IiwiaWF0IjoxNzAyMTQ1MDI0LCJzdWIiOiJBUElLZXkifQ._17NY3GaDzyHnDaaT5zgGy9MEIAE0xOp-VmFBM-PqB4";
            var url = "http://quark-v:9999/GrahphQL";
            client.DefaultRequestHeaders.Add("Accept", "application/graphql-response+json");
            client.DefaultRequestHeaders.Add("ApiKey", key);
            client.DefaultRequestHeaders.Add("Content-Type", "application/graphql-response+json");
            // Set up the request body
            var jsonBody = "{ query = \"mutation { metadataGenerate ( input : { sprites: true previews: true imagePreviews: true markers: true transcodes: false } ) }\"}";
            var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(jsonBody), Encoding.UTF8, "application/json");

            // Send the POST request
            HttpResponseMessage response = await client.PostAsync(url, content);

            // Read and display the response
            string responseString = await response.Content.ReadAsStringAsync();
            Logger.Instance.LogMessage(TracingLevel.INFO, "scanstash_Response: {responseString}");
            }

            if (isAlerting)
            {
                await ResetAlert();
                return;
            }
        }

        public override void KeyReleased(KeyPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "Key Released");
        }

        public async override void OnTick()
        {
            // Stream Deck calls this function every second, 
            // so this is the best place to determine if we need to reset (versus the internal timer which may be paused)

            if (isAlerting)
            {
                if (endDateTime > DateTime.MinValue)
                {
                    await ShowElapsedTimeOnKey();

                    long timeElapsed = (long)(DateTime.Now - endDateTime).TotalSeconds;
                    if (autoResetSeconds > 0 && timeElapsed > autoResetSeconds)
                    {
                        await ResetAlert();
                    }
                }
                return;
            }

            HandleTimeDisplay();
        }

        #endregion

        #region Private methods

        private async void HandleTimeDisplay()
        {
            if (hadInputError)
            {
                return;
            }

            if (endDateTime == DateTime.MinValue)
            {
                await Connection.SetTitleAsync((string)null);
                await Connection.SetImageAsync((string)null);
                return;
            }

            // Handle alerting
            long total = (long)(endDateTime - DateTime.Now).TotalSeconds;
            if (total <= 0 && !tmrAlert.Enabled) // Time passed, need to alert
            {
                total = 0;
                isAlerting = true;
                tmrAlert.Start();
                PlaySoundOnEnd();
            }

            // Handle hourglass mode
            if (settings.HourglassMode)
            {
                await DisplayHourglass(total);
                if (settings.HourglassTime)
                {
                    await ShowTimeOnKey();
                }
            }
            else // Not Hourglass mode
            {
                await Connection.SetImageAsync((string)null);
                await ShowTimeOnKey();
            }
        }

        private async Task ShowElapsedTimeOnKey()
        {
            if (!settings.CountUpOnEnd)
            {
                await Connection.SetTitleAsync(null);
                return;
            }
            string output = HelperUtils.FormatTime((long)(DateTime.Now - endDateTime).TotalSeconds, "h:mm:ss", settings.Multiline);
            await Connection.SetTitleAsync(output);

            string fileOutput = "00:00";
            if (!String.IsNullOrEmpty(settings.CountdownEndText))
            {
                fileOutput = settings.CountdownEndText.Replace(@"\n", "\n");
            }
            HelperUtils.WriteToFile(settings.TimerFileName, fileOutput);
        }
        private async Task ShowTimeOnKey()
        {
            string output = HelperUtils.FormatTime((long)(endDateTime - DateTime.Now).TotalSeconds, settings.TimeFormat, settings.Multiline);
            if (output == null)
            {
                settings.TimeFormat = HelperUtils.DEFAULT_TIME_FORMAT;
                await SaveSettings();
            }
            await Connection.SetTitleAsync($"{settings.KeyPrefix?.Replace(@"\n", "\n")}{output}");
            HelperUtils.WriteToFile(settings.TimerFileName, $"{settings.FilePrefix.Replace(@"\n", "\n")}{output.Replace("\n", ":")}");
        }

        private void SetRemainingInterval()
        {
            endDateTime = DateTime.MinValue;
            if (settings.CountdownTimeOnly)
            {
                if (!TimeSpan.TryParse(settings.CountdownTime, out TimeSpan timespan))
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, $"Invalid Countdown Time: {settings.CountdownTime}");
                    settings.CountdownTime = DEFAULT_COUNTDOWN_TIME_INTERVAL;
                    SaveSettings();
                    HandleInputError();
                }
                else
                {
                    DateTime dt = DateTime.Today.Add(timespan);
                    if ((dt - DateTime.Now).TotalSeconds < 0)
                    {
                        dt = dt.AddDays(1);
                    }
                    endDateTime = dt;
                    TriggerTimerIntervalChange();
                }
            }
            else
            {
                if (!DateTime.TryParse(settings.CountdownDateTime, out DateTime dt))
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, $"Invalid Countdown DateTime: {settings.CountdownDateTime}");
                    settings.CountdownDateTime = DEFAULT_COUNTDOWN_DATETIME_INTERVAL;
                    SaveSettings();
                    HandleInputError();
                }
                else
                {
                    if ((dt - DateTime.Now).TotalSeconds < 0) // Verify it hasn't passed
                    {
                        HandleInputError();
                    }
                    else
                    {
                        endDateTime = dt;
                    }
                    TriggerTimerIntervalChange();
                }
            }
        }

        private void HandleInputError()
        {
            hadInputError = true;
            try
            {
                Connection.SetTitleAsync(null);
                using (Image img = Image.FromFile(INPUT_ERROR_FILE))
                {
                    Connection.SetImageAsync(img);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"{this.GetType()} HandleInputError Exception: {ex}");
            }

        }

        private void TriggerTimerIntervalChange()
        {
            highestTimerSeconds = (long)(endDateTime - DateTime.Now).TotalSeconds;
        }

        private Task SaveSettings()
        {
            return Connection.SetSettingsAsync(JObject.FromObject(settings));
        }

        private Color GenerateStageColor(string initialColor, int stage, int totalAmountOfStages)
        {
            Color color = ColorTranslator.FromHtml(initialColor);
            int a = color.A;
            double r = color.R;
            double g = color.G;
            double b = color.B;

            // Try and increase the color in the last stage;
            if (stage == totalAmountOfStages - 1)
            {
                stage = 1;
            }

            for (int idx = 0; idx < stage; idx++)
            {
                r /= 2;
                g /= 2;
                b /= 2;
            }

            return Color.FromArgb(a, (int)r, (int)g, (int)b);
        }

        private void TmrAlert_Elapsed(object sender, ElapsedEventArgs e)
        {
            Bitmap img = Tools.GenerateGenericKeyImage(out Graphics graphics);
            int height = img.Height;
            int width = img.Width;

            // Background
            var bgBrush = new SolidBrush(GenerateStageColor(settings.AlertColor, alertStage, TOTAL_ALERT_STAGES));
            graphics.FillRectangle(bgBrush, 0, 0, width, height);
            Connection.SetImageAsync(img);

            alertStage = (alertStage + 1) % TOTAL_ALERT_STAGES;
            graphics.Dispose();
        }

        private Color GetHourglassColor(Color initialColor, double remainingPercentage)
        {
            if (initialColor.R != 0 || initialColor.G != 0 || initialColor.B != 0)
            {
                return initialColor;
            }

            if (remainingPercentage > 0.5)
            {
                return Color.Green;
            }
            else if (remainingPercentage > 0.20)
            {
                return Color.Yellow;
            }
            else
            {
                return Color.Red;
            }
        }

        private async Task DisplayHourglass(long remainingSeconds)
        {
            long totalSeconds = highestTimerSeconds;

            if (remainingSeconds <= 0)
            {
                return;
            }
            else if (remainingSeconds > totalSeconds)
            {
                totalSeconds = remainingSeconds + 1;
            }

            double remainingPercentage = (double)remainingSeconds / (double)totalSeconds;

            Bitmap img = Tools.GenerateGenericKeyImage(out Graphics graphics);
            int height = img.Height;
            int width = img.Width;
            int startHeight = height - (int)(height * remainingPercentage);

            // Background

            if (settings.HourglassImageMode && pauseImage != null)
            {
                // Draw image
                graphics.DrawImage(pauseImage, new Rectangle(0, 0, width, height));

                // Cover the top parts based on the time left
                graphics.FillRectangle(new SolidBrush(Color.Black), 0, 0, width, startHeight);
            }
            else
            {
                var color = GetHourglassColor(ColorTranslator.FromHtml(settings.HourglassColor), remainingPercentage);
                var bgBrush = new SolidBrush(color);
                graphics.FillRectangle(bgBrush, 0, startHeight, width, height);
            }

            await Connection.SetTitleAsync((string)null);
            await Connection.SetImageAsync(img);
            graphics.Dispose();
        }

        private void InitializeSettings()
        {
            Task.Run(() =>
            {
                SetRemainingInterval();

                if (!Int32.TryParse(settings.AutoResetSeconds, out autoResetSeconds))
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, $"Invalid AutoResetSeconds: {settings.AutoResetSeconds}");
                    settings.AutoResetSeconds = DEFAULT_AUTO_RESET_SECONDS.ToString();
                    autoResetSeconds = DEFAULT_AUTO_RESET_SECONDS;
                    SaveSettings();
                }
            });
        }

        private void PropagatePlaybackDevices()
        {
            settings.PlaybackDevices = new List<PlaybackDevice>();

            try
            {
                if (settings.PlaySoundOnEnd)
                {
                    settings.PlaybackDevices = AudioUtils.Common.GetAllPlaybackDevices(true).Select(d => new PlaybackDevice() { ProductName = d }).OrderBy(p => p.ProductName).ToList();
                    SaveSettings();
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error propagating playback devices {ex}");
            }
        }

        private void PlaySoundOnEnd()
        {
            Task.Run(async () =>
            {
                if (!settings.PlaySoundOnEnd)
                {
                    return;
                }

                stopPlayback = false;
                if (String.IsNullOrEmpty(settings.PlaySoundOnEndFile) || string.IsNullOrEmpty(settings.PlaybackDevice))
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, $"PlaySoundOnEnd called but File or Playback device are empty. File: {settings.PlaySoundOnEndFile} Device: {settings.PlaybackDevice}");
                    return;
                }

                if (!File.Exists(settings.PlaySoundOnEndFile))
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, $"PlaySoundOnEnd called but file does not exist: {settings.PlaySoundOnEndFile}");
                    return;
                }

                Logger.Instance.LogMessage(TracingLevel.INFO, $"PlaySoundOnEnd called. Playing {settings.PlaySoundOnEndFile} on device: {settings.PlaybackDevice}");
                await AudioUtils.Common.PlaySound(settings.PlaySoundOnEndFile, settings.PlaybackDevice);
            });
        }

        private void StopPlayback()
        {
            stopPlayback = true;
        }

        private void Connection_OnSendToPlugin(object sender, BarRaider.SdTools.Wrappers.SDEventReceivedEventArgs<BarRaider.SdTools.Events.SendToPlugin> e)
        {
            var payload = e.Event.Payload;

            Logger.Instance.LogMessage(TracingLevel.INFO, "OnSendToPlugin called");
            if (payload["property_inspector"] != null)
            {
                switch (payload["property_inspector"].ToString().ToLowerInvariant())
                {
                    case "loadsavepicker":
                        string propertyName = (string)payload["property_name"];
                        string pickerTitle = (string)payload["picker_title"];
                        string pickerFilter = (string)payload["picker_filter"];
                        string fileName = PickersUtil.Pickers.SaveFilePicker(pickerTitle, null, pickerFilter);
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            if (!PickersUtil.Pickers.SetJsonPropertyValue(settings, propertyName, fileName))
                            {
                                Logger.Instance.LogMessage(TracingLevel.ERROR, "Failed to save picker value to settings");
                            }
                            SaveSettings();
                        }
                        break;
                }
            }
        }

        private void Connection_OnPropertyInspectorDidAppear(object sender, BarRaider.SdTools.Wrappers.SDEventReceivedEventArgs<BarRaider.SdTools.Events.PropertyInspectorDidAppear> e)
        {
            PropagatePlaybackDevices();
        }

        private async Task ResetAlert()
        {
            isAlerting = false;
            tmrAlert.Stop();
            StopPlayback();
            await Connection.SetImageAsync((string)null);
            await Connection.SetTitleAsync(null);
            SetRemainingInterval();

        }

        #endregion
    }
}