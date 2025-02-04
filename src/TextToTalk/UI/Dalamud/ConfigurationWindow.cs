﻿using Dalamud.CrystalTower.UI;
using Dalamud.Game.Text;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Data;
using Dalamud.Interface;
using Dalamud.Logging;
using Lumina.Excel.GeneratedSheets;
using TextToTalk.Backends;
using TextToTalk.GameEnums;

namespace TextToTalk.UI.Dalamud
{
    public class ConfigurationWindow : ImmediateModeWindow
    {
        private static readonly Vector4 HintColor = new(0.7f, 0.7f, 0.7f, 1.0f);
        private static readonly Vector4 Green = new(0.0f, 1.0f, 0.0f, 1.0f);
        private static readonly Vector4 Red = new(1.0f, 0.0f, 0.0f, 1.0f);

        public PluginConfiguration Configuration { get; set; }
        public DataManager Data { get; set; }
        public VoiceBackendManager BackendManager { get; set; }
        public PlayerService Players { get; set; }

        private readonly IConfigUIDelegates helpers;

        private IDictionary<Guid, string> playerWorldEditing = new Dictionary<Guid, string>();
        private IDictionary<Guid, bool> playerWorldValid = new Dictionary<Guid, bool>();
        private string playerName = string.Empty;
        private string playerWorld = string.Empty;
        private string playerWorldError = string.Empty;

        public ConfigurationWindow()
        {
            this.helpers = new ConfigUIDelegates
            {
                OpenVoiceUnlockerAction = OpenWindow<VoiceUnlockerWindow>,
            };
        }

        public override void Draw(ref bool visible)
        {
            var titleBarColor = BackendManager.GetBackendTitleBarColor();
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, titleBarColor != default
                ? titleBarColor
                : ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32(ImGuiCol.TitleBgActive)));

