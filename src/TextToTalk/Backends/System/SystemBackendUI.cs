﻿using ImGuiNET;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Speech.Synthesis;
using System.Text;
using Dalamud.Logging;
using TextToTalk.Lexicons;
using TextToTalk.Lexicons.Updater;
using TextToTalk.UI.Dalamud.Lexicons;

namespace TextToTalk.Backends.System;

public class SystemBackendUI
{
    private static readonly Vector4 Red = new(1, 0, 0, 1);
    private static readonly Vector4 HintColor = new(0.7f, 0.7f, 0.7f, 1.0f);

    private static readonly Lazy<SpeechSynthesizer> DummySynthesizer = new(() =>
    {
        try
        {
            return new SpeechSynthesizer();
        }
        catch (Exception e)
        {
            PluginLog.LogError(e, "Failed to create speech synthesizer.");
            return null;
        }
    });

    private readonly PluginConfiguration config;
    private readonly LexiconComponent lexiconComponent;
    private readonly ConcurrentQueue<SelectVoiceFailedException> selectVoiceFailures;

    public SystemBackendUI(PluginConfiguration config, LexiconManager lexiconManager,
        ConcurrentQueue<SelectVoiceFailedException> selectVoiceFailures, HttpClient http)
    {
        // TODO: Make this configurable
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var downloadPath = Path.Join(appData, "TextToTalk");

        var lexiconRepository = new LexiconRepository(http, downloadPath);

        this.config = config;
        this.lexiconComponent = new LexiconComponent(lexiconManager, lexiconRepository, config, () => config.Lexicons);
        this.selectVoiceFailures = selectVoiceFailures;
    }

    private readonly IDictionary<string, Exception> voiceExceptions = new Dictionary<string, Exception>();

