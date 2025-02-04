﻿using Amazon;
using Amazon.Polly;
using Amazon.Polly.Model;
using Dalamud.Logging;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Text.RegularExpressions;
using TextToTalk.Lexicons;
using TextToTalk.Lexicons.Updater;
using TextToTalk.UI.Dalamud.Lexicons;

namespace TextToTalk.Backends.Polly;

public class PollyBackendUI
{
    private static readonly Vector4 HintColor = new(0.7f, 0.7f, 0.7f, 1.0f);
    private static readonly Vector4 Red = new(1, 0, 0, 1);
    private static readonly string[] Regions = RegionEndpoint.EnumerableAllRegions.Select(r => r.SystemName).ToArray();
    private static readonly string[] Engines = { Engine.Neural, Engine.Standard };

    private readonly PluginConfiguration config;
    private readonly LexiconComponent lexiconComponent;
    private readonly LexiconManager lexiconManager;

    private readonly Func<PollyClient> getPolly;
    private readonly Action<PollyClient> setPolly;
    private readonly Func<IList<Voice>> getVoices;
    private readonly Action<IList<Voice>> setVoices;

    private string accessKey = string.Empty;
    private string secretKey = string.Empty;

    public PollyBackendUI(PluginConfiguration config, LexiconManager lexiconManager, HttpClient http,
        Func<PollyClient> getPolly, Action<PollyClient> setPolly, Func<IList<Voice>> getVoices,
        Action<IList<Voice>> setVoices)
    {
        this.getPolly = getPolly;
        this.setPolly = setPolly;
        this.getVoices = getVoices;
        this.setVoices = setVoices;

        // TODO: Make this configurable
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var downloadPath = Path.Join(appData, "TextToTalk");
        var lexiconRepository = new LexiconRepository(http, downloadPath);

        this.config = config;
        this.lexiconComponent =
            new LexiconComponent(lexiconManager, lexiconRepository, config, () => config.PollyLexiconFiles);
        this.lexiconManager = lexiconManager;

        var credentials = PollyCredentialManager.LoadCredentials();
        if (credentials != null)
        {
            this.accessKey = credentials.UserName;
            this.secretKey = credentials.Password;

            var regionEndpoint =
                RegionEndpoint.EnumerableAllRegions.FirstOrDefault(r => r.SystemName == this.config.PollyRegion)
                ?? RegionEndpoint.EUWest1;
            PollyLogin(regionEndpoint);
        }
    }

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public void DrawSettings(IConfigUIDelegates helpers)
    {
        var region = this.config.PollyRegion;
        var regionIndex = Array.IndexOf(Regions, region);
        if (ImGui.Combo("Region##TTTPollyRegion", ref regionIndex, Regions, Regions.Length))
        {
            this.config.PollyRegion = Regions[regionIndex];
            this.config.Save();
        }

        ImGui.InputTextWithHint("##TTTPollyAccessKey", "Access key", ref this.accessKey, 100,
            ImGuiInputTextFlags.Password);
        ImGui.InputTextWithHint("##TTTPollySecretKey", "Secret key", ref this.secretKey, 100,
            ImGuiInputTextFlags.Password);

        if (ImGui.Button("Save and Login##TTTSavePollyAuth"))
        {
            var username = Whitespace.Replace(this.accessKey, "");
            var password = Whitespace.Replace(this.secretKey, "");
            PollyCredentialManager.SaveCredentials(username, password);

            var regionEndpoint =
                RegionEndpoint.EnumerableAllRegions.FirstOrDefault(r => r.SystemName == this.config.PollyRegion);
            if (regionEndpoint == null)
            {
                ImGui.TextColored(Red, "Invalid region!");
            }
            else
            {
                PollyLogin(regionEndpoint);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Register##TTTRegisterPollyAuth"))
        {
            WebBrowser.Open("https://docs.aws.amazon.com/polly/latest/dg/getting-started.html");
        }

        ImGui.TextColored(HintColor, "Credentials secured with Windows Credential Manager");

        ImGui.Spacing();

        var currentVoicePreset = this.config.GetCurrentVoicePreset<PollyVoicePreset>();

        var presets = this.config.GetVoicePresetsForBackend(TTSBackend.AmazonPolly).ToList();
        presets.Sort((a, b) => a.Id - b.Id);
        var presetsArray = presets.ToArray();
        var presetNamesArray = presets.Select(p => p.Name).ToArray();

        if (presets.Any())
        {
            var presetIndex = presets.IndexOf(currentVoicePreset);
            if (ImGui.Combo("Preset##TTTPollyPresetSelect", ref presetIndex, presetNamesArray, presets.Count))
            {
                this.config.SetCurrentVoicePreset(presets[presetIndex].Id);
                this.config.Save();
            }
        }
        else
        {
            ImGui.TextColored(Red, "You have no presets. Please create one using the \"New preset\" button.");
        }

        if (ImGui.Button("New preset##TTTPollyVoice4") &&
            this.config.TryCreateVoicePreset<PollyVoicePreset>(out var newPreset))
        {
            this.config.SetCurrentVoicePreset(newPreset.Id);
        }

        if (!presets.Any() || currentVoicePreset is null)
        {
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button("Delete preset##TTTPollyVoice5"))
        {
            var otherPreset = this.config.VoicePresetConfig.VoicePresets.First(p => p.Id != currentVoicePreset.Id);
            this.config.SetCurrentVoicePreset(otherPreset.Id);

            if (this.config.VoicePresetConfig.UngenderedVoicePresets[TTSBackend.AmazonPolly] == currentVoicePreset.Id)
            {
                this.config.VoicePresetConfig.UngenderedVoicePresets[TTSBackend.AmazonPolly] = 0;
            }
            else if (this.config.VoicePresetConfig.MaleVoicePresets[TTSBackend.AmazonPolly] == currentVoicePreset.Id)
            {
                this.config.VoicePresetConfig.MaleVoicePresets[TTSBackend.AmazonPolly] = 0;
            }
            else if (this.config.VoicePresetConfig.FemaleVoicePresets[TTSBackend.AmazonPolly] == currentVoicePreset.Id)
            {
                this.config.VoicePresetConfig.FemaleVoicePresets[TTSBackend.AmazonPolly] = 0;
            }

            this.config.VoicePresetConfig.VoicePresets.Remove(currentVoicePreset);
        }

        var presetName = currentVoicePreset.Name;
        if (ImGui.InputText("Preset name##TTTPollyVoice99", ref presetName, 64))
        {
            currentVoicePreset.Name = presetName;
            this.config.Save();
        }

        var engine = currentVoicePreset.VoiceEngine;
        var engineIndex = Array.IndexOf(Engines, engine);
        if (ImGui.Combo("Engine##TTTPollyEngine", ref engineIndex, Engines, Engines.Length))
        {
            currentVoicePreset.VoiceEngine = Engines[engineIndex];
            this.config.Save();

            {
                var polly = this.getPolly.Invoke();
                var voices = polly?.GetVoicesForEngine(currentVoicePreset.VoiceEngine) ?? new List<Voice>();
                this.setVoices.Invoke(voices);
            }
        }

        {
            var voices = this.getVoices.Invoke();
            var voiceArray = voices.Select(v => v.Name).ToArray();
            var voiceIdArray = voices.Select(v => v.Id).ToArray();
            var voiceIndex = Array.IndexOf(voiceIdArray, currentVoicePreset.VoiceName);
            if (ImGui.Combo("Voice##TTTPollyVoice98", ref voiceIndex, voiceArray, voices.Count))
            {
                currentVoicePreset.VoiceName = voiceIdArray[voiceIndex];
                this.config.Save();
            }

            switch (voices.Count)
            {
                case 0:
                    ImGui.TextColored(Red, "No voices are available on this voice engine for the current region.\n" +
                                           "Please log in using a different region.");
                    break;
                case > 0 when !voices.Any(v => v.Id == currentVoicePreset.VoiceName):
                    ImGuiVoiceNotSupported();
                    break;
            }
        }

        var validSampleRates = new[] { "8000", "16000", "22050", "24000" };
        var sampleRate = currentVoicePreset.SampleRate.ToString();
        var sampleRateIndex = Array.IndexOf(validSampleRates, sampleRate);
        if (ImGui.Combo("Sample rate##TTTPollyVoice6", ref sampleRateIndex, validSampleRates, validSampleRates.Length))
        {
            currentVoicePreset.SampleRate = int.Parse(validSampleRates[sampleRateIndex]);
            this.config.Save();
        }

        var playbackRate = currentVoicePreset.PlaybackRate;
        if (ImGui.SliderInt("Playback rate##TTTPollyVoice8", ref playbackRate, 20, 200, "%d%%",
                ImGuiSliderFlags.AlwaysClamp))
        {
            currentVoicePreset.PlaybackRate = playbackRate;
            this.config.Save();
        }

        var volume = (int)(currentVoicePreset.Volume * 100);
        if (ImGui.SliderInt("Volume##TTTPollyVoice7", ref volume, 0, 200, "%d%%"))
        {
            currentVoicePreset.Volume = (float)Math.Round((double)volume / 100, 2);
            this.config.Save();
        }

        this.lexiconComponent.Draw();
        ImGui.Spacing();

        {
            var useGenderedVoicePresets = this.config.UseGenderedVoicePresets;
            if (ImGui.Checkbox("Use gendered voices##TTTPollyVoice2", ref useGenderedVoicePresets))
            {
                this.config.UseGenderedVoicePresets = useGenderedVoicePresets;
                this.config.Save();
            }

            ImGui.Spacing();
            if (useGenderedVoicePresets)
            {
                var currentUngenderedVoice = this.config.GetCurrentUngenderedVoicePreset<PollyVoicePreset>();
                var currentMaleVoice = this.config.GetCurrentMaleVoicePreset<PollyVoicePreset>();
                var currentFemaleVoice = this.config.GetCurrentFemaleVoicePreset<PollyVoicePreset>();

                var ungenderedVoiceIndex = Array.IndexOf(presetsArray, currentUngenderedVoice);
                if (ImGui.Combo("Ungendered preset##TTTPollyEnabledUPresetSelect", ref ungenderedVoiceIndex, presetNamesArray,
                        presets.Count))
                {
                    this.config.VoicePresetConfig.UngenderedVoicePresets[TTSBackend.AmazonPolly] = presetsArray[ungenderedVoiceIndex].Id;
                    this.config.Save();
                }

                var maleVoiceIndex = Array.IndexOf(presetsArray, currentMaleVoice);
                if (ImGui.Combo("Male preset##TTTPollyEnabledMPresetSelect", ref maleVoiceIndex, presetNamesArray, presets.Count))
                {
                    this.config.VoicePresetConfig.MaleVoicePresets[TTSBackend.AmazonPolly] = presetsArray[maleVoiceIndex].Id;
                    this.config.Save();
                }

                var femaleVoiceIndex = Array.IndexOf(presetsArray, currentFemaleVoice);
                if (ImGui.Combo("Female preset##TTTPollyEnabledFPresetSelect", ref femaleVoiceIndex, presetNamesArray, presets.Count))
                {
                    this.config.VoicePresetConfig.FemaleVoicePresets[TTSBackend.AmazonPolly] = presetsArray[femaleVoiceIndex].Id;
                    this.config.Save();
                }
            }
        }
    }

    private void PollyLogin(RegionEndpoint regionEndpoint)
    {
        var polly = this.getPolly.Invoke();
        polly?.Dispose();
        try
        {
            PluginLog.Log($"Logging into AWS region {regionEndpoint}.");
            polly = new PollyClient(this.accessKey, this.secretKey, regionEndpoint, this.lexiconManager);
            var currentVoicePreset = this.config.GetCurrentVoicePreset<PollyVoicePreset>();
            var voices = polly.GetVoicesForEngine(currentVoicePreset?.VoiceEngine ?? Engine.Neural);
            this.setPolly.Invoke(polly);
            this.setVoices.Invoke(voices);
        }
        catch (Exception e)
        {
            PluginLog.LogError(e, "Failed to initialize AWS client.");
            PollyCredentialManager.DeleteCredentials();
        }
    }

    private static void ImGuiVoiceNotSupported()
    {
        ImGui.TextColored(Red, "Voice not supported on this engine");
    }
}