            ImGui.SetNextWindowSize(new Vector2(520, 480), ImGuiCond.FirstUseEver);
            ImGui.Begin(
                $"TextToTalk Configuration (TTS {(this.Configuration.Enabled ? "Enabled" : "Disabled")})###TextToTalkConfig",
                ref visible);
            {
                if (ImGui.BeginTabBar("TextToTalk##tabbar"))
                {
                    if (ImGui.BeginTabItem("Synthesizer Settings"))
                    {
                        DrawSynthesizerSettings();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Player Voices"))
                    {
                        DrawPlayerVoiceSettings();
                        ImGui.EndTabItem();
                    }
                    else if (this.playerWorldEditing.Count > 0)
                    {
                        // Clear all user edits if the tab isn't selected anymore
                        this.playerWorldEditing = new Dictionary<Guid, string>();
                        this.playerWorldValid = new Dictionary<Guid, bool>();
                        this.playerName = string.Empty;
                        this.playerWorld = string.Empty;
                        this.playerWorldError = string.Empty;
                    }

                    if (ImGui.BeginTabItem("Channel Settings"))
                    {
                        DrawChannelSettings();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Triggers/Exclusions"))
                    {
                        DrawTriggersExclusions();
                        ImGui.EndTabItem();
                    }
                }

                ImGui.EndTabBar();
            }
            ImGui.End();

            ImGui.PopStyleColor();
        }

        private void DrawSynthesizerSettings()
        {
            if (ImGui.CollapsingHeader("Keybinds##TextToTalkKeybind1"))
            {
                var useKeybind = Configuration.UseKeybind;
                if (ImGui.Checkbox("Enable Keybind##TextToTalkKeybind2", ref useKeybind))
                {
                    Configuration.UseKeybind = useKeybind;
                    Configuration.Save();
                }

                ImGui.PushItemWidth(100f);
                var kItem1 = VirtualKey.EnumToIndex(Configuration.ModifierKey);
                if (ImGui.Combo("##TextToTalkKeybind3", ref kItem1, VirtualKey.Names.Take(3).ToArray(), 3))
                {
                    Configuration.ModifierKey = VirtualKey.IndexToEnum(kItem1);
                    Configuration.Save();
                }

                ImGui.SameLine();
                var kItem2 = VirtualKey.EnumToIndex(Configuration.MajorKey) - 3;
                if (ImGui.Combo("TTS Toggle Keybind##TextToTalkKeybind4", ref kItem2,
                        VirtualKey.Names.Skip(3).ToArray(), VirtualKey.Names.Length - 3))
                {
                    Configuration.MajorKey = VirtualKey.IndexToEnum(kItem2 + 3);
                    Configuration.Save();
                }

                ImGui.PopItemWidth();
            }

            if (ImGui.CollapsingHeader("General"))
            {
                var readFromQuestTalkAddon = Configuration.ReadFromQuestTalkAddon;
                if (ImGui.Checkbox("Read NPC dialogue from the dialogue window", ref readFromQuestTalkAddon))
                {
                    Configuration.ReadFromQuestTalkAddon = readFromQuestTalkAddon;
                    Configuration.Save();
                }

                if (readFromQuestTalkAddon)
                {
                    ImGui.Spacing();
                    ImGui.Indent();

                    var cancelSpeechOnTextAdvance = Configuration.CancelSpeechOnTextAdvance;
                    if (ImGui.Checkbox("Cancel the current NPC speech when new text is available or text is advanced",
                            ref cancelSpeechOnTextAdvance))
                    {
                        Configuration.CancelSpeechOnTextAdvance = cancelSpeechOnTextAdvance;
                        Configuration.Save();
                    }

                    ImGui.Unindent();
                }

                ImGui.Spacing();
                var enableNameWithSay = Configuration.EnableNameWithSay;
                if (ImGui.Checkbox("Enable \"X says:\" when people speak", ref enableNameWithSay))
                {
                    Configuration.EnableNameWithSay = enableNameWithSay;
                    Configuration.Save();
                }

                if (enableNameWithSay)
                {
                    ImGui.Spacing();
                    ImGui.Indent();

                    var nameNpcWithSay = Configuration.NameNpcWithSay;
                    if (ImGui.Checkbox("Also say \"NPC Name says:\" in NPC dialogue", ref nameNpcWithSay))
                    {
                        Configuration.NameNpcWithSay = nameNpcWithSay;
                        Configuration.Save();
                    }

                    var sayPlayerWorldName = Configuration.SayPlayerWorldName;
                    if (ImGui.Checkbox("Say player world name", ref sayPlayerWorldName))
                    {
                        Configuration.SayPlayerWorldName = sayPlayerWorldName;
                        Configuration.Save();
                    }

                    var disallowMultipleSay = Configuration.DisallowMultipleSay;
                    if (ImGui.Checkbox("Only say \"Character Name says:\" the first time a character speaks",
                            ref disallowMultipleSay))
                    {
                        Configuration.DisallowMultipleSay = disallowMultipleSay;
                        Configuration.Save();
                    }

                    var sayPartialName = Configuration.SayPartialName;
                    if (ImGui.Checkbox("Only say forename or surname", ref sayPartialName))
                    {
                        Configuration.SayPartialName = sayPartialName;
                        Configuration.Save();
                    }

                    if (sayPartialName)
                    {
                        ImGui.Spacing();
                        ImGui.Indent();

                        var onlySayFirstOrLastName = (int)Configuration.OnlySayFirstOrLastName;

                        if (ImGui.RadioButton("Only say forename", ref onlySayFirstOrLastName,
                                (int)FirstOrLastName.First))
                        {
                            Configuration.OnlySayFirstOrLastName = FirstOrLastName.First;
                            Configuration.Save();
                        }

                        if (ImGui.RadioButton("Only say surname", ref onlySayFirstOrLastName,
                                (int)FirstOrLastName.Last))
                        {
                            Configuration.OnlySayFirstOrLastName = FirstOrLastName.Last;
                            Configuration.Save();
                        }

                        ImGui.Unindent();
                    }

                    ImGui.Unindent();
                }

                var useRateLimiter = Configuration.UsePlayerRateLimiter;
                if (ImGui.Checkbox("Limit player TTS frequency", ref useRateLimiter))
                {
                    Configuration.UsePlayerRateLimiter = useRateLimiter;
                    Configuration.Save();
                }

                var messagesPerSecond = Configuration.MessagesPerSecond;
                if (useRateLimiter)
                {
                    ImGui.Indent();

                    if (ImGui.DragFloat("", ref messagesPerSecond, 0.1f, 0.1f, 30, "%.3f message(s)/s"))
                    {
                        Configuration.MessagesPerSecond = messagesPerSecond;
                    }

                    ImGui.Unindent();
                }
            }

            if (ImGui.CollapsingHeader("Voices##TTTVoicePre1", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var backends = Enum.GetValues<TTSBackend>();
                var backendsDisplay = backends.Select(b => b.GetFormattedName()).ToArray();
                var backend = Configuration.Backend;
                var backendIndex = Array.IndexOf(backends, backend);

                if (ImGui.Combo("Voice backend##TTTVoicePre2", ref backendIndex, backendsDisplay, backends.Length))
                {
                    var newBackend = backends[backendIndex];

                    Configuration.Backend = newBackend;
                    Configuration.Save();

                    BackendManager.SetBackend(newBackend);
                }

                if (!BackendManager.BackendLoading)
                {
                    // Draw the settings for the specific backend we're using.
                    BackendManager.DrawSettings(this.helpers);
                }
            }

            if (ImGui.CollapsingHeader("Experimental", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var removeStutterEnabled = Configuration.RemoveStutterEnabled;
                if (ImGui.Checkbox("Attempt to remove stutter from NPC dialogue (default: On)",
                        ref removeStutterEnabled))
                {
                    Configuration.RemoveStutterEnabled = removeStutterEnabled;
                    Configuration.Save();
                }
            }
        }

        private void DrawPlayerVoiceSettings()
        {
            ImGui.TextColored(HintColor, "Set specific voice presets for players using the options below.");

            ImGui.Spacing();

            var tableSize = new Vector2(0.0f, 300f);
            if (ImGui.BeginTable("##TTTPlayerVoiceList", 4, ImGuiTableFlags.Borders, tableSize))
            {
                ImGui.TableSetupScrollFreeze(0, 1); // Make top row always visible
                ImGui.TableSetupColumn("##TTTPlayerVoiceDelete", ImGuiTableColumnFlags.None, 30f);
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 280f);
                ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.None, 100f);
                ImGui.TableSetupColumn("Preset", ImGuiTableColumnFlags.None, 220f);
                ImGui.TableHeadersRow();

                var presets = Configuration.GetVoicePresetsForBackend(Configuration.Backend).ToList();
                presets.Sort((a, b) => a.Id - b.Id);
                var presetArray = presets.Select(p => p.Name).ToArray();

                var toDelete = new List<PlayerInfo>();
                foreach (var (id, playerInfo) in Configuration.Players)
                {
                    // Get player info fields
                    var name = playerInfo.Name;
                    if (!playerWorldEditing.TryGetValue(id, out var worldName))
                    {
                        var world = Data.GetExcelSheet<World>()?.GetRow(playerInfo.WorldId);
                        playerWorldEditing[id] = world?.Name.ToString() ?? "";
                    }

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);

                    ImGui.PushFont(UiBuilder.IconFont);
                    if (ImGui.Button($"{FontAwesomeIcon.Trash.ToIconString()}##TTTPlayerVoiceDelete-{id}"))
                    {
                        toDelete.Add(playerInfo);
                    }

                    ImGui.PopFont();

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("Delete");
                        ImGui.EndTooltip();
                    }

                    ImGui.TableSetColumnIndex(1);

                    // Allow player names to be edited in the table
                    if (ImGui.InputText($"##TTTPlayerName-{id}", ref name, 32))
                    {
                        playerInfo.Name = name;
                        Configuration.Save();
                        PluginLog.LogDebug($"Updated player name: {playerInfo.Name}@{worldName ?? ""}");
                    }

                    ImGui.TableSetColumnIndex(2);

                    // Allow player worlds to be edited in the table
                    worldName ??= "";
                    if (ImGui.InputText($"##TTTPlayerWorld-{id}", ref worldName, 32))
                    {
                        this.playerWorldEditing[id] = worldName;

                        // Try to get the input world
                        var worldPending = GetWorldForUserInput(worldName);

                        // Only save the result if the name actually matches a world
                        if (worldPending != null)
                        {
                            this.playerWorldValid[id] = true;
                            playerInfo.WorldId = worldPending.RowId;
                            Configuration.Save();
                            PluginLog.LogDebug($"Updated player world: {playerInfo.Name}@{worldPending.Name}");
                        }
                        else
                        {
                            this.playerWorldValid[id] = false;
                        }
                    }

                    // Indicate if the operation succeeded
                    if (this.playerWorldValid.TryGetValue(id, out var valid))
                    {
                        ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        if (valid)
                        {
                            ImGui.TextColored(Green, FontAwesomeIcon.CheckCircle.ToIconString());
                        }
                        else
                        {
                            ImGui.TextColored(Red, FontAwesomeIcon.MinusCircle.ToIconString());
                        }

                        ImGui.PopFont();
                    }

                    // Player voice dropdown
                    var presetIndex = Players.TryGetPlayerVoice(playerInfo, out var v) ? presets.IndexOf(v) : 0;
                    ImGui.TableSetColumnIndex(3);
                    if (ImGui.Combo($"##TTTPlayerVoice-{id}", ref presetIndex, presetArray, presets.Count))
                    {
                        Players.SetPlayerVoice(playerInfo, presets[presetIndex]);
                        Configuration.Save();
                        PluginLog.LogDebug($"Updated voice for {name}@{worldName}: {presets[presetIndex].Name}");
                    }
                }

                foreach (var playerInfo in toDelete)
                {
                    Players.DeletePlayer(playerInfo);
                }

                if (toDelete.Any())
                {
                    Configuration.Save();
                }

                ImGui.EndTable();
            }

