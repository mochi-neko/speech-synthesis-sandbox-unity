#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Cysharp.Threading.Tasks;
using Mochineko.KoeiromapAPI;
using Mochineko.Relent.UncertainResult;
using Mochineko.SimpleAudioCodec;
using Mochineko.VOICEVOX_API;
using Mochineko.VOICEVOX_API.QueryCreation;
using Mochineko.VOICEVOX_API.Synthesis;
using NAudio.Wave;
using UnityEngine;
using UnityEngine.Assertions;

namespace Mochineko.SpeechSynthesis
{
    internal sealed class Demonstrator : MonoBehaviour
    {
        [SerializeField, TextArea(3, 10)]
        private string serif = string.Empty;

        [SerializeField]
        private SynthesisBackend synthesisBackend = SynthesisBackend.VoiceVox;

        [SerializeField]
        private PlayerBackend playerBackend = PlayerBackend.AudioSource;

        [SerializeField]
        private string voicevoxBaseURL = "http://127.0.0.1:50021";

        [SerializeField]
        private int voicevoxSpeakerId = 0;

        [SerializeField, Range(-3f, 3f)]
        private float koeiromapSpeakerX = 0f;

        [SerializeField, Range(-3f, 3f)]
        private float koeiromapSpeakerY = 0f;

        [SerializeField, Range(-3f, 3f)]
        private Style koeiromapStyle = Style.Talk;

        [SerializeField]
        private AudioSource? audioSource = default;

        [SerializeField]
        private int directSoundDeviceID = 0;

        [SerializeField]
        private List<DirectSoundDeviceInfo> directSoundDevices = new();

        private static readonly HttpClient HttpClient = new();

        private AudioClip? audioClip = default;
        private DirectSoundOut? directSoundDevice = default;

        private void Awake()
        {
            Assert.IsNotNull(audioSource);
        }

        private void Start()
        {
            UpdateDevices();
        }

        private void OnDestroy()
        {
            if (audioClip != null)
            {
                UnityEngine.Object.Destroy(audioClip);
            }

            directSoundDevice?.Dispose();
        }

