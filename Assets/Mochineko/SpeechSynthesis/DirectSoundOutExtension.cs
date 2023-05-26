#nullable enable
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NAudio.Wave;

namespace Mochineko.SpeechSynthesis
{
    internal static class DirectSoundOutExtension
    {
        public static async UniTask PlayAsync(
            this DirectSoundOut device,
            CancellationToken cancellationToken)
        {
            device.Stop();

            device.Play();

            try
            {
                await UniTask.WaitUntil(
                    () => device.PlaybackState == PlaybackState.Stopped,
                    cancellationToken: cancellationToken);
                
                device.Stop();
            }
            catch (OperationCanceledException)
            {
                device.Stop();
            }
        }
    }
}