            ImGui.InputText("Player name##TTTPlayerVoiceName", ref this.playerName, 32);
            ImGui.InputText("Player world##TTTPlayerVoiceWorld", ref this.playerWorld, 32);
            if (!string.IsNullOrEmpty(this.playerWorldError))
            {
                ImGui.TextColored(Red, this.playerWorldError);
            }

            if (ImGui.Button("Add player##TTTPlayerVoiceAdd"))
            {
                // Validate data before saving the new player
                var world = GetWorldForUserInput(this.playerWorld);
                if (world != null && Players.AddPlayer(this.playerName, world.RowId))
                {
                    Configuration.Save();
                    PluginLog.Log($"Added player: {this.playerName}@{world.Name}");
                }
                else if (world == null)
                {
                    this.playerWorldError = "Unknown world.";
                    PluginLog.LogError("The provided world name was invalid");
                }
                else
                {
                    this.playerWorldError = "Failed to add player - is this a duplicate?";
                    PluginLog.LogError("Failed to add player; this might be a duplicate entry");
                }
            }
        }

        private World GetWorldForUserInput(string worldName)
        {
            return Data.GetExcelSheet<World>()?
                .Where(w => w.IsPublic)
                .Where(w => !string.IsNullOrWhiteSpace(w.Name))
                .FirstOrDefault(w =>
                    string.Equals(w.Name.RawString, worldName, StringComparison.InvariantCultureIgnoreCase));
        }

        private void DrawChannelSettings()
        {
            var currentEnabledChatTypesPreset = Configuration.GetCurrentEnabledChatTypesPreset();

            var presets = Configuration.EnabledChatTypesPresets.ToList();
            presets.Sort((a, b) => a.Id - b.Id);
            var presetIndex = presets.IndexOf(currentEnabledChatTypesPreset);
            if (ImGui.Combo("Preset##TTT1", ref presetIndex, presets.Select(p => p.Name).ToArray(), presets.Count))
            {
                Configuration.CurrentPresetId = presets[presetIndex].Id;
                Configuration.Save();
            }

            if (ImGui.Button("New preset##TTT2"))
            {
                var newPreset = Configuration.NewChatTypesPreset();
                Configuration.SetCurrentEnabledChatTypesPreset(newPreset.Id);
                OpenWindow<ChannelPresetModificationWindow>();
            }

            ImGui.SameLine();

            if (ImGui.Button("Edit##TTT3"))
            {
                OpenWindow<ChannelPresetModificationWindow>();
            }

            if (Configuration.EnabledChatTypesPresets.Count > 1)
            {
                ImGui.SameLine();
                if (ImGui.Button("Delete##TTT4"))
                {
                    var otherPreset =
                        Configuration.EnabledChatTypesPresets.First(p => p.Id != currentEnabledChatTypesPreset.Id);
                    Configuration.SetCurrentEnabledChatTypesPreset(otherPreset.Id);
                    Configuration.EnabledChatTypesPresets.Remove(currentEnabledChatTypesPreset);
                }
            }

            ImGui.Spacing();

            ImGui.TextColored(new Vector4(1.0f, 1.0f, 1.0f, 0.6f), "Recommended for trigger use");
            var enableAll = currentEnabledChatTypesPreset.EnableAllChatTypes;
            if (ImGui.Checkbox("Enable all (including undocumented)", ref enableAll))
            {
                currentEnabledChatTypesPreset.EnableAllChatTypes = enableAll;
            }

            if (enableAll) return;
            ImGui.Spacing();

            var channels = Enum.GetNames(typeof(XivChatType)).Concat(Enum.GetNames(typeof(AdditionalChatType)));
            foreach (var channel in channels)
            {
                XivChatType enumValue;
                try
                {
                    enumValue = (XivChatType)Enum.Parse(typeof(XivChatType), channel);
                }
                catch (ArgumentException)
                {
                    enumValue = (XivChatType)(int)Enum.Parse(typeof(AdditionalChatType), channel);
                }

                var selected = currentEnabledChatTypesPreset.EnabledChatTypes.Contains((int)enumValue);
                if (!ImGui.Checkbox(channel == "PvPTeam" ? "PvP Team" : SplitWords(channel), ref selected)) continue;
                var inEnabled = currentEnabledChatTypesPreset.EnabledChatTypes.Contains((int)enumValue);
                if (inEnabled)
                {
                    currentEnabledChatTypesPreset.EnabledChatTypes.Remove((int)enumValue);
                    Configuration.Save();
                }
                else
                {
                    currentEnabledChatTypesPreset.EnabledChatTypes.Add((int)enumValue);
                    Configuration.Save();
                }
            }
        }

        private static string SplitWords(string oneWord)
        {
            var words = oneWord
                .Select(c => c)
                .Skip(1)
                .Aggregate("" + oneWord[0],
                    (acc, c) => acc + (c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' ? " " + c : "" + c))
                .Split(' ');

            var finalWords = new StringBuilder(oneWord.Length + 3);
            for (var i = 0; i < words.Length - 1; i++)
            {
                finalWords.Append(words[i]);
                if (words[i].Length == 1 && words[i + 1].Length == 1)
                {
                    continue;
                }

                finalWords.Append(" ");
            }

            return finalWords.Append(words.Last()).ToString();
        }

        private void DrawTriggersExclusions()
        {
            var currentConfiguration = Configuration.GetCurrentEnabledChatTypesPreset();

            var enableAll = currentConfiguration.EnableAllChatTypes;
            if (ImGui.Checkbox("Enable all chat types (including undocumented)", ref enableAll))
            {
                currentConfiguration.EnableAllChatTypes = enableAll;
                Configuration.Save();
            }

            ImGui.TextColored(new Vector4(1.0f, 1.0f, 1.0f, 0.6f), "Recommended for trigger use");
            ImGui.Dummy(new Vector2(0, 5));

            ExpandyList("Trigger", Configuration.Good);
            ExpandyList("Exclusion", Configuration.Bad);
        }

        private void ExpandyList(string kind, IList<Trigger> listItems)
        {
            ImGui.Text($"{kind}s");

            for (var i = 0; i < listItems.Count; i++)
            {
                var str = listItems[i].Text;
                if (ImGui.InputTextWithHint($"###TextToTalk{kind}{i}", $"Enter {kind} here...", ref str, 100))
                {
                    listItems[i].Text = str;
                    Configuration.Save();
                }

                ImGui.SameLine();
                var isRegex = listItems[i].IsRegex;
                if (ImGui.Checkbox($"Regex###TextToTalkRegex{kind}{i}", ref isRegex))
                {
                    listItems[i].IsRegex = isRegex;
                    Configuration.Save();
                }

                ImGui.SameLine();
                if (ImGui.Button($"Remove###TextToTalkRemove{kind}{i}"))
                {
                    listItems[i].ShouldRemove = true;
                }
            }

            for (var j = 0; j < listItems.Count; j++)
            {
                if (listItems[j].ShouldRemove)
                {
                    listItems.RemoveAt(j);
                    Configuration.Save();
                }
            }

            if (ImGui.Button($"Add {kind}"))
            {
                listItems.Add(new Trigger());
            }
        }
    }
}