        [ContextMenu(nameof(Synthesize))]
        public void Synthesize()
        {
            SynthesizeSpeechAsync(this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        [ContextMenu(nameof(Replay))]
        public void Replay()
        {
            switch (playerBackend)
            {
                case PlayerBackend.AudioSource:
                    ReplayByAudioSource();
                    break;

                case PlayerBackend.DirectSound:
                    ReplayByDirectSound();
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(playerBackend));
            }
        }

        private void ReplayByAudioSource()
        {
            if (audioSource == null)
            {
                throw new NullReferenceException(nameof(audioSource));
            }

            if (audioClip == null)
            {
                Debug.Log($"[SpeechSynthesis] No audio clip to replay.");
                return;
            }

            audioSource
                .PlayAsync(audioClip, this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        private void ReplayByDirectSound()
        {
            if (directSoundDevice == null)
            {
                Debug.Log($"[SpeechSynthesis] No direct sound device to replay.");
                return;
            }

            directSoundDevice
                .PlayAsync(this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        [ContextMenu(nameof(UpdateDevices))]
        public void UpdateDevices()
        {
            directSoundDevices = DirectSoundOut.Devices.ToList();
        }

        private async UniTask SynthesizeSpeechAsync(CancellationToken cancellationToken)
        {
            switch (synthesisBackend)
            {
                case SynthesisBackend.VoiceVox:
                    await SynthesizeSpeechByVoicevoxAsync(cancellationToken);
                    break;

                case SynthesisBackend.Koeiromap:
                    await SynthesizeSpeechByKoeiromapAsync(cancellationToken);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(synthesisBackend));
            }
        }

        private async UniTask SynthesizeSpeechByVoicevoxAsync(CancellationToken cancellationToken)
        {
            await UniTask.SwitchToThreadPool();

            VoiceVoxBaseURL.BaseURL = voicevoxBaseURL;

            var createQueryResult = await QueryCreationAPI.CreateQueryAsync(
                HttpClient,
                serif,
                voicevoxSpeakerId,
                coreVersion: null,
                cancellationToken
            );

            AudioQuery audioQuery;
            switch (createQueryResult)
            {
                case IUncertainSuccessResult<AudioQuery> createQuerySuccess:
                    Debug.Log($"[SpeechSynthesis] Succeeded to create audio query by VOICEVOX from text:{serif}.");
                    audioQuery = createQuerySuccess.Result;
                    break;

                case IUncertainRetryableResult<AudioQuery> createQueryRetryable:
                    Debug.LogError(
                        $"[SpeechSynthesis] Failed to create audio query of VOICEVOX because -> {createQueryRetryable.Message}.");
                    return;

                case IUncertainFailureResult<AudioQuery> createQueryFailure:
                    Debug.LogError(
                        $"[SpeechSynthesis] Failed to create audio query of VOICEVOX because -> {createQueryFailure.Message}.");
                    return;

                default:
                    throw new UncertainResultPatternMatchException(nameof(createQueryResult));
            }

            Stream stream;
            var synthesizeResult = await SynthesisAPI.SynthesizeAsync(
                HttpClientPool.PooledClient,
                audioQuery: audioQuery,
                speaker: voicevoxSpeakerId,
                enableInterrogativeUpspeak: null,
                coreVersion: null,
                cancellationToken: cancellationToken
            );
            switch (synthesizeResult)
            {
                case IUncertainSuccessResult<Stream> synthesizeSuccess:
                    Debug.Log($"[SpeechSynthesis] Succeeded to synthesize speech by VOICEVOX from text:{serif}.");
                    stream = synthesizeSuccess.Result;
                    break;

                case IUncertainRetryableResult<Stream> synthesizeRetryable:
                    Debug.LogError(
                        $"[SpeechSynthesis] Failed to synthesize speech by VOICEVOX because -> {synthesizeRetryable.Message}.");
                    return;

                case IUncertainFailureResult<Stream> synthesizeFailure:
                    Debug.LogError(
                        $"[SpeechSynthesis] Failed to synthesize speech by VOICEVOX because -> {synthesizeFailure.Message}.");
                    return;

                default:
                    throw new UncertainResultPatternMatchException(nameof(synthesizeResult));
            }

            await PlayAsync(stream, cancellationToken);
        }

        private async UniTask SynthesizeSpeechByKoeiromapAsync(CancellationToken cancellationToken)
        {
            await UniTask.SwitchToThreadPool();

            var synthesisResult = await KoeiromapAPI.SpeechSynthesisAPI
                .SynthesizeSpeechAsync(
                    HttpClient,
                    serif,
                    cancellationToken,
                    speakerX: koeiromapSpeakerX,
                    speakerY: koeiromapSpeakerY,
                    style: Style.Talk
                );

            Stream stream;
            switch (synthesisResult)
            {
                case IUncertainSuccessResult<SpeechSynthesisResult> synthesisSuccess:
                    Debug.Log($"[SpeechSynthesis] Succeeded to synthesis speech by Koeiromap from text:{serif}.");
                    stream = synthesisSuccess.Result.Audio;
                    break;

                case IUncertainRetryableResult<SpeechSynthesisResult> synthesisRetryable:
                    Debug.LogError(
                        $"[SpeechSynthesis] Failed to synthesis speech bu Koeiromap because -> {synthesisRetryable.Message}.");
                    return;

                case IUncertainFailureResult<SpeechSynthesisResult> synthesisFailure:
                    Debug.LogError(
                        $"[SpeechSynthesis] Failed to synthesis speech bu Koeiromap because -> {synthesisFailure.Message}.");
                    return;

                default:
                    throw new UncertainResultPatternMatchException(nameof(synthesisResult));
            }

            await PlayAsync(stream, cancellationToken);
        }

        private async UniTask PlayAsync(Stream stream, CancellationToken cancellationToken)
        {
            switch (playerBackend)
            {
                case PlayerBackend.AudioSource:
                    await PlayByAudioSourceAsync(stream, cancellationToken);
                    break;

                case PlayerBackend.DirectSound:
                    await PlayByDirectSoundAsync(stream, cancellationToken);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(playerBackend));
            }
        }

        private async UniTask PlayByAudioSourceAsync(Stream stream, CancellationToken cancellationToken)
        {
            if (audioSource == null)
            {
                throw new NullReferenceException(nameof(audioSource));
            }

            if (audioClip != null)
            {
                UnityEngine.Object.Destroy(audioClip);
            }

            try
            {
                // Decode WAV data to AudioClip by SimpleAudioCodec WAV decoder.
                audioClip = await WaveDecoder.DecodeByBlockAsync(
                    stream: stream,
                    fileName: "Synthesis.wav",
                    cancellationToken: cancellationToken
                );

                Debug.Log($"[SpeechSynthesis] Succeeded to decode audio, " +
                          $"samples:{audioClip.samples}, " +
                          $"frequency:{audioClip.frequency}, " +
                          $"channels:{audioClip.channels}, " +
                          $"length:{audioClip.length}.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SpeechSynthesis] Failed to decode audio because -> {e}.");
                return;
            }
            finally
            {
                await stream.DisposeAsync();
            }

            await audioSource.PlayAsync(audioClip, cancellationToken);
        }

        private async UniTask PlayByDirectSoundAsync(Stream stream, CancellationToken cancellationToken)
        {
            directSoundDevice?.Dispose();
            
            directSoundDevice = new DirectSoundOut(device: directSoundDevices[directSoundDeviceID].Guid);
            var reader = new WaveFileReader(stream);

            directSoundDevice.Init(reader);

            await directSoundDevice.PlayAsync(cancellationToken);
        }
    }
}