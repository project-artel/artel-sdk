#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Artel.Affordances.Live;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// <see cref="LiveState.Attachment"/> 자리 하나에 여러 조각을 꽂는다.
    /// </summary>
    /// <remarks>
    /// 자리는 하나인데 <c>PULSE</c> 에 필드를 더하는 쪽은 cheat·behavior, 예외 로그, 소리 셋이다. 조각 하나가 던져도
    /// 나머지는 실린다. <see cref="LiveState"/> 가 던지는 조각을 통째로 빼므로, 여기서 따로 잡지 않으면 한 조각의 실패가
    /// 다른 조각까지 지운다.
    /// </remarks>
    internal sealed class PulseAttachmentSet : IPulseAttachment
    {
        private readonly List<IPulseAttachment> attachments;

        public PulseAttachmentSet(params IPulseAttachment[] attachments)
        {
            this.attachments = new List<IPulseAttachment>(attachments ?? Array.Empty<IPulseAttachment>());
        }

        public string Compose(bool whole, out bool news)
        {
            news = false;
            string combined = null;

            foreach (var attachment in attachments)
            {
                if (attachment == null)
                {
                    continue;
                }

                string fragment;
                bool fragmentNews;
                try
                {
                    fragment = attachment.Compose(whole, out fragmentNews);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Artel] A pulse attachment could not be written: " + exception.Message);
                    continue;
                }

                news |= fragmentNews;
                if (fragment != null)
                {
                    combined += fragment;
                }
            }

            return combined;
        }
    }
}
#endif
