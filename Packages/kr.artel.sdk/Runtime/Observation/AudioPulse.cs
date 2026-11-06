#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Artel.Affordances.Live;
using Artel.Protocol.Dto;
using Newtonsoft.Json;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// <c>PULSE</c> 에 <c>audio</c> 를 더한다(ARTEL-1224).
    /// </summary>
    /// <remarks>
    /// <c>AudioSource</c> 는 Unity 내장 <c>Behaviour</c> 라 scan 이 읽지 않는다(ADR 0003). 그래서 <c>pulse</c> 를 쓸 때마다
    /// 씬의 <c>AudioSource</c> 를 찾아 <c>isPlaying</c> 을 본다. 직전에 재생 중이 아니었거나, loop 가 아닌데 <c>time</c> 이
    /// 직전보다 작아졌으면(다시 <c>Play</c> 됨) 시작한 것으로 센다.
    ///
    /// 재는 간격이 <c>pulse</c> 간격(0.1 초)이라, 그 사이에 시작해서 끝난 짧은 소리는 보이지 않는다.
    /// <c>PlayOneShot</c> 은 쉬던 source 에서는 <c>isPlaying</c> 을 올려 시작으로 잡히지만 <c>clip</c> 이 비어 이름이 null 이다.
    /// 이미 재생 중인 source 위의 <c>PlayOneShot</c> 은 잡히지 않는다. clip 없이 붙은 source 는 playOnAwake 때문에
    /// <c>isPlaying</c> 이 계속 true 로 남아(2022.3.34f1) 그 위의 <c>PlayOneShot</c> 은 전부 보이지 않는다. 호출부를 후킹하는
    /// 일은 IL post-processor 의 몫이라 여기서 다루지 않는다.
    ///
    /// 처음 재는 순간 이미 재생 중인 것은 시작으로 세지 않는다. 언제 시작했는지 모르는 BGM 에 지금 frame 을 붙이면 틀린 말이
    /// 된다. 그것은 <c>playing</c> 수에만 들어간다.
    /// </remarks>
    internal sealed class AudioPulse : IPulseAttachment
    {
        internal const int MaxStartedPerPulse = 20;

        private readonly Func<IReadOnlyList<AudioSource>> findSources;
        private readonly Func<int> currentFrame;

        private Dictionary<int, float> previousTimes;
        private int previousPlaying = -1;

        public AudioPulse(Func<IReadOnlyList<AudioSource>> findSources = null, Func<int> currentFrame = null)
        {
            this.findSources = findSources ??
                               (() => UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None));
            this.currentFrame = currentFrame ?? (() => Time.frameCount);
        }

        public string Compose(bool whole, out bool news)
        {
            var sample = Sample();
            news = sample.Started.Count > 0 || sample.Playing != previousPlaying;
            previousPlaying = sample.Playing;

            if (!news && !whole)
            {
                return null;
            }

            return ",\"audio\":" + JsonConvert.SerializeObject(sample);
        }

        private AudioPulseDto Sample()
        {
            var seeding = previousTimes == null;
            var nowTimes = new Dictionary<int, float>();
            var result = new AudioPulseDto();
            var frame = currentFrame();

            foreach (var source in findSources())
            {
                if (source == null || !source.isPlaying)
                {
                    continue;
                }

                var id = source.GetInstanceID();
                var time = source.time;
                nowTimes[id] = time;

                if (seeding || !StartedSince(source, id, time))
                {
                    continue;
                }

                if (result.Started.Count >= MaxStartedPerPulse)
                {
                    result.Truncated = true;
                    continue;
                }

                result.Started.Add(new AudioStartDto
                {
                    Clip = source.clip != null ? source.clip.name : null,
                    Frame = frame
                });
            }

            result.Playing = nowTimes.Count;
            previousTimes = nowTimes;
            return result;
        }

        private bool StartedSince(AudioSource source, int id, float time)
        {
            if (!previousTimes.TryGetValue(id, out var before))
            {
                return true;
            }

            // loop 하는 소리는 한 바퀴마다 time 이 0 으로 돌아간다. 그것을 새 시작으로 세면 BGM 이 몇 초마다 시작한다.
            return !source.loop && time < before;
        }
    }
}
#endif
