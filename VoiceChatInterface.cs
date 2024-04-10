﻿using OpusDotNet;
using RNNoise.NET;
using System;
using WebRtcVadSharp;

namespace OpenVoiceSharp
{
    public sealed class VoiceChatInterface
    {
        public const int FrameLength = 20; // 20 ms, for max compatibility
        public const int SampleRate = 48000; // base opus and RNNoise frequency and webrtc
        public const int DefaultBitrate = 16000; // 16kbps, decent enough for voice chatting

        // properties

        /// <summary>
        /// Defines the quality of the audio data.
        /// A good quality bitrate for voice chatting could be 16 kbps (16000).
        /// </summary>
        public int Bitrate { get; private set; } = DefaultBitrate;
        /// <summary>
        /// Defines if the audio will be in stereo or not. If the input data doesnt match the channel count,
        /// it will be forced into if this is enabled or not.
        /// </summary>
        public bool Stereo { get; private set; } = false;
        /// <summary>
        /// Defines if noise suppression (RNNoise) is enabled. RNNoise runs on CPU.
        /// Disable if this is using too much usage on lower spec devices.
        /// </summary>
        public bool EnableNoiseSuppression { get; set; } = true;
        /// <summary>
        /// Defines if opus should favor the audio quality for audio streaming.
        /// Makes packets bigger with less loss but is useless for simple voice chatting and discussion.
        /// </summary>
        public bool FavorAudioStreaming { get; private set; } = false;

        public int GetChannelsAmount() => Stereo ? 2 : 1;

        // instances
        private readonly OpusEncoder OpusEncoder;
        private readonly OpusDecoder OpusDecoder;
        private readonly Denoiser Denoiser = new();
        private readonly WebRtcVad VoiceActivityDetector = new()
        {
            FrameLength = WebRtcVadSharp.FrameLength.Is20ms,
            SampleRate = WebRtcVadSharp.SampleRate.Is48kHz
        };

        
        /// <summary>
        /// Returns if voice activity was detected using the WebRTC VAD.
        /// </summary>
        /// <param name="pcmData">The raw pcm frame (in 16 bit PCM)</param>
        /// <returns>If voice activity was detected in the frame.</returns>
        public bool IsSpeaking(byte[] pcmData) => VoiceActivityDetector.HasSpeech(pcmData);


        private readonly float[] FloatSamples;

        private void ApplyNoiseSuppression(byte[] pcmData)
        {
            // convert to float32
            VoiceUtilities.Convert16BitToFloat(pcmData, FloatSamples);

            // apply noise suppression
            Denoiser.Denoise(FloatSamples);

            // convert back to 16 bit pcm
            VoiceUtilities.ConvertFloatTo16Bit(FloatSamples, pcmData);
        }

        /// <summary>
        /// Encodes and processes audio data. Also handles noise suppression if needed.
        /// </summary>
        /// <param name="pcmData">The 16 bit PCM data according to your needs.</param>
        /// <returns>Encoded Opus data.</returns>
        public (byte[] encodedOpusData, int encodedLength) SubmitAudioData(byte[] pcmData, int length)
        {
            if (EnableNoiseSuppression)
                ApplyNoiseSuppression(pcmData);
            
            return (OpusEncoder.Encode(pcmData, length, out int encodedLength), encodedLength);
        }

        public (byte[] decodedOpusData, int decodedLength) WhenDataReceived(byte[] encodedData, int length)
            => (OpusDecoder.Decode(encodedData, length, out int decodedLength), decodedLength);

        public VoiceChatInterface(
            int bitrate = DefaultBitrate, 
            bool stereo = false, 
            bool enableNoiseSuppression = true,
            bool favorAudioStreaming = false, 
            OperatingMode? vadOperatingMode = null
        ) {
            Bitrate = bitrate;
            Stereo = stereo;
            EnableNoiseSuppression = enableNoiseSuppression;
            FavorAudioStreaming = favorAudioStreaming;
            int channels = GetChannelsAmount();

            // fill float samples for noise suppression
            FloatSamples = new float[VoiceUtilities.GetSampleSize(SampleRate, FrameLength, channels) / 2];

            // create opus encoder/decoder
            OpusEncoder = new(
                FavorAudioStreaming ? Application.Audio : Application.VoIP,
                SampleRate,
                channels
            ) {
                Bitrate = Bitrate,
                VBR = false,
                ForceChannels = Stereo ? ForceChannels.Stereo : ForceChannels.Mono
            };

            OpusDecoder = new(FrameLength, SampleRate, channels);

            if (vadOperatingMode != null)
                VoiceActivityDetector.OperatingMode = (OperatingMode)vadOperatingMode;
        }
    }
}