    public void DrawSettings(IConfigUIDelegates helpers)
    {
        ImGui.TextColored(HintColor, "This TTS provider is only supported on Windows.");

        if (this.selectVoiceFailures.TryDequeue(out var e1))
        {
            this.voiceExceptions[e1.VoiceId] = e1;
        }

        var currentVoicePreset = this.config.GetCurrentVoicePreset<SystemVoicePreset>();

        var presets = this.config.GetVoicePresetsForBackend(TTSBackend.System).ToList();
        presets.Sort((a, b) => a.Id - b.Id);

        if (presets.Any())
        {
            var presetIndex = currentVoicePreset is not null ? presets.IndexOf(currentVoicePreset) : -1;
            if (ImGui.Combo("Preset##TTTSystemVoice3", ref presetIndex, presets.Select(p => p.Name).ToArray(),
                    presets.Count))
            {
                this.config.SetCurrentVoicePreset(presets[presetIndex].Id);
                this.config.Save();
            }
        }
        else
        {
            ImGui.TextColored(Red, "You have no presets. Please create one using the \"New preset\" button.");
        }

        if (ImGui.Button("New preset##TTTSystemVoice4") &&
            this.config.TryCreateVoicePreset<SystemVoicePreset>(out var newPreset))
        {
            this.config.SetCurrentVoicePreset(newPreset.Id);
        }

        if (!presets.Any() || currentVoicePreset is null)
        {
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button("Delete preset##TTTSystemVoice5"))
        {
            var otherPreset = this.config.VoicePresetConfig.VoicePresets.First(p => p.Id != currentVoicePreset.Id);
            this.config.SetCurrentVoicePreset(otherPreset.Id);

            if (this.config.VoicePresetConfig.UngenderedVoicePresets[TTSBackend.System] == currentVoicePreset.Id)
            {
                this.config.VoicePresetConfig.UngenderedVoicePresets[TTSBackend.System] = 0;
            }
            else if (this.config.VoicePresetConfig.MaleVoicePresets[TTSBackend.System] == currentVoicePreset.Id)
            {
                this.config.VoicePresetConfig.MaleVoicePresets[TTSBackend.System] = 0;
            }
            else if (this.config.VoicePresetConfig.FemaleVoicePresets[TTSBackend.System] == currentVoicePreset.Id)
            {
                this.config.VoicePresetConfig.FemaleVoicePresets[TTSBackend.System] = 0;
            }

            this.config.VoicePresetConfig.VoicePresets.Remove(currentVoicePreset);
        }

        var presetName = currentVoicePreset.Name;
        if (ImGui.InputText("Preset name##TTTSystemVoice99", ref presetName, 64))
        {
            currentVoicePreset.Name = presetName;
            this.config.Save();
        }

        var rate = currentVoicePreset.Rate;
        if (ImGui.SliderInt("Rate##TTTSystemVoice6", ref rate, -10, 10))
        {
            currentVoicePreset.Rate = rate;
            this.config.Save();
        }

        var volume = currentVoicePreset.Volume;
        if (ImGui.SliderInt("Volume##TTTSystemVoice7", ref volume, 0, 100))
        {
            currentVoicePreset.Volume = volume;
            this.config.Save();
        }

        var voiceName = currentVoicePreset.VoiceName;
        var voices = DummySynthesizer.Value != null
            ? DummySynthesizer.Value.GetInstalledVoices().Where(iv => iv?.Enabled ?? false).ToList()
            : new List<InstalledVoice>();
        if (voices.Any())
        {
            var voicesUi = voices.Select(FormatVoiceInfo).ToArray();
            var voiceIndex = voices.FindIndex(iv => iv.VoiceInfo?.Name == voiceName);
            if (ImGui.Combo("Voice##TTTSystemVoice8", ref voiceIndex, voicesUi, voices.Count))
            {
                this.voiceExceptions.Remove(voices[voiceIndex].VoiceInfo.Name);
                currentVoicePreset.VoiceName = voices[voiceIndex].VoiceInfo.Name;
                this.config.Save();
            }

            if (this.voiceExceptions.TryGetValue(voiceName, out var e2))
            {
                PrintVoiceExceptions(e2);
            }
        }

        if (ImGui.Button("Don't see all of your voices?##VoiceUnlockerSuggestion"))
        {
            helpers.OpenVoiceUnlocker();
        }

        this.lexiconComponent.Draw();

        ImGui.Spacing();

        var useGenderedVoicePresets = this.config.UseGenderedVoicePresets;
        if (ImGui.Checkbox("Use gendered voice presets##TTTSystemVoice9", ref useGenderedVoicePresets))
        {
            this.config.UseGenderedVoicePresets = useGenderedVoicePresets;
            this.config.Save();
        }

        if (useGenderedVoicePresets)
        {
            var currentUngenderedVoicePreset = this.config.GetCurrentUngenderedVoicePreset<SystemVoicePreset>();
            var currentMaleVoicePreset = this.config.GetCurrentMaleVoicePreset<SystemVoicePreset>();
            var currentFemaleVoicePreset = this.config.GetCurrentFemaleVoicePreset<SystemVoicePreset>();

            var presetArray = presets.Select(p => p.Name).ToArray();

            var ungenderedPresetIndex = presets.IndexOf(currentUngenderedVoicePreset);
            if (ImGui.Combo("Ungendered preset##TTTSystemVoice12", ref ungenderedPresetIndex, presetArray, presets.Count))
            {
                this.config.VoicePresetConfig.UngenderedVoicePresets[TTSBackend.System] = presets[ungenderedPresetIndex].Id;
                this.config.Save();
            }

            var malePresetIndex = presets.IndexOf(currentMaleVoicePreset);
            if (ImGui.Combo("Male preset##TTTSystemVoice10", ref malePresetIndex, presetArray, presets.Count))
            {
                this.config.VoicePresetConfig.MaleVoicePresets[TTSBackend.System] = presets[malePresetIndex].Id;
                this.config.Save();
            }

            var femalePresetIndex = presets.IndexOf(currentFemaleVoicePreset);
            if (ImGui.Combo("Female preset##TTTSystemVoice11", ref femalePresetIndex, presetArray, presets.Count))
            {
                this.config.VoicePresetConfig.FemaleVoicePresets[TTSBackend.System] = presets[femalePresetIndex].Id;
                this.config.Save();
            }
        }
    }

    private static void PrintVoiceExceptions(Exception e)
    {
        if (e.InnerException != null)
        {
            ImGui.TextColored(Red, $"Voice errors:\n  {e.Message}");
            PrintVoiceExceptionsR(e.InnerException);
        }
        else
        {
            ImGui.TextColored(Red, $"Voice error:\n  {e.Message}");
        }
    }

    private static void PrintVoiceExceptionsR(Exception e)
    {
        do
        {
            ImGui.TextColored(Red, $"  {e.Message}");
        } while (e.InnerException != null);
    }

    private static string FormatVoiceInfo(InstalledVoice iv)
    {
        var line = new StringBuilder(iv.VoiceInfo?.Name ?? "");
        line.Append(" (")
            .Append(iv.VoiceInfo?.Culture?.TwoLetterISOLanguageName.ToUpperInvariant() ?? "Unknown Language")
            .Append(")");

        if (iv.VoiceInfo?.Name.Contains("Zira") ?? false)
        {
            line.Append(" [UNSTABLE]");
        }

        return line.ToString();
    }
}