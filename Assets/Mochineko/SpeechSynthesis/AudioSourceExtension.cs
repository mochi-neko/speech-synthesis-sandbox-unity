#nullable enable
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mochineko.SpeechSynthesis
{
    internal static class AudioSourceExtension
    {
        public static async UniTask PlayAsync(
            this AudioSource audioSource,
            AudioClip audioClip,
            CancellationToken cancellationToken)
        {
            await UniTask.SwitchToMainThread();
            
            audioSource.Stop();
            audioSource.clip = audioClip;
            Debug.Log($"Play {audioClip.name}.");
            audioSource.Play();

            try
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(audioClip.length),
                    cancellationToken: cancellationToken);

                audioSource.Stop();
            }
            catch (OperationCanceledException)
            {
                audioSource.Stop();
            }
        }
    }
}