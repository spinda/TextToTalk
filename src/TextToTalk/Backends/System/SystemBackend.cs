﻿using System.Collections.Concurrent;
using System.Net.Http;
using TextToTalk.GameEnums;

namespace TextToTalk.Backends.System
{
    public class SystemBackend : VoiceBackend
    {
        private readonly PluginConfiguration config;
        private readonly SystemBackendUI ui;
        private readonly SystemSoundQueue soundQueue;

        public SystemBackend(PluginConfiguration config, HttpClient http)
        {
            var lexiconManager = new DalamudLexiconManager();
            LexiconUtils.LoadFromConfigSystem(lexiconManager, config);

            var selectVoiceFailures = new ConcurrentQueue<SelectVoiceFailedException>();

            this.ui = new SystemBackendUI(config, lexiconManager, selectVoiceFailures, http);

            this.config = config;
            this.soundQueue = new SystemSoundQueue(lexiconManager, selectVoiceFailures);
        }

        public override void Say(TextSource source, VoicePreset voice, string text)
        {
            this.soundQueue.EnqueueSound(voice, source, text);
        }

        public override void CancelAllSpeech()
        {
            this.soundQueue.CancelAllSounds();
        }

        public override void CancelSay(TextSource source)
        {
            this.soundQueue.CancelFromSource(source);
        }

        public override void DrawSettings(IConfigUIDelegates helpers)
        {
            this.ui.DrawSettings(helpers);
        }

        public override TextSource GetCurrentlySpokenTextSource()
        {
            return this.soundQueue.GetCurrentlySpokenTextSource();
        }

        public VoicePreset GetSystemVoiceForGender(Gender gender)
        {
            var voicePreset = this.config.GetCurrentVoicePreset<SystemVoicePreset>();
            if (this.config.UseGenderedVoicePresets)
            {
                voicePreset = gender switch
                {
                    Gender.Male => this.config.GetCurrentMaleVoicePreset<SystemVoicePreset>(),
                    Gender.Female => this.config.GetCurrentFemaleVoicePreset<SystemVoicePreset>(),
                    _ => this.config.GetCurrentUngenderedVoicePreset<SystemVoicePreset>(),
                };
            }

            return voicePreset;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.soundQueue.Dispose();
            }
        }
    